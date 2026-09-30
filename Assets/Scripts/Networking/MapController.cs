using System;
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
    private Vector3 _baseScale = new Vector3(2f, 1, 2f); // переопределится при загрузке
    private Vector3 _originalPlaneScale; // чистый размер MapPlane до масштабирования
    private bool _isDragging;
    private Vector3 _dragStartPlanePos;
    private Vector3 _dragStartMouseWorld;
    private Camera _cam;
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
            if (GameplayInputGate.AllowsWorldPointerInput)
                HandleDrag();
            else
                _isDragging = false;

            if (GameplayInputGate.AllowsKeyboardHotkeys)
                HandleRotate();
        }
        // Сетка и стены обновляются у всех клиентов из синхронизированных bounds карты
        // (NetworkTransform — позиция/поворот, NetworkVariable — масштаб, MapSync — текстура).
        RebuildGrid();
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
        if (pngData == null || pngData.Length == 0 || pngData.Length > MapSync.MaxMapBytes)
        {
            Debug.LogError("[Map] Image exceeds the 16 MB network limit");
            DiceUI.Instance?.ShowToolNotice("Карта слишком большая для передачи (максимум 16 МБ).");
            return;
        }
        if (!ApplyImageInternal(pngData)) return;
        MapSync.Instance?.SendMapToAll(pngData);
    }

    /// <summary>Клиент получает картинку от хоста.</summary>
    public bool ApplyImageLocal(byte[] pngData)
    {
        return ApplyImageInternal(pngData);
    }

    private bool ApplyImageInternal(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return false;
        Texture2D tex = new Texture2D(2, 2);
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

        if (_currentTexture != null) Destroy(_currentTexture);
        _currentTexture = tex;
        ApplyTextureToMapMaterial(tex);

        // Скрываем GameBoard (теперь карта на MapPlane)
        HideGameBoard();

        FitPlaneToTexture();
        ApplyScale(_netScale.Value);
        RebuildGrid();

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
        RebuildGrid();
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
        RebuildGrid();
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
            RebuildGrid();
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
        RebuildGrid();
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

        // localScale уже учитывает _baseScale × scale, не умножаем повторно
        float w = 10f * mapPlane.transform.localScale.x;
        float h = 10f * mapPlane.transform.localScale.z;
        return new Bounds(mapPlane.transform.position, new Vector3(w, 0.1f, h));
    }

    private void RebuildGrid()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null)
            gm.SetBounds(GetMapBounds(), mapPlane != null ? mapPlane.transform.rotation : Quaternion.identity);
    }

    public byte[] GetCurrentPngData()
    {
        if (_currentTexture == null) return null;
        return _currentTexture.EncodeToPNG();
    }

    private new void OnDestroy()
    {
        if (_scaleInputField != null)
            _scaleInputField.onEndEdit.RemoveListener(OnScaleInputChanged);
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
    }
}
