using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace SimpleFileBrowser
{
	public partial class FileBrowser
	{
		private const int PreviewMaxFileBytes = 32 * 1024 * 1024;
		private const int PreviewMaxEdge = 512;
		private bool showImagePreview;
		private RectTransform previewPanel;
		private UnityEngine.UI.Image quickLinksPlate;
		private UnityEngine.UI.RawImage previewImage;
		private TMP_Text previewTitle, previewStatus;
		private RenderTexture previewTexture;
		private Coroutine previewLoad;
		private UnityWebRequest previewRequest;
		private string previewPath;

#if UNITY_EDITOR_WIN || ( !UNITY_EDITOR && UNITY_STANDALONE_WIN )
		[DllImport( "shell32.dll" )]
		private static extern int SHGetKnownFolderPath( ref Guid folderId, uint flags, IntPtr token, out IntPtr path );
#endif

		private static string GetDownloadsPath()
		{
#if UNITY_EDITOR_WIN || ( !UNITY_EDITOR && UNITY_STANDALONE_WIN )
			var folderId = new Guid( "374DE290-123F-4565-9164-39C4925E467B" );
			int result = SHGetKnownFolderPath( ref folderId, 0, IntPtr.Zero, out var path );
			try
			{
				if( result == 0 && path != IntPtr.Zero ) return Marshal.PtrToStringUni( path );
			}
			finally { Marshal.FreeCoTaskMem( path ); }
#endif
			string home = Environment.GetFolderPath( Environment.SpecialFolder.UserProfile );
			return string.IsNullOrEmpty( home ) ? null : Path.Combine( home, "Downloads" );
		}

		private void StyleNavigationButton( UnityEngine.UI.Button button, string key )
		{
			var iconRect = (RectTransform) button.transform.Find( "NavigationIcon" );
			if( !iconRect )
			{
				iconRect = CreatePreviewElement( "NavigationIcon", button.transform );
				iconRect.anchorMin = iconRect.anchorMax = Vector2.one * .5f;
				iconRect.sizeDelta = Vector2.one * 19;
				iconRect.localRotation = button.transform.localRotation;
				button.transform.localRotation = Quaternion.identity;
				var newIcon = iconRect.gameObject.AddComponent<UnityEngine.UI.Image>();
				newIcon.raycastTarget = false;
				newIcon.preserveAspect = true;
			}

			var icon = iconRect.GetComponent<UnityEngine.UI.Image>();
			icon.sprite = button.image.sprite;
			icon.color = m_skin.HeaderButtonsColor;
			ApplyIconStyle( icon, key, m_skin.HeaderButtonsColor );
			button.image.sprite = null;
			button.image.color = m_skin.ButtonColor;
			ApplySurfaceStyle( button.image, 6, true );
		}

		internal static bool IsImageFile( string name )
		{
			switch( Path.GetExtension( name ).ToLowerInvariant() )
			{
				case ".png":
				case ".jpg":
				case ".jpeg":
				case ".bmp":
				case ".tga":
					return true;
				default:
					return false;
			}
		}

		private void ConfigureImagePreview()
		{
			ClearImagePreview();
			if( filtersDropdown.options.Count > 0 && filters.Count == 1 && filters[0] == allFilesFilter )
			{
				filtersDropdown.options[0].text = showImagePreview ? "Изображения" : AllFilesFilterText;
				filtersDropdown.RefreshShownValue();
			}
			if( showImagePreview && !previewPanel )
			{
				previewPanel = CreatePreviewElement( "ImagePreview", middleView.transform );
				var background = previewPanel.gameObject.AddComponent<UnityEngine.UI.Image>();
				background.raycastTarget = false;
				previewTitle = CreatePreviewLabel( "PreviewTitle", "Предпросмотр", TextAlignmentOptions.MidlineLeft );
				previewTitle.fontStyle = FontStyles.Bold;
				SetPreviewBounds( previewTitle.rectTransform, new Vector2( 0, 1 ), Vector2.one, new Vector2( 14, -34 ), new Vector2( -14, -4 ) );

				var image = CreatePreviewElement( "PreviewImage", previewPanel );
				image.anchorMin = image.anchorMax = Vector2.one * .5f;
				previewImage = image.gameObject.AddComponent<UnityEngine.UI.RawImage>();
				previewImage.raycastTarget = false;
				previewStatus = CreatePreviewLabel( "PreviewStatus", "", TextAlignmentOptions.Center );
				SetPreviewBounds( previewStatus.rectTransform, Vector2.zero, Vector2.one, new Vector2( 12, 12 ), new Vector2( -12, -36 ) );
				RefreshPreviewSkin();
			}

			if( previewPanel )
			{
				previewPanel.gameObject.SetActive( showImagePreview );
				previewStatus.text = "Выберите изображение";
			}
			OnWindowDimensionsChanged( windowTR.sizeDelta );
		}

		private void RefreshPreviewSkin()
		{
			if( !quickLinksPlate )
			{
				var plate = CreatePreviewElement( "QuickLinksPlate", middleViewQuickLinks );
				plate.SetAsFirstSibling();
				SetPreviewBounds( plate, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero );
				quickLinksPlate = plate.gameObject.AddComponent<UnityEngine.UI.Image>();
				quickLinksPlate.raycastTarget = false;
			}
			quickLinksPlate.color = m_skin.WindowColor;
			ApplySurfaceStyle( quickLinksPlate, 7 );

			if( !previewPanel ) return;
			var background = previewPanel.GetComponent<UnityEngine.UI.Image>();
			background.color = m_skin.WindowColor;
			ApplySurfaceStyle( background, 8, true );
			m_skin.ApplyTo( previewTitle, m_skin.FileNormalTextColor );
			ApplyTextStyle( previewTitle, true );
			m_skin.ApplyTo( previewStatus, m_skin.InputFieldPlaceholderTextColor );
		}

		private static RectTransform CreatePreviewElement( string name, Transform parent )
		{
			var element = new GameObject( name, typeof( RectTransform ) ) { layer = parent.gameObject.layer };
			var rect = (RectTransform) element.transform;
			rect.SetParent( parent, false );
			return rect;
		}

		private TMP_Text CreatePreviewLabel( string name, string text, TextAlignmentOptions alignment )
		{
			var label = CreatePreviewElement( name, previewPanel ).gameObject.AddComponent<TextMeshProUGUI>();
			label.alignment = alignment;
			label.text = text;
			label.raycastTarget = false;
			label.richText = false;
			return label;
		}

		private static void SetPreviewBounds( RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax )
		{
			rect.anchorMin = min;
			rect.anchorMax = max;
			rect.offsetMin = offsetMin;
			rect.offsetMax = offsetMax;
		}

		private void LayoutFileColumns( float windowWidth, float quickLinksWidth )
		{
			bool docked = showImagePreview && previewPanel && previewPanel.gameObject.activeSelf;
			bool stack = docked && windowWidth < 650;
			float bottom = stack ? .43f : 0;
			float side = docked && !stack ? Mathf.Clamp( windowWidth * .29f, 190, 310 ) : 0;
			SetPreviewBounds( middleViewQuickLinks, new Vector2( 0, bottom ), new Vector2( 0, 1 ),
				new Vector2( 5, 5 ), new Vector2( quickLinksWidth - 5, -5 ) );
			SetPreviewBounds( middleViewFiles, new Vector2( 0, bottom ), Vector2.one,
				new Vector2( quickLinksWidth + 6, 5 ), new Vector2( -side - 6, -5 ) );
			SetPreviewBounds( middleViewSeparator.rectTransform, new Vector2( 0, bottom ), new Vector2( 0, 1 ),
				new Vector2( quickLinksWidth, 5 ), new Vector2( quickLinksWidth + 1, -5 ) );

			if( !docked ) return;
			if( stack )
				SetPreviewBounds( previewPanel, Vector2.zero, new Vector2( 1, bottom ), new Vector2( 5, 5 ), new Vector2( -5, -1 ) );
			else
				SetPreviewBounds( previewPanel, new Vector2( 1, 0 ), Vector2.one, new Vector2( -side + 1, 5 ), new Vector2( -5, -5 ) );
			ResizePreviewImage();
		}

		private void RefreshImagePreviewSelection()
		{
			if( !showImagePreview || !previewPanel || !previewPanel.gameObject.activeSelf ) return;
			string path = null;
			if( selectedFileEntries.Count == 1 )
			{
				var entry = validFileEntries[selectedFileEntries[0]];
				if( !entry.IsDirectory && IsImageFile( entry.Name ) ) path = entry.Path;
			}
			if( path != null && path == previewPath ) return;

			ClearImagePreview();
			previewPath = path;
			previewStatus.text = path == null ? "Выберите одно изображение" : "Загрузка…";
			if( path != null ) previewLoad = StartCoroutine( LoadImagePreview( path ) );
		}

		private IEnumerator LoadImagePreview( string path )
		{
			// A short delay avoids decoding files skipped during rapid selection.
			yield return new WaitForSecondsRealtime( .12f );
			string extension = Path.GetExtension( path ).ToLowerInvariant();
			if( extension != ".png" && extension != ".jpg" && extension != ".jpeg" )
			{
				previewStatus.text = "Предпросмотр доступен для PNG и JPEG";
				yield break;
			}

			try
			{
				var file = new FileInfo( path );
				if( file.Length > PreviewMaxFileBytes )
				{
					previewStatus.text = "Файл слишком большой для предпросмотра";
					yield break;
				}
				previewRequest = UnityWebRequestTexture.GetTexture( new Uri( file.FullName ).AbsoluteUri, true );
			}
			catch( Exception ex ) when( ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is UriFormatException )
			{
				previewStatus.text = "Не удалось открыть изображение";
			}
			if( previewRequest == null ) yield break;

			yield return previewRequest.SendWebRequest();
			if( previewRequest.result == UnityWebRequest.Result.Success )
			{
				var source = DownloadHandlerTexture.GetContent( previewRequest );
				if( source )
				{
					float scale = Mathf.Min( 1f, (float) PreviewMaxEdge / Mathf.Max( source.width, source.height ) );
					previewTexture = new RenderTexture( Mathf.Max( 1, Mathf.RoundToInt( source.width * scale ) ),
						Mathf.Max( 1, Mathf.RoundToInt( source.height * scale ) ), 0, RenderTextureFormat.ARGB32 );
					var previousTarget = RenderTexture.active;
					Graphics.Blit( source, previewTexture );
					RenderTexture.active = previousTarget;
					Destroy( source );
					previewImage.texture = previewTexture;
					previewImage.enabled = true;
					ResizePreviewImage();
				}
			}
			previewStatus.text = previewTexture ? "" : "Не удалось открыть изображение";
			previewRequest.Dispose();
			previewRequest = null;
			previewLoad = null;
		}

		private void ResizePreviewImage()
		{
			if( !previewTexture ) return;
			float width = Mathf.Max( 1, previewPanel.rect.width - 24 );
			float height = Mathf.Max( 1, previewPanel.rect.height - 46 );
			float scale = Mathf.Min( width / previewTexture.width, height / previewTexture.height );
			previewImage.rectTransform.sizeDelta = new Vector2( previewTexture.width, previewTexture.height ) * scale;
			previewImage.rectTransform.anchoredPosition = new Vector2( 0, -12 );
		}

		private void ClearImagePreview()
		{
			if( previewLoad != null ) StopCoroutine( previewLoad );
			previewLoad = null;
			if( previewRequest != null )
			{
				previewRequest.Abort();
				previewRequest.Dispose();
				previewRequest = null;
			}
			if( previewImage )
			{
				previewImage.texture = null;
				previewImage.enabled = false;
			}
			if( previewTexture )
			{
				previewTexture.Release();
				Destroy( previewTexture );
				previewTexture = null;
			}
			previewPath = null;
		}

		private void OnDisable() => ClearImagePreview();
	}
}
