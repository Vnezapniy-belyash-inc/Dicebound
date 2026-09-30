using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Сетевой токен. Права: IsSpawner — свои; IsHost — любые (картинка, удаление, копирование).
/// Перемещение проходит через сервер; создатель и хост управляют меню.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TokenController : NetworkBehaviour
{
    [Header("Portrait")]
    public GameObject portraitQuad; // Quad для картинки

    [Header("Appearance")]
    public Color defaultColor = new Color(0.4f, 0.45f, 0.55f);

    private Material _bodyMaterial;
    private Material _portraitMaterial;
    private Texture2D _portraitTexture;
    private bool _isDragging;
    private Vector2Int _currentCell = new(-1, -1);
    private ulong _dragController = ulong.MaxValue;
    private int _dragGesture;
    private float _dragLeaseUntil;

    private NetworkVariable<Vector3> _netColor = new(Vector3.one,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _netSpawnerClientId = new(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ulong SpawnerClientId => _netSpawnerClientId.Value;

    public bool IsSpawner =>
        NetworkManager.Singleton != null
        && _netSpawnerClientId.Value != ulong.MaxValue
        && NetworkManager.Singleton.LocalClientId == _netSpawnerClientId.Value;

    public static TokenController Instance { get; private set; }

    private void Awake()
    {
        _bodyMaterial = GetComponent<MeshRenderer>()?.material;

        if (portraitQuad != null)
        {
            var mr = portraitQuad.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                _portraitMaterial = mr.material;
                _portraitMaterial.color = Color.white;
            }
        }
    }

    public override void OnNetworkSpawn()
    {
        // Применить цвет при спавне (для late-join)
        _netColor.OnValueChanged += (old, val) =>
        {
            if (_portraitMaterial != null && _portraitTexture == null)
                _portraitMaterial.color = new Color(val.x, val.y, val.z, 1f);
        };
        ApplyColor(_netColor.Value);

        if (IsServer)
        {
            if (_netSpawnerClientId.Value == ulong.MaxValue)
                _netSpawnerClientId.Value = OwnerClientId;
            ServerRefreshPlayerColor();
        }

        TokenImageSync.EnsureInstance();
        TokenImageSync.TryApplyPending(this);
        StartCoroutine(RegisterCellAfterSpawn());
    }

    private System.Collections.IEnumerator RegisterCellAfterSpawn()
    {
        // NetworkTransform's initial position is available after the spawn frame.
        yield return null;
        if (!IsSpawned || _currentCell.x >= 0) yield break;
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) yield break;
        Vector2Int cell = gm.GetGridPosition(transform.position);
        if (gm.TryOccupyCell(cell)) _currentCell = cell;
    }

    /// <summary>Rebuild local snap occupancy after the map changes its grid bounds.</summary>
    public static void RebuildCellOccupancy()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;
        gm.ClearOccupiedCells();
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (token == null || !token.IsSpawned) continue;
            Vector2Int cell = gm.GetGridPosition(token.transform.position);
            token._currentCell = gm.TryOccupyCell(cell) ? cell : new Vector2Int(-1, -1);
        }
    }

    /// <summary>Server: update spawner after reconnect ownership transfer.</summary>
    public void ServerUpdateSpawnerClientId(ulong clientId)
    {
        if (!IsServer) return;
        _netSpawnerClientId.Value = clientId;
    }

    /// <summary>Server: sync baked token color from player registry to all clients.</summary>
    public void ServerRefreshPlayerColor()
    {
        if (!IsServer) return;

        Color c = PlayerRegistry.GetServerPlayerColor(
            _netSpawnerClientId.Value != ulong.MaxValue ? _netSpawnerClientId.Value : OwnerClientId);
        var rgb = new Vector3(c.r, c.g, c.b);
        _netColor.Value = rgb;
        ApplyColor(rgb);
        RefreshColorClientRpc(rgb);
    }

    [Rpc(SendTo.Everyone)]
    private void RefreshColorClientRpc(Vector3 rgb)
    {
        ApplyColor(rgb);
    }

    private void ApplyColor(Vector3 rgb)
    {
        if (_portraitMaterial != null && _portraitTexture == null)
            _portraitMaterial.color = new Color(rgb.x, rgb.y, rgb.z, 1f);
    }

    public void BeginDrag(int gesture)
    {
        if (!IsSpawned) return;
        if (IsServer) BeginDragOnServer(NetworkManager.LocalClientId, gesture);
        else BeginDragServerRpc(gesture);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void BeginDragServerRpc(int gesture, RpcParams rpcParams = default) =>
        BeginDragOnServer(rpcParams.Receive.SenderClientId, gesture);

    private void BeginDragOnServer(ulong clientId, int gesture)
    {
        if (_dragController != ulong.MaxValue && _dragController != clientId
            && Time.unscaledTime < _dragLeaseUntil) return;
        _dragController = clientId;
        _dragGesture = gesture;
        _dragLeaseUntil = Time.unscaledTime + 2f;
    }

    public void MoveDrag(int gesture, Vector3 position)
    {
        if (!IsSpawned) return;
        if (IsServer) MoveDragOnServer(NetworkManager.LocalClientId, gesture, position);
        else MoveDragServerRpc(gesture, position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone,
        Delivery = RpcDelivery.Unreliable)]
    private void MoveDragServerRpc(int gesture, Vector3 position, RpcParams rpcParams = default) =>
        MoveDragOnServer(rpcParams.Receive.SenderClientId, gesture, position);

    private void MoveDragOnServer(ulong clientId, int gesture, Vector3 position)
    {
        if (!CanControlDrag(clientId, gesture) || !ValidDragPosition(position)) return;
        _dragLeaseUntil = Time.unscaledTime + 2f;
        transform.position = position;
    }

    public void EndDrag(int gesture, Vector3 position)
    {
        if (!IsSpawned) return;
        if (IsServer) EndDragOnServer(NetworkManager.LocalClientId, gesture, position);
        else EndDragServerRpc(gesture, position);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void EndDragServerRpc(int gesture, Vector3 position, RpcParams rpcParams = default) =>
        EndDragOnServer(rpcParams.Receive.SenderClientId, gesture, position);

    private void EndDragOnServer(ulong clientId, int gesture, Vector3 position)
    {
        if (!CanControlDrag(clientId, gesture) || !ValidDragPosition(position)) return;
        transform.position = position;
        _dragController = ulong.MaxValue;
        SnapAuthoritative();
    }

    private bool CanControlDrag(ulong clientId, int gesture) =>
        IsServer && _dragController == clientId && _dragGesture == gesture
        && Time.unscaledTime <= _dragLeaseUntil;

    private static bool ValidDragPosition(Vector3 position) =>
        !float.IsNaN(position.x) && !float.IsNaN(position.y) && !float.IsNaN(position.z)
        && Mathf.Abs(position.x) < 10000f && Mathf.Abs(position.y) < 10000f
        && Mathf.Abs(position.z) < 10000f;

    /// <summary>Загружает картинку на токен (создатель или хост).</summary>
    public void LoadImage(byte[] jpgData)
    {
        if ((!IsSpawner && !IsHost) || jpgData == null || jpgData.Length == 0) return;
        if (jpgData.Length > TokenImageSync.MaxPortraitBytes)
        {
            DiceUI.Instance?.ShowToolNotice("Изображение токена слишком большое (максимум 2 МБ).");
            return;
        }

        if (!ApplyImageLocal(jpgData)) return;
        if (!IsSpawned) return;

        TokenImageSync.EnsureInstance();
        ulong netId = NetworkObjectId;

        if (IsServer)
        {
            TokenImageSync.BroadcastImage(netId, jpgData);
        }
        else
            TokenImageSync.UploadToServer(netId, jpgData);
    }

    /// <summary>Клиент получает картинку от создателя.</summary>
    public bool ApplyImageLocal(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return false;

        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Destroy(tex);
            Debug.LogError("[Token] Failed to load image");
            return false;
        }

        if (_portraitTexture != null)
            Destroy(_portraitTexture);
        _portraitTexture = tex;

        if (_portraitMaterial != null)
        {
            _portraitMaterial.mainTexture = tex;
            _portraitMaterial.color = Color.white;
            Debug.Log($"[Token] Image applied: {tex.width}x{tex.height}");
        }
        return true;
    }

    // ═══ Снап к сетке ═══

    /// <summary>Снап при создании токена на сервере.</summary>
    public void SnapToGrid()
    {
        if (IsServer) SnapAuthoritative();
    }

    private void SnapAuthoritative()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;

        Vector2Int desired = gm.GetGridPosition(transform.position);
        if (desired.x < 0) return;

        gm.ReleaseCell(_currentCell);
        Vector2Int target = gm.FindNearestFreeCell(desired, maxRadius: 3);
        gm.TryOccupyCell(target);
        _currentCell = target;

        Vector3 snapped = gm.GetCellCenter(target.x, target.y, transform.position.y);
        transform.position = snapped;
        if (IsSpawned) SnapResultClientRpc(snapped);
    }

    [Rpc(SendTo.Everyone)]
    private void SnapResultClientRpc(Vector3 snappedPos)
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null)
        {
            gm.ReleaseCell(_currentCell);
            Vector2Int cell = gm.GetGridPosition(snappedPos);
            if (cell.x >= 0)
            {
                gm.TryOccupyCell(cell);
                _currentCell = cell;
            }
        }
        transform.position = snappedPos;
    }

    // ═══ Удаление ═══

    /// <summary>Запросить удаление токена (владелец или хост).</summary>
    public void RequestDespawn()
    {
        if (!IsSpawned) return;
        if (!IsSpawner && !IsHost) return;
        RequestDespawnServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDespawnServerRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (NetworkPermissions.CanUploadTokenPortrait(sender, this))
            GetComponent<NetworkObject>().Despawn();
    }

    public override void OnNetworkDespawn()
    {
        TokenImageSync.RemovePortrait(NetworkObjectId);

        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null && _currentCell.x >= 0)
            gm.ReleaseCell(_currentCell);
        _currentCell = new(-1, -1);
    }

    private new void OnDestroy()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null && _currentCell.x >= 0)
            gm.ReleaseCell(_currentCell);
    }

    // ═══ Копирование ═══

    public void RequestCopy()
    {
        if (!IsSpawned) return;
        if (!IsSpawner && !IsHost) return;

        if (IsServer)
        {
            if (NetworkPermissions.CanCopyToken(NetworkManager.Singleton.LocalClientId, this))
                TokenManager.Instance?.CopyToken(this, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            RequestCopyServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCopyServerRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!NetworkPermissions.CanCopyToken(sender, this)) return;
        TokenManager.Instance?.CopyToken(this, sender);
    }

    // ═══ Контекстное меню (OnGUI) ═══

    private static TokenController _activeMenuToken;
    private bool _showMenu;
    private Rect _menuRect;

    public byte[] GetPortraitJpg()
    {
        if (_portraitTexture == null) return null;
        return _portraitTexture.EncodeToJPG(50);
    }

    private void OnMouseOver()
    {
        if (!IsSpawner && !IsHost) return;
        if (MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive) return;
        if (EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive) return;

        if (!GameplayInputGate.AllowsWorldPointerInput) return;

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame &&
            GameplayInputGate.LastToolExitFrame != Time.frameCount)
        {
            // Закрыть предыдущее меню
            if (_activeMenuToken != null && _activeMenuToken != this)
                _activeMenuToken._showMenu = false;
            _activeMenuToken = this;

            _showMenu = true;
            Vector2 mousePos = mouse.position.ReadValue();
            _menuRect = new Rect(
                Mathf.Clamp(mousePos.x, 4, Screen.width - 204),
                Mathf.Clamp(Screen.height - mousePos.y, 4, Screen.height - 160),
                200, 156);
        }
    }

    private void LoadImageDialog()
    {
        PickTokenImageFile(path =>
        {
            if (string.IsNullOrEmpty(path)) return;

            byte[] jpg = PreparePortraitJpg(File.ReadAllBytes(path));
            if (jpg != null && jpg.Length > 0)
                LoadImage(jpg);
        });
    }

    private static byte[] PreparePortraitJpg(byte[] fileData, int maxEdge = 512, int quality = 75)
    {
        if (fileData == null || fileData.Length == 0) return null;

        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(fileData))
        {
            Destroy(tex);
            return null;
        }

        int w = tex.width;
        int h = tex.height;
        if (Mathf.Max(w, h) > maxEdge)
        {
            float scale = maxEdge / (float)Mathf.Max(w, h);
            w = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            h = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(tex, rt);
            var resized = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            resized.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
            tex = resized;
        }

        byte[] jpg = tex.EncodeToJPG(quality);
        Destroy(tex);
        return jpg;
    }

    private static void PickTokenImageFile(Action<string> onPicked)
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

    private void OnGUI()
    {
        if (!GameplayInputGate.AllowsImGuiOverlays) return;
        if (MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive ||
            EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive)
        {
            _showMenu = false;
            return;
        }
        if (!_showMenu) return;

        bool canLoad = IsSpawner || IsHost;
        bool canCopy = IsSpawner || IsHost;
        bool canDelete = IsSpawner || IsHost;
        if (!canLoad && !canCopy && !canDelete)
        {
            _showMenu = false;
            return;
        }

        GUI.Box(_menuRect, "", VttUiSkin.ImGuiPanel);
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        titleStyle.normal.textColor = VttUiSkin.Muted;
        GUI.Label(new Rect(_menuRect.x + 12, _menuRect.y + 8, 175, 25),
            "ДЕЙСТВИЯ С ТОКЕНОМ", titleStyle);

        float y = _menuRect.y + 38f;
        if (canLoad)
        {
            if (GUI.Button(new Rect(_menuRect.x + 10, y, 180, 30),
                "Загрузить изображение", VttUiSkin.ImGuiButton))
            {
                LoadImageDialog();
                _showMenu = false;
                return;
            }

            y += 36f;
        }

        if (canCopy && GUI.Button(new Rect(_menuRect.x + 10, y, 180, 30),
            "Копировать", VttUiSkin.ImGuiButton))
        {
            RequestCopy();
            _showMenu = false;
            return;
        }

        if (canCopy)
            y += 36f;

        if (canDelete && GUI.Button(new Rect(_menuRect.x + 10, y, 180, 30),
            "Удалить токен", VttUiSkin.ImGuiDangerButton))
        {
            _showMenu = false;
            DiceUI.Instance?.ConfirmAction("Удалить токен?",
                "Токен исчезнет у всех участников сессии. Отменить удаление нельзя.",
                RequestDespawn);
        }

        if (Event.current.type == EventType.MouseDown && !_menuRect.Contains(Event.current.mousePosition))
            _showMenu = false;
    }
}
