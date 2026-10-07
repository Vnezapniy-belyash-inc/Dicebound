using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Контроллер карты: загрузка PNG, масштаб, drag (средняя кнопка), rotate (R).
/// Права: только хост (IsHost) — загрузка, масштаб, перемещение, поворот.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class MapController : NetworkBehaviour
{
    [Header("Map Plane (отдельный Plane для картинки)")]
    public GameObject mapPlane;

    [Header("Defaults")]
    public float minScale = 0.1f;
    public float maxScale = 5f;
    public float defaultScale = 1f;

    private readonly NetworkVariable<float> _netScale = new(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Material _mapMaterial;
    private Texture2D _currentTexture;
    private byte[] _currentImageBytes;
    private Shader _fogShader;
    private Vector3 _baseScale = new Vector3(2f, 1, 2f); // переопределится при загрузке
    private Vector3 _originalPlaneScale; // чистый размер MapPlane до масштабирования
    private bool _isDragging;
    private Vector3 _dragStartPlanePos;
    private Vector3 _dragStartMouseWorld;
    private Camera _cam;
    private GridManager _gridManager;
    private InputField _scaleInputField;
    private bool _scaleInputReady;

    public static MapController Instance { get; private set; }
    public float CurrentScale => _netScale.Value;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _cam = Camera.main;

        if (mapPlane != null)
        {
            _originalPlaneScale = mapPlane.transform.localScale;

            var renderer = mapPlane.GetComponent<MeshRenderer>();
            EnsureUnlitMapMaterial(renderer);
            if (_mapMaterial != null)
                _mapMaterial.color = new Color(1, 1, 1, 0);
        }

        FindScaleInput();
        InvokeRepeating(nameof(FindScaleInput), 0.5f, 0.5f);

        // Авто-спавн при старте сервера
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }

    private void OnServerStarted()
    {
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned)
        {
            netObj.Spawn();
            Debug.Log("[Map] MapManager spawned on server");
        }
    }

    private void FindScaleInput()
    {
        if (_scaleInputReady) return;
        var diceUI = FindAnyObjectByType<DiceUI>();
        if (diceUI != null)
        {
            _scaleInputField = diceUI.ScaleInput;
            if (_scaleInputField != null)
            {
                _scaleInputField.onEndEdit.AddListener(OnScaleInputChanged);
                _scaleInputField.text = _netScale.Value.ToString("F2");
                _scaleInputReady = true;
                CancelInvoke(nameof(FindScaleInput));
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        if (_netScale.Value != defaultScale)
            ApplyScale(_netScale.Value);

        _netScale.OnValueChanged += (oldVal, newVal) =>
        {
            ApplyScale(newVal);
            if (_scaleInputField != null)
                _scaleInputField.text = newVal.ToString("F2");
        };
    }

    private void Update()
    {
        if (IsHost)
        {
            if (GameplayInputGate.AllowsWorldPointerInput && !SceneEditor.IsEditing && !FogManager.IsManualEditing)
                HandleDrag();
            else
                _isDragging = false;

            if (GameplayInputGate.AllowsKeyboardHotkeys && !SceneEditor.IsEditing && !FogManager.IsManualEditing)
                HandleRotate();
        }
    }

    private void LateUpdate()
    {
        if (_gridManager == null) _gridManager = FindAnyObjectByType<GridManager>();
        if (_gridManager != null && mapPlane != null)
            _gridManager.SetVisualBounds(GetMapBounds());
    }

    // ═══ Загрузка изображения ═══

    public void LoadImage()
    {
        if (!IsHost) return;
        PickImageFile(path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            byte[] data = File.ReadAllBytes(path);
            ApplyImage(data);
        });
    }

    public void LoadImageToLibrary(string name)
    {
        if (!IsHost) return;
        try { name = SceneFileStore.ValidateNewMapAssetName(name); }
        catch (Exception ex) { DiceUI.Instance?.ShowToolNotice("Каталог карт: " + ex.Message); return; }
        PickImageFile(path =>
        {
            if (string.IsNullOrEmpty(path)) return;
            byte[] data = File.ReadAllBytes(path);
            if (data.Length == 0 || data.Length > 96 * 1024 * 1024)
            {
                DiceUI.Instance?.ShowToolNotice("Исходная карта превышает 96 МБ.");
                return;
            }
            if (!ApplyImageInternal(data)) return;
            MapSync.Instance?.SendMapToAll(_currentImageBytes);
            SceneFileStore.ResetCurrentMapAssetLink();
            try
            {
                SceneFileStore.AddCurrentMapAsset(name);
                DmPanelUI.Instance?.RefreshMapCatalog();
                DiceUI.Instance?.ShowToolNotice("Карта добавлена в каталог: " + name);
            }
            catch (Exception ex) { DiceUI.Instance?.ShowToolNotice("Каталог карт: " + ex.Message); }
        });
    }

    public void ImportImageFilesToLibrary()
    {
        if (!IsHost) return;
        PickImageFiles(paths =>
        {
            if (paths == null || paths.Length == 0) return;
            if (paths.Length > 32)
            {
                DiceUI.Instance?.ShowToolNotice("За один импорт можно выбрать не более 32 изображений.");
                return;
            }
            var names = new List<string>();
            var prepared = new List<byte[]>();
            int skipped = 0;
            long totalPreparedBytes = 0;
            foreach (string path in paths)
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || info.Length <= 0 || info.Length > 96L * 1024 * 1024)
                        throw new FormatException("Исходный файл пуст или превышает 96 МБ.");
                    byte[] bytes = PrepareImageForStorage(File.ReadAllBytes(path));
                    if (bytes == null) throw new FormatException("Файл не удалось обработать как изображение карты.");
                    totalPreparedBytes += bytes.Length;
                    if (totalPreparedBytes > 96L * 1024 * 1024)
                    {
                        DiceUI.Instance?.ShowToolNotice("Пакет превышает 96 МБ. Импортируйте карты несколькими наборами.");
                        return;
                    }
                    names.Add(Path.GetFileName(path));
                    prepared.Add(bytes);
                }
                catch (Exception ex)
                {
                    skipped++;
                    Debug.LogWarning("[Map] Skipped catalog image " + path + ": " + ex.Message);
                }
            }
            if (prepared.Count == 0)
            {
                DiceUI.Instance?.ShowToolNotice("Не удалось импортировать ни одного изображения.");
                return;
            }
            try
            {
                int count = SceneFileStore.ImportMapAssets(names.ToArray(), prepared.ToArray());
                DmPanelUI.Instance?.RefreshMapCatalog(true);
                string message = "В каталог добавлено карт: " + count;
                if (skipped > 0) message += "; пропущено файлов: " + skipped;
                DiceUI.Instance?.ShowToolNotice(message + ". Сохраните сессию, чтобы записать изменения.");
            }
            catch (Exception ex) { DiceUI.Instance?.ShowToolNotice("Каталог карт: " + ex.Message); }
        });
    }

    private static byte[] PrepareImageForStorage(byte[] source)
    {
        if (source == null || source.Length == 0 || source.Length > 96 * 1024 * 1024) return null;
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(source)) return null;
            if (texture.width > 16384 || texture.height > 16384) return null;
            if (Mathf.Max(texture.width, texture.height) > MapTextureUtility.MaxDimension)
            {
                var resized = MapTextureUtility.Resize(texture);
                Destroy(texture);
                texture = resized;
            }
            byte[] stored = texture.EncodeToPNG();
            while (stored.Length > MapSync.MaxMapBytes)
            {
                byte[] jpeg = MapTextureUtility.TryJpegWithinBudget(texture, MapSync.MaxMapBytes);
                if (jpeg != null) return jpeg;
                int next = Mathf.Max(texture.width, texture.height) / 2;
                if (next < 64) return null;
                var resized = MapTextureUtility.ResizeTo(texture, next);
                Destroy(texture);
                texture = resized;
                stored = texture.EncodeToPNG();
            }
            return stored;
        }
        finally { if (texture != null) Destroy(texture); }
    }

    private static void PickImageFiles(Action<string[]> onPicked)
    {
#if UNITY_EDITOR
        string folder = EditorUtility.OpenFolderPanel("Выберите папку с картами", "", "");
        if (string.IsNullOrEmpty(folder)) { onPicked(null); return; }
        string[] files = Directory.GetFiles(folder);
        var images = new List<string>();
        foreach (string path in files)
        {
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg"
                || extension == ".bmp" || extension == ".tga") images.Add(path);
        }
        images.Sort(StringComparer.OrdinalIgnoreCase);
        onPicked(images.ToArray());
