using Unity.Netcode;
using UnityEngine;

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

    public static TokenController Instance { get; private set; }

    private void Awake()
    {
        _bodyMaterial = GetComponent<MeshRenderer>()?.material;
        // Цвет задаётся в Editor через Material

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
        if (IsServer)
        {
            Color c = PlayerColors.GetColor(OwnerClientId);
            // Локально на хосте
            if (_portraitMaterial != null && _portraitTexture == null)
                _portraitMaterial.color = c;
            // Всем клиентам (и хосту тоже)
            SetColorClientRpc(new Vector3(c.r, c.g, c.b));
        }
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

    /// <summary>Загружает картинку на токен (только владелец).</summary>
    public void LoadImage(byte[] jpgData)
    {
        if (!IsOwner) return;
        ApplyImageLocal(jpgData);
        // Отправляем всем через RPC (как MapSync)
        BroadcastImageClientRpc(jpgData);
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

    /// <summary>Привязывает токен к ближайшей клетке сетки.</summary>
    public void SnapToGrid()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;
        Vector3 snapped = gm.SnapToGrid(transform.position);
        snapped.y = transform.position.y;
        transform.position = snapped;
    }

    public byte[] GetPortraitPng()
    {
        if (_portraitTexture == null) return null;
        return _portraitTexture.EncodeToPNG();
    }
}
