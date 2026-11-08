using System;
using System.Collections.Generic;
using System.IO;
using SimpleFileBrowser;
using Unity.Netcode;
using UnityEngine;

public partial class DiceUI
{
    private enum FeedFilter { All, Rolls, Messages }

    private sealed class FeedRow
    {
        internal long Timestamp;
        internal bool IsRoll;
        internal GameObject Object;
        internal Texture2D Image;
    }

    private readonly List<FeedRow> _feedRows = new();
    private LobbyChat _chat;
    private RectTransform _feedContent;
    private UnityEngine.UI.ScrollRect _feedScroll;
    private UnityEngine.UI.Text _feedEmpty, _chatStatus, _hostUnread, _playerUnread;
    private UnityEngine.UI.Toggle _feedFilter, _messageFilter;
    private UnityEngine.UI.Button _chatAttach, _chatSend, _chatRemove;
    private UnityEngine.UI.RawImage _chatPreview;
    private RectTransform _chatPreviewRow;
    private Texture2D _chatPreviewTexture;
    private GameObject _feedImageModal;
    private UnityEngine.UI.RawImage _feedImageViewer;
    private Texture2D _viewedImage;
    private UnityEngine.UI.InputField _chatInput;
    private GameObject _chatComposer;
    private byte[] _chatAttachment;
    private int _unreadChat, _chatSession;
    private FeedFilter _activeFeedFilter;
    private bool _chatPending, _feedScrollToBottom;

    private void BuildSessionFeed()
    {
        var panel = AtlasUi.Panel(_nativeRoot, "SessionJournal", AtlasUi.Paper);
        _logPanelGO = panel.gameObject;
        var body = AtlasUi.Column(panel, "JournalBody", 12);
        AtlasUi.Fill(body);
        AtlasUi.Heading(body, "Журнал и чат", () => _logPanelGO.SetActive(false));
        BuildFeedFilter(body);

        _feedContent = AtlasUi.Scroll(body, "JournalDrawer", out _feedScroll, 8);
        _feedContent.anchorMin = Vector2.zero;
        _feedContent.anchorMax = new Vector2(1, 0);
        _feedContent.pivot = new Vector2(.5f, 0);
        _feedContent.anchoredPosition = Vector2.zero;
        _feedContent.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().spacing = 10;
        var feedLayout = _feedScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
        feedLayout.flexibleHeight = 1;
        feedLayout.minHeight = 70;
        _feedEmpty = AtlasUi.Text(_feedContent, "Здесь появятся сообщения и броски.", 14, AtlasUi.Muted);

        var composer = AtlasUi.Column(body, "ChatComposer", 0, 6);
        _chatComposer = composer.gameObject;
        _chatPreviewRow = AtlasUi.Row(composer, "AttachmentPreview", 42, 8);
        var preview = AtlasUi.Node(_chatPreviewRow, "Thumbnail");
        AtlasUi.Width(preview, 56);
        AtlasUi.Height(preview, 36);
        _chatPreview = AtlasUi.Node(preview, "Image").gameObject.AddComponent<UnityEngine.UI.RawImage>();
        AtlasUi.Fill(_chatPreview.rectTransform);
        _chatPreview.raycastTarget = false;
        _chatPreview.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>().aspectMode =
            UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
        var attached = AtlasUi.Text(_chatPreviewRow, "Фото прикреплено", 13, AtlasUi.Muted, height: 26);
        attached.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
        _chatRemove = AtlasUi.CloseButton(_chatPreviewRow, () =>
        {
            ClearChatAttachment();
            SetChatStatus("");
        });

        var line = AtlasUi.Row(composer, "MessageEntry", 36, 0);
        line.GetComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 0;
        line.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
        var background = line.gameObject.AddComponent<UnityEngine.UI.Image>();
        VttUiSkin.Surface(background, new Color(1f, .98f, .945f), 6);
        _chatAttach = BuildChatIconButton(line, "AttachImage", "paperclip", PickChatImage, false);
        _chatInput = AtlasUi.Input(line, "", "Сообщение…", height: 36);
        _chatInput.GetComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1;
        var inputBackground = _chatInput.GetComponent<UnityEngine.UI.Image>();
        inputBackground.color = Color.clear;
        inputBackground.canvasRenderer.cullTransparentMesh = false;
        _chatInput.transform.Find("Border").gameObject.SetActive(false);
        _chatInput.characterLimit = LobbyChat.TextLimit;
        _chatInput.onValidateInput = (_, _, character) => char.IsControl(character) ? '\0' : character;
        _chatInput.onSubmit.AddListener(_ => SendChatMessage());
        _chatSend = BuildChatIconButton(line, "SendMessage", "arrow-up", SendChatMessage, true);
        _chatStatus = AtlasUi.Paragraph(composer, "", 13);
        RefreshFeedFilter();
        SetChatStatus("");
        BuildFeedImageViewer();
    }