#else
        SimpleFileBrowser.FileBrowser.ShowLoadDialog(
            paths => onPicked(paths), () => onPicked(null),
            SimpleFileBrowser.FileBrowser.PickMode.Files, true, null, null,
            "Выберите несколько карт", "Импортировать");
#endif
    }

    private static void PickImageFile(Action<string> onPicked)
    {
#if UNITY_EDITOR
        string p = EditorUtility.OpenFilePanel("Выберите изображение", "", "png,jpg,jpeg,bmp,tga");
        onPicked(string.IsNullOrEmpty(p) ? null : p);
#else
        SimpleFileBrowser.FileBrowser.ShowLoadDialog(
            (paths) => onPicked(paths.Length > 0 ? paths[0] : null),
            () => onPicked(null),
            SimpleFileBrowser.FileBrowser.PickMode.Files,
            false, null, null, "Выберите изображение", "Select");
#endif
    }

    /// <summary>Хост загружает картинку и рассылает клиентам.</summary>
    public void ApplyImage(byte[] pngData)
    {
        if (!IsHost) return;
        if (pngData == null || pngData.Length == 0 || pngData.Length > 96 * 1024 * 1024)
        {
            Debug.LogError("[Map] Source image exceeds 96 MB");
            DiceUI.Instance?.ShowToolNotice("Исходная карта превышает 96 МБ.");
            return;
        }
        if (!ApplyImageInternal(pngData)) return;
        MapSync.Instance?.SendMapToAll(_currentImageBytes);
        SceneFileStore.ResetCurrentMapAssetLink();
    }

    /// <summary>Клиент получает картинку от хоста.</summary>
    public bool ApplyImageLocal(byte[] pngData)
    {
        return ApplyImageInternal(pngData);
    }

    private bool ApplyImageInternal(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return false;
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(pngData))
        {
            Debug.LogError("[Map] Failed to load image");
            Destroy(tex);
            return false;
        }

        if (_mapMaterial == null)
        {
            Debug.LogError("[Map] _mapMaterial is null! GameBoard needs MeshRenderer.");
            Destroy(tex);
            return false;
        }

        bool resized = Mathf.Max(tex.width, tex.height) > MapTextureUtility.MaxDimension;
        if (resized)
        {
            var smaller = MapTextureUtility.Resize(tex);
            Destroy(tex); tex = smaller;
        }
        byte[] stored = resized ? tex.EncodeToPNG() : pngData;
        while (stored.Length > MapSync.MaxMapBytes)
        {
            byte[] jpeg = MapTextureUtility.TryJpegWithinBudget(tex, MapSync.MaxMapBytes);
            if (jpeg != null)
            {
                if (!tex.LoadImage(jpeg)) { Destroy(tex); return false; }
                stored = jpeg; break;
            }
            int next = Mathf.Max(tex.width, tex.height) / 2;
            if (next < 64) { Destroy(tex); DiceUI.Instance?.ShowToolNotice("Не удалось подготовить карту для передачи."); return false; }
            var smaller = MapTextureUtility.ResizeTo(tex, next); Destroy(tex); tex = smaller;
            stored = tex.EncodeToPNG();
        }

        if (_currentTexture != null) Destroy(_currentTexture);
        _currentTexture = tex;
        _currentImageBytes = stored;
        ApplyTextureToMapMaterial(tex);

        // Скрываем GameBoard (теперь карта на MapPlane)
        HideGameBoard();

        FitPlaneToTexture();
        ApplyScale(_netScale.Value);

        Debug.Log($"[Map] Loaded: {tex.width}x{tex.height}");
        return true;
    }

    // ═══ Масштаб ═══

    private void OnScaleInputChanged(string text)
    {
        if (!IsHost) return;
        if (float.TryParse(text, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float scale))
            SetScale(scale);
        if (_scaleInputField != null)
            _scaleInputField.text = _netScale.Value.ToString("F2");
    }

    public void SetScale(float scale)
    {
        if (!IsHost) return;
        scale = Mathf.Clamp(scale, minScale, maxScale);
        if (IsServer) _netScale.Value = scale;
        else RequestScaleServerRpc(scale);
    }

    public void Rotate90()
    {
        if (!IsHost) return;
        transform.Rotate(Vector3.up, 90f);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestScaleServerRpc(float scale, RpcParams rpcParams = default)
    {
        if (!NetworkPermissions.IsHostClient(rpcParams.Receive.SenderClientId))
            return;

        _netScale.Value = Mathf.Clamp(scale, minScale, maxScale);
    }

    private void ApplyScale(float scale)
    {
        if (mapPlane == null) return;
        // Сохраняем пропорции: умножаем baseScale на scale
        mapPlane.transform.localScale = new Vector3(
            _baseScale.x * scale, _baseScale.y, _baseScale.z * scale);
    }

    // ═══ Drag (средняя кнопка) ═══

    private void HandleDrag()
    {
        if (mapPlane == null || _cam == null) return;
        Mouse m = Mouse.current;
        if (m == null) return;

        if (m.middleButton.wasPressedThisFrame)
        {
            _isDragging = true;
            _dragStartPlanePos = transform.position;
            _dragStartMouseWorld = GetMouseWorldPos(m);
        }

        if (m.middleButton.wasReleasedThisFrame)
            _isDragging = false;

        if (_isDragging && m.middleButton.isPressed)
        {
            Vector3 currentMouse = GetMouseWorldPos(m);
            Vector3 delta = currentMouse - _dragStartMouseWorld;
            transform.position = _dragStartPlanePos + new Vector3(delta.x, 0, delta.z);
        }
    }

    // ═══ Поворот (R) ═══

    private void HandleRotate()
    {
        Keyboard k = Keyboard.current;
        if (k == null) return;

        if (k.rKey.wasPressedThisFrame && !k.shiftKey.isPressed && !k.ctrlKey.isPressed)
        {
            Rotate90();
        }
    }

    // ═══ Сброс ═══

    public void ResetMap()
    {
        if (!IsHost || mapPlane == null) return;
        ResetMapPosition();
        _netScale.Value = defaultScale;
        ApplyScale(defaultScale);
    }

    public void ResetMapPosition()
    {
        if (!IsHost || mapPlane == null) return;
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
    }

    // ═══ Утилиты ═══

    private void EnsureUnlitMapMaterial(MeshRenderer renderer)
    {
        if (renderer == null) return;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Texture")
            ?? Shader.Find("Unlit/Color");
        if (shader == null)
        {
            _mapMaterial = renderer.material;
            return;
        }

        _mapMaterial = new Material(shader) { name = "MapPlaneUnlit" };
        renderer.material = _mapMaterial;
        renderer.receiveShadows = false;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    private void ApplyTextureToMapMaterial(Texture2D tex)
    {
        _mapMaterial.mainTexture = tex;
        _mapMaterial.color = Color.white;

        if (_mapMaterial.HasProperty("_BaseMap"))
            _mapMaterial.SetTexture("_BaseMap", tex);
        if (_mapMaterial.HasProperty("_BaseColor"))
            _mapMaterial.SetColor("_BaseColor", Color.white);
        if (_mapMaterial.HasProperty("_Smoothness"))
            _mapMaterial.SetFloat("_Smoothness", 0f);
        if (_mapMaterial.HasProperty("_Metallic"))
            _mapMaterial.SetFloat("_Metallic", 0f);
    }

    private void HideGameBoard()
    {
        var gb = GameObject.Find("GameBoard");
        if (gb != null)
        {
            var mr = gb.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.enabled = false;
        }
    }

    private Vector3 GetMouseWorldPos(Mouse m)
    {
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = _cam.ScreenPointToRay(m.position.ReadValue());
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return Vector3.zero;
    }

    private void FitPlaneToTexture()
    {
        if (_currentTexture == null || mapPlane == null) return;
        float aspect = (float)_currentTexture.width / _currentTexture.height;
        // Используем чистый исходный размер, а не текущий localScale (который меняется при масштабировании)
        float baseSize = Mathf.Max(_originalPlaneScale.x, _originalPlaneScale.z);
        if (aspect >= 1)
            _baseScale = new Vector3(baseSize, 1, baseSize / aspect);
        else
            _baseScale = new Vector3(baseSize * aspect, 1, baseSize);
    }

    public Bounds GetMapBounds()
    {
        if (mapPlane == null)
            return new Bounds(Vector3.zero, Vector3.one * 10);

        var renderer = mapPlane.GetComponent<Renderer>();
        if (renderer != null) return renderer.bounds;

        // localScale уже учитывает _baseScale × scale, не умножаем повторно
        float w = 10f * mapPlane.transform.localScale.x;
        float h = 10f * mapPlane.transform.localScale.z;
        return new Bounds(mapPlane.transform.position, new Vector3(w, 0.1f, h));
    }

    public byte[] GetCurrentPngData()
    {
        return _currentImageBytes;
    }

    public bool HasImage => _currentTexture != null;
    public void ApplyFog(Texture2D mask, bool enabled, GridManager grid)
    {
        if (_mapMaterial == null) return;
        var shader = _fogShader != null ? _fogShader : _fogShader = Resources.Load<Shader>("DiceboundFog");
        if (shader == null) return;
        if (_mapMaterial.shader != shader)
        {
            _mapMaterial.shader = shader;
            _mapMaterial.SetTexture("_BaseMap", _currentTexture);
            _mapMaterial.SetColor("_BaseColor", _currentTexture != null ? Color.white : Color.clear);
        }
        _mapMaterial.SetTexture("_FogMask", mask);
        _mapMaterial.SetFloat("_FogEnabled", enabled ? 1 : 0);
        _mapMaterial.SetVector("_GridSize", new Vector4(grid.Width * grid.CellSize, grid.Height * grid.CellSize, 0, 0));
        _mapMaterial.SetMatrix("_GridWorldToLocal", Matrix4x4.TRS(grid.GridOrigin, grid.GridRotation, Vector3.one).inverse);
    }

    public void RestoreSceneMap(byte[] data, Vector3 position, Vector3 rotation, float scale)
    {
        if (!IsHost) return;
        transform.SetPositionAndRotation(position, Quaternion.Euler(rotation));
        _netScale.Value = scale;
        ApplyScale(scale);
        if (data != null && data.Length > 0) ApplyImage(data);
        else
        {
            if (_currentTexture != null) Destroy(_currentTexture);
            _currentTexture = null;
            _currentImageBytes = null;
            if (_mapMaterial != null) _mapMaterial.color = new Color(1, 1, 1, 0);
        }
    }

    private new void OnDestroy()
    {
        if (_scaleInputField != null)
            _scaleInputField.onEndEdit.RemoveListener(OnScaleInputChanged);
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }
}
