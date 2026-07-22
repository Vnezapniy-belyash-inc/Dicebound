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
/// IsOwner — перемещение (любой игрок через RequestOwnership).
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

    /// <summary>Клиент запрашивает владение чтобы двигать токен.</summary>
    public void RequestOwnership()
    {
        if (IsOwner) return;
        RequestOwnershipServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestOwnershipServerRpc(RpcParams rpcParams = default)
    {
        GetComponent<NetworkObject>().ChangeOwnership(rpcParams.Receive.SenderClientId);
    }

    /// <summary>Загружает картинку на токен (создатель или хост).</summary>
    public void LoadImage(byte[] jpgData)
    {
        if ((!IsSpawner && !IsHost) || jpgData == null || jpgData.Length == 0) return;

        ApplyImageLocal(jpgData);
        if (!IsSpawned) return;

        TokenImageSync.EnsureInstance();
        ulong netId = NetworkObjectId;

        if (IsServer)
        {
            TokenImageSync.CachePortrait(netId, jpgData);
            TokenImageSync.BroadcastImage(netId, jpgData);
        }
        else
            TokenImageSync.UploadToServer(netId, jpgData);
    }

    /// <summary>Клиент получает картинку от создателя.</summary>
    public void ApplyImageLocal(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return;

        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Destroy(tex);
            Debug.LogError("[Token] Failed to load image");
            return;
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
    }

    // ═══ Снап к сетке ═══

    /// <summary>Снап с проверкой занятости (локально сразу + серверная валидация).</summary>
    public void SnapToGrid()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;

        Vector2Int desired = gm.GetGridPosition(transform.position);
        if (desired.x < 0) return;

        // Полная проверка занятости локально — мгновенный отклик без телепортаций
        gm.ReleaseCell(_currentCell);
        Vector2Int target = gm.FindNearestFreeCell(desired, maxRadius: 3);
        gm.TryOccupyCell(target);
        _currentCell = target;

        Vector3 snapped = gm.GetCellCenter(target.x, target.y, transform.position.y);
        transform.position = snapped;

        // Сервер перепроверяет (на случай рассинхрона HashSet)
        if (IsServer)
            SnapAuthoritative();
        else if (IsSpawned)
            RequestSnapServerRpc(transform.position);
    }

    [Rpc(SendTo.Server)]
    private void RequestSnapServerRpc(Vector3 worldPos)
    {
        transform.position = worldPos;
        SnapAuthoritative();
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
        if (sender == _netSpawnerClientId.Value || IsHost)
            GetComponent<NetworkObject>().Despawn();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
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

        if (!GameplayInputGate.AllowsWorldPointerInput) return;

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            // Закрыть предыдущее меню
            if (_activeMenuToken != null && _activeMenuToken != this)
                _activeMenuToken._showMenu = false;
            _activeMenuToken = this;

            _showMenu = true;
            Vector2 mousePos = mouse.position.ReadValue();
            _menuRect = new Rect(mousePos.x, Screen.height - mousePos.y - 108, 160, 108);
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
        if (!_showMenu) return;

        bool canLoad = IsSpawner || IsHost;
        bool canCopy = IsSpawner || IsHost;
        bool canDelete = IsSpawner || IsHost;
        if (!canLoad && !canCopy && !canDelete)
        {
            _showMenu = false;
            return;
        }

        GUI.Box(_menuRect, "");

        float y = _menuRect.y + 4f;
        if (canLoad)
        {
            if (GUI.Button(new Rect(_menuRect.x + 4, y, 152, 28), "Загрузить изображение"))
            {
                LoadImageDialog();
                _showMenu = false;
                return;
            }

            y += 34f;
        }

        if (canCopy && GUI.Button(new Rect(_menuRect.x + 4, y, 152, 28), "Копировать"))
        {
            RequestCopy();
            _showMenu = false;
            return;
        }

        if (canCopy)
            y += 34f;

        if (canDelete && GUI.Button(new Rect(_menuRect.x + 4, y, 152, 28), "Удалить токен"))
        {
            RequestDespawn();
            _showMenu = false;
        }

        if (Event.current.type == EventType.MouseDown && !_menuRect.Contains(Event.current.mousePosition))
            _showMenu = false;
    }
}