    private static UnityEngine.UI.Button BuildChatIconButton(RectTransform parent, string name, string key,
        UnityEngine.Events.UnityAction action, bool primary)
    {
        var slot = AtlasUi.Node(parent, name + "Slot");
        AtlasUi.Width(slot, 36);
        var button = AtlasUi.Button(slot, "", action, primary, height: 30);
        button.name = name;
        AtlasUi.Fill((RectTransform)button.transform, 3, 3, 3, 3);
        var glyph = AtlasUi.Node(button.transform, "Icon").gameObject.AddComponent<AtlasGlyph>();
        AtlasUi.Fill(glyph.rectTransform, 5, 5, 5, 5);
        glyph.key = key;
        glyph.color = primary ? AtlasUi.Light : AtlasUi.Ink;
        glyph.raycastTarget = false;
        return button;
    }

    private void BuildFeedFilter(RectTransform parent)
    {
        var row = AtlasUi.Row(parent, "RollFilterRow", 24);
        row.GetComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 0;
        row.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>().childForceExpandWidth = false;
        _feedFilter = BuildFeedFilterToggle(row, "RollsOnly", "Только броски", 132, FeedFilter.Rolls);
        _messageFilter = BuildFeedFilterToggle(row, "MessagesOnly", "Только сообщения", 158, FeedFilter.Messages);
    }

    private UnityEngine.UI.Toggle BuildFeedFilterToggle(RectTransform row, string name, string caption,
        float width, FeedFilter filter)
    {
        var control = AtlasUi.Panel(row, name, Color.clear, true);
        control.GetComponent<UnityEngine.UI.Image>().canvasRenderer.cullTransparentMesh = false;
        AtlasUi.Width(control, width);

        var box = AtlasUi.Panel(control, "Checkbox", AtlasUi.Raised, true);
        var boxImage = box.GetComponent<UnityEngine.UI.Image>();
        VttUiSkin.Surface(boxImage, AtlasUi.Raised, 3);
        AtlasUi.Place(box, 1, 3, 18, 18);
        var mark = AtlasUi.Text(box, "✓", 14, AtlasUi.Ink);
        mark.alignment = TextAnchor.MiddleCenter;
        AtlasUi.Fill(mark.rectTransform);
        var label = AtlasUi.Text(control, caption, 14);
        AtlasUi.Fill(label.rectTransform, left: 26);

        var toggle = control.gameObject.AddComponent<UnityEngine.UI.Toggle>();
        toggle.targetGraphic = boxImage;
        toggle.graphic = mark;
        toggle.onValueChanged.AddListener(value =>
        {
            if (value) _activeFeedFilter = filter;
            else if (_activeFeedFilter == filter) _activeFeedFilter = FeedFilter.All;
            RefreshFeedFilter();
        });
        return toggle;
    }

    private UnityEngine.UI.Text BuildUnreadBadge(UnityEngine.UI.Button button)
    {
        var badge = AtlasUi.Panel(button.transform, "UnreadChat", new Color(.69f, .12f, .15f), true);
        var image = badge.GetComponent<UnityEngine.UI.Image>();
        VttUiSkin.Surface(image, image.color, 12, false);
        image.raycastTarget = false;
        badge.anchorMin = badge.anchorMax = new Vector2(1, 1);
        badge.pivot = Vector2.one;
        badge.anchoredPosition = new Vector2(-2, -1);
        badge.sizeDelta = new Vector2(25, 25);
        var text = AtlasUi.Text(badge, "", 11, Color.white);
        text.alignment = TextAnchor.MiddleCenter;
        AtlasUi.Fill(text.rectTransform);
        return text;
    }

