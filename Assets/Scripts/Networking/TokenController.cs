using System;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Сетевой токен (шайба с портретом).
/// Двигать может любой (через RequestOwnership).
/// Картинку загружает только создатель.
/// Привязка к сетке при отпускании.
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
            Color c = PlayerColors.GetColor(OwnerClientId);
            _netColor.Value = new Vector3(c.r, c.g, c.b);
        }
    }

    private void ApplyColor(Vector3 rgb)
    {
        if (_portraitMaterial != null && _portraitTexture == null)
            _portraitMaterial.color = new Color(rgb.x, rgb.y, rgb.z, 1f);
    }

    [Rpc(SendTo.Everyone)]
    private void SetColorClientRpc(Vector3 rgb)
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

    /// <summary>Загружает картинку на токен (только владелец). Данные должны быть сжаты (&lt;1KB).</summary>
    public void LoadImage(byte[] jpgData)
    {
        if (!IsOwner) return;
        ApplyImageLocal(jpgData);
        BroadcastImageClientRpc(jpgData); // сжатый JPG влезает в RPC
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

    [Rpc(SendTo.Everyone)]
    private void BroadcastImageClientRpc(byte[] pngData)
    {
        ApplyImageLocal(pngData);
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
        if (!IsOwner && !IsHost) return;
        RequestDespawnServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDespawnServerRpc(RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId == OwnerClientId || IsHost)
            GetComponent<NetworkObject>().Despawn();
    }

    public override void OnNetworkDespawn()
    {
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
        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            // Закрыть предыдущее меню
            if (_activeMenuToken != null && _activeMenuToken != this)
                _activeMenuToken._showMenu = false;
            _activeMenuToken = this;

            _showMenu = true;
            Vector2 mousePos = mouse.position.ReadValue();
            _menuRect = new Rect(mousePos.x, Screen.height - mousePos.y - 70, 160, 75);
        }
    }

    private void LoadImageDialog()
    {
        PickTokenImageFile(path =>
        {
            if (!string.IsNullOrEmpty(path))
            {
                byte[] data = File.ReadAllBytes(path);
                Texture2D temp = new Texture2D(2, 2);
                temp.LoadImage(data);
                byte[] jpg = temp.EncodeToJPG(30);
                Destroy(temp);
                LoadImage(jpg);
            }
        });
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
        if (!_showMenu) return;
        GUI.Box(_menuRect, "");

        if (GUI.Button(new Rect(_menuRect.x + 4, _menuRect.y + 4, 152, 28), "Загрузить изображение"))
        {
            LoadImageDialog();
            _showMenu = false;
        }

        if (GUI.Button(new Rect(_menuRect.x + 4, _menuRect.y + 38, 152, 28), "Удалить токен"))
        {
            RequestDespawn();
            _showMenu = false;
        }

        if (Event.current.type == EventType.MouseDown && !_menuRect.Contains(Event.current.mousePosition))
            _showMenu = false;
    }

    // ═══ Late-join ═══

    private static bool _lateJoinHookRegistered;

    /// <summary>Сервер: отправить все изображения токенов при late-join (с задержкой).</summary>
    public static void SendAllImagesToLateJoiner(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        // Задержка чтобы NetworkObjects успели заспавниться на клиенте
        MonoBehaviour runner = FindAnyObjectByType<MonoBehaviour>();
        if (runner != null)
            runner.StartCoroutine(SendImagesDelayed(clientId));
    }

    private static System.Collections.IEnumerator SendImagesDelayed(ulong clientId)
    {
        yield return new WaitForSeconds(2f);
        if (!NetworkManager.Singleton.IsServer) yield break;
        var tokens = FindObjectsByType<TokenController>(FindObjectsInactive.Exclude);
        foreach (var t in tokens)
        {
            byte[] jpg = t.GetPortraitJpg();
            if (jpg != null && jpg.Length > 0)
                t.BroadcastImageClientRpc(jpg);
        }
        Debug.Log($"[Token] Late-join: sent {tokens.Length} token images to client {clientId}");
    }

    /// <summary>Подписаться на late-join (вызывается один раз).</summary>
    public static void EnsureLateJoinHook()
    {
        if (_lateJoinHookRegistered) return;
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.OnClientConnectedCallback += SendAllImagesToLateJoiner;
        _lateJoinHookRegistered = true;
    }
}