    private void LayoutSessionFeed(float top)
    {
        var panel = (RectTransform)_logPanelGO.transform;
        const float bottom = 16;
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1, 0);
        panel.anchoredPosition = new Vector2(-16, bottom);
        panel.sizeDelta = new Vector2(Mathf.Min(410, Mathf.Max(0, _nativeRoot.rect.width - 32)),
            Mathf.Max(0, _nativeRoot.rect.height - top - bottom));
    }

    private void ConnectSessionFeed()
    {
        var chat = string.IsNullOrEmpty(RelayManager.CurrentJoinCode) ? null : GameNetworkManager.Instance?.Chat;
        if (_chat != chat)
        {
            DisconnectSessionFeed();
            _chat = chat;
            if (_chat != null)
            {
                _chat.Received += ReceiveChatMessage;
                _chat.Rejected += OnChatRejected;
                _chat.Reset += ResetSessionFeed;
            }
        }
        RefreshFeedFilter();
        _chat?.Subscribe();
    }

    private void DisconnectSessionFeed()
    {
        if (_chat != null)
        {
            _chat.Received -= ReceiveChatMessage;
            _chat.Rejected -= OnChatRejected;
            _chat.Reset -= ResetSessionFeed;
            _chat = null;
        }
        ResetSessionFeed();
    }

    private void ResetSessionFeed()
    {
        ++_chatSession;
        CloseFeedImage();
        foreach (var row in _feedRows) DestroyFeedRow(row);
        _feedRows.Clear();
        _unreadChat = 0;
        _activeFeedFilter = FeedFilter.All;
        _chatPending = false;
        ClearChatAttachment();
        if (_chatInput != null)
        {
            _chatInput.text = "";
            SetChatStatus("");
            RefreshFeedFilter();
            RefreshUnreadBadge();
        }
    }

    private void RefreshFeedFilter()
    {
        _feedFilter.SetIsOnWithoutNotify(_activeFeedFilter == FeedFilter.Rolls);
        _messageFilter.SetIsOnWithoutNotify(_activeFeedFilter == FeedFilter.Messages);
        _chatComposer.SetActive(_chat != null && _activeFeedFilter != FeedFilter.Rolls);
        foreach (var row in _feedRows)
            row.Object.SetActive(ShowsFeedEntry(row.IsRoll));
        RefreshFeedEmpty();
        _feedScrollToBottom = true;
        UpdateFeedReadState();
    }

    private void UpdateFeedScroll()
    {
        if (!_feedScrollToBottom || !_logPanelGO.activeInHierarchy) return;
        _feedScrollToBottom = false;
        Canvas.ForceUpdateCanvases();
        _feedScroll.StopMovement();
        _feedScroll.verticalNormalizedPosition = 0f;
    }

    private bool ShowsFeedEntry(bool isRoll) =>
        _activeFeedFilter == FeedFilter.All || _activeFeedFilter == (isRoll ? FeedFilter.Rolls : FeedFilter.Messages);

    private bool IsReadingChat => ShowsFeedEntry(false) && _logPanelGO.activeInHierarchy
        && (_feedImageModal == null || !_feedImageModal.activeInHierarchy)
        && !HostSceneCurtain.IsLoadingScreenVisible && !HostSceneCurtain.IsBlockingLocalPlayer;

    private void UpdateFeedReadState()
    {
        if (_unreadChat == 0 || !IsReadingChat) return;
        _unreadChat = 0;
        RefreshUnreadBadge();
    }

    private void RefreshUnreadBadge()
    {
        SetUnreadBadge(_hostUnread);
        SetUnreadBadge(_playerUnread);
    }

    private void SetUnreadBadge(UnityEngine.UI.Text badge)
    {
        if (badge == null) return;
        badge.text = _unreadChat > 99 ? "99+" : _unreadChat.ToString();
        badge.transform.parent.gameObject.SetActive(_unreadChat > 0);
    }

    private void RefreshFeedEmpty()
    {
        _feedEmpty.text = _activeFeedFilter switch
        {
            FeedFilter.Messages => "Сообщений пока нет.",
            FeedFilter.Rolls => "Бросков пока нет.",
            _ => _chat == null ? "Бросков пока нет." : "Здесь появятся сообщения и броски."
        };
        _feedEmpty.gameObject.SetActive(!_feedRows.Exists(row => ShowsFeedEntry(row.IsRoll)));
    }

    private void AddFeedRow(long timestamp, string label, bool isRoll, string message = null, byte[] image = null)
    {
        if (_feedContent == null) return;
        if (ShowsFeedEntry(isRoll) && _logPanelGO.activeInHierarchy
            && (_feedContent.rect.height <= _feedScroll.viewport.rect.height || _feedScroll.verticalNormalizedPosition <= .02f))
            _feedScrollToBottom = true;

        var card = AtlasUi.Column(_feedContent, isRoll ? "Roll" : "Message", 8, 3);
        VttUiSkin.Surface(card.gameObject.AddComponent<UnityEngine.UI.Image>(),
            isRoll ? AtlasUi.Raised : new Color(1f, .995f, .97f), 6, false);
        var outline = AtlasUi.Node(card, "CardBorder");
        AtlasUi.Fill(outline);
        outline.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        var border = outline.gameObject.AddComponent<AtlasBorder>();
        border.radius = 6;
        border.color = new Color(.72f, .64f, .49f, .85f);
        border.raycastTarget = false;
        var heading = AtlasUi.Paragraph(card, label, 14);
        heading.fontStyle = FontStyle.Bold;
        if (!string.IsNullOrEmpty(message)) AtlasUi.Paragraph(card, message, 15);
        var row = new FeedRow { Timestamp = timestamp, IsRoll = isRoll, Object = card.gameObject };
        if (image != null && image.Length > 0) AddFeedImage(card, row, image);

        int position = _feedRows.FindIndex(existing => existing.Timestamp > timestamp);
        if (position < 0) position = _feedRows.Count;
        _feedRows.Insert(position, row);
        card.SetSiblingIndex(position);
        card.gameObject.SetActive(ShowsFeedEntry(isRoll));

        int remaining = isRoll ? 500 : 100;
        for (int index = _feedRows.Count - 1; index >= 0; index--)
        {
            if (_feedRows[index].IsRoll != isRoll || remaining-- > 0) continue;
            DestroyFeedRow(_feedRows[index]);
            _feedRows.RemoveAt(index);
            break;
        }
        RefreshFeedEmpty();
    }

    private void AddFeedImage(RectTransform card, FeedRow row, byte[] bytes)
    {
        var fullSize = LobbyChat.UnpackImage(bytes, readable: true);
        if (fullSize == null) return;
        row.Image = MapTextureUtility.ResizeTo(fullSize, 160);
        row.Image.Apply(false, true);
        ReleaseFeedTexture(fullSize);
        int width = row.Image.width, height = row.Image.height;
        var slot = AtlasUi.Node(card, "ImageSlot");
        AtlasUi.Height(slot, height);
        var rect = AtlasUi.Node(slot, "Attachment");
        AtlasUi.Place(rect, 0, 0, width, height);
        var image = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
        image.texture = row.Image;
        var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => OpenFeedImage(bytes));
    }

    private void BuildFeedImageViewer()
    {
        var viewer = AtlasUi.Node(_nativeRoot, "ChatImageViewer");
        AtlasUi.Fill(viewer);
        var content = AtlasUi.Node(viewer, "FullImageArea");
        AtlasUi.Fill(content, 20, 58, 20, 20);
        _feedImageViewer = AtlasUi.Node(content, "FullImage").gameObject.AddComponent<UnityEngine.UI.RawImage>();
        AtlasUi.Fill(_feedImageViewer.rectTransform);
        _feedImageViewer.raycastTarget = false;
        _feedImageViewer.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>().aspectMode =
            UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;

        var close = AtlasUi.CloseButton(viewer, CloseFeedImage, dark: true);
        var closeRect = (RectTransform)close.transform;
        closeRect.anchorMin = closeRect.anchorMax = closeRect.pivot = Vector2.one;
        closeRect.anchoredPosition = new Vector2(-18, -14);
        _feedImageModal = UiModalScope.Wrap(viewer.gameObject, close);
        _feedImageModal.GetComponent<UiModalScope>().dismiss = CloseFeedImage;
        _feedImageModal.GetComponent<UnityEngine.UI.Image>().color = new Color(.025f, .04f, .035f, .96f);
    }

    private void OpenFeedImage(byte[] bytes)
    {
        if (GameplayInputGate.IsModalOpen) return;
        _viewedImage = LobbyChat.UnpackImage(bytes);
        if (_viewedImage == null) return;
        _feedImageViewer.texture = _viewedImage;
        _feedImageViewer.GetComponent<UnityEngine.UI.AspectRatioFitter>().aspectRatio =
            (float)_viewedImage.width / _viewedImage.height;
        _feedImageModal.SetActive(true);
    }

    private void CloseFeedImage()
    {
        if (_feedImageModal != null) _feedImageModal.SetActive(false);
        if (_feedImageViewer != null) _feedImageViewer.texture = null;
        ReleaseFeedTexture(_viewedImage);
        _viewedImage = null;
    }

    private static void DestroyFeedRow(FeedRow row)
    {
        if (row.Object != null)
        {
            row.Object.SetActive(false);
            if (Application.isPlaying) Destroy(row.Object);
            else DestroyImmediate(row.Object);
        }
        ReleaseFeedTexture(row.Image);
    }

    private static void ReleaseFeedTexture(Texture2D texture)
    {
        if (texture == null) return;
        if (Application.isPlaying) Destroy(texture);
        else DestroyImmediate(texture);
    }

    private void ReceiveChatMessage(LobbyChat.Entry entry)
    {
        string time = new DateTime(entry.Timestamp, DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss");
        AddFeedRow(entry.Timestamp, $"{time}  {entry.Author}", false, entry.Text, entry.Image);
        if (NetworkManager.Singleton?.LocalClientId == entry.Sender)
        {
            if (_chatPending)
            {
                _chatPending = false;
                _chatInput.text = "";
                ClearChatAttachment();
                SetChatStatus("");
            }
        }
        else if (!IsReadingChat)
        {
            ++_unreadChat;
            RefreshUnreadBadge();
        }
    }

    private void SetChatStatus(string status)
    {
        _chatStatus.text = status;
        _chatStatus.gameObject.SetActive(status.Length > 0);
        _chatPreviewRow.gameObject.SetActive(_chatAttachment != null);
        _chatRemove.interactable = !_chatPending;
        _chatAttach.interactable = !_chatPending;
        _chatSend.interactable = !_chatPending;
        _chatInput.interactable = !_chatPending;
    }

    private void SendChatMessage()
    {
        string message = _chatInput.text.Trim();
        if (_chatPending || (message.Length == 0 && _chatAttachment == null)) return;
        _chatPending = true;
        SetChatStatus("Отправка…");
        if (_chat?.Post(message, _chatAttachment) != true) OnChatRejected();
    }

    private void OnChatRejected()
    {
        _chatPending = false;
        SetChatStatus("Сообщение не отправлено. Попробуйте ещё раз.");
    }

    private void PickChatImage()
    {
        if (_chatPending || GameplayInputGate.IsModalOpen) return;
        int session = _chatSession;
        FileBrowser.ShowLoadDialog(paths =>
        {
            if (this == null || session != _chatSession || paths.Length == 0) return;
            LoadChatImage(paths[0]);
        }, null, FileBrowser.PickMode.Files, false, null, null, "Изображение для чата", "Прикрепить",
            showImagePreview: true);
    }

    private void LoadChatImage(string path)
    {
        Texture2D original = null, preview = null;
        try
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
            {
                SetChatStatus("Поддерживаются PNG и JPEG.");
                return;
            }
            if (new FileInfo(path).Length > 16 * 1024 * 1024)
            {
                SetChatStatus("Выберите файл размером до 16 МБ.");
                return;
            }
            original = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!original.LoadImage(File.ReadAllBytes(path)))
            {
                SetChatStatus("Не удалось открыть изображение.");
                return;
            }
            int dimension = LobbyChat.ImageDimension;
            byte[] attachment;
            do
            {
                ReleaseFeedTexture(preview);
                preview = MapTextureUtility.ResizeTo(original, dimension);
                attachment = LobbyChat.PackImage(preview);
                dimension = Mathf.Max(256, dimension * 2 / 3);
            } while (attachment == null);
            ClearChatAttachment();
            _chatAttachment = attachment;
            _chatPreviewTexture = preview;
            _chatPreview.texture = _chatPreviewTexture;
            _chatPreview.GetComponent<UnityEngine.UI.AspectRatioFitter>().aspectRatio =
                (float)preview.width / preview.height;
            preview = null;
            SetChatStatus("");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[LobbyChat] Image attachment failed: {exception.Message}");
            SetChatStatus("Не удалось открыть изображение.");
        }
        finally
        {
            if (original != null) Destroy(original);
            if (preview != null) Destroy(preview);
        }
    }

    private void ClearChatAttachment()
    {
        _chatAttachment = null;
        if (_chatPreview != null) _chatPreview.texture = null;
        if (_chatPreviewTexture != null) Destroy(_chatPreviewTexture);
        _chatPreviewTexture = null;
    }
}
