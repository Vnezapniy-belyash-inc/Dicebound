using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Маркер на клетку: цветной полупрозрачный Quad.
/// Спавнится через MeasurementTool.ApplyArea().
/// </summary>
public class CellMarker : NetworkBehaviour
{
    [Header("Visual")]
    public float yOffset = 0.015f;

    public static readonly Color[] TextureColors = {
        new Color(0.9f, 0.2f, 0.2f, 0.45f), // 0 — красный (огонь/опасность)
        new Color(0.2f, 0.5f, 0.9f, 0.45f), // 1 — синий (вода/магия)
        new Color(0.2f, 0.8f, 0.3f, 0.45f), // 2 — зелёный (природа/яд)
        new Color(0.8f, 0.7f, 0.2f, 0.45f), // 3 — жёлтый (сложная местность)
        new Color(0.5f, 0.3f, 0.7f, 0.45f), // 4 — фиолетовый (тьма)
        new Color(0.3f, 0.3f, 0.3f, 0.55f), // 5 — чёрный (стена/непроходимо)
    };

    public static readonly string[] TextureNames = {
        "Огонь", "Вода", "Природа", "Препят.", "Тьма", "Стена"
    };

    private NetworkVariable<int> _netTexIndex = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private MeshRenderer _renderer;
    private static GameObject _prefab;
    private static bool _handlerRegistered;

    public static void EnsureRegistered()
    {
        if (_handlerRegistered) return;
        if (_prefab == null)
            _prefab = CreatePrefab();

        // Регистрируем через PrefabHandler чтобы клиенты могли спавнить
        var netObj = _prefab.GetComponent<NetworkObject>();
        NetworkManager.Singleton.PrefabHandler.AddHandler(netObj, new CellMarkerSpawnHandler(_prefab));
        _handlerRegistered = true;
    }
    public static void Spawn(Vector3 position, int textureIndex)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (_prefab == null)
            _prefab = CreatePrefab();

        var go = Instantiate(_prefab, position, Quaternion.identity);
        var netObj = go.GetComponent<NetworkObject>();
        netObj.Spawn();

        var marker = go.GetComponent<CellMarker>();
        marker._netTexIndex.Value = textureIndex;
    }

    private static GameObject CreatePrefab()
    {
        var go = new GameObject("CellMarker");
        go.AddComponent<NetworkObject>();

        // Quad
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(go.transform);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
        quad.transform.localScale = new Vector3(0.95f, 0.95f, 1f); // чуть меньше клетки

        // Удаляем коллайдер (не нужен)
        var collider = quad.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        go.AddComponent<CellMarker>();
        return go;
    }

    private void Awake()
    {
        _renderer = GetComponentInChildren<MeshRenderer>();
        if (_renderer != null)
        {
            _renderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            _renderer.material.SetFloat("_Surface", 1f);
            _renderer.material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _renderer.material.SetInt("_SrcBlend", 5);
            _renderer.material.SetInt("_DstBlend", 10);
            _renderer.material.SetInt("_ZWrite", 0);
            _renderer.material.renderQueue = 3000;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
        }
    }

    public override void OnNetworkSpawn()
    {
        ApplyTexture(_netTexIndex.Value);
        _netTexIndex.OnValueChanged += (old, val) => ApplyTexture(val);
    }

    private void ApplyTexture(int index)
    {
        if (_renderer == null) return;
        index = Mathf.Clamp(index, 0, TextureColors.Length - 1);
        _renderer.material.color = TextureColors[index];
    }

    /// <summary>Удалить этот маркер (вызывается по ПКМ).</summary>
    public void RequestRemove()
    {
        if (IsServer)
            GetComponent<NetworkObject>().Despawn();
        else
            RequestRemoveServerRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestRemoveServerRpc()
    {
        GetComponent<NetworkObject>().Despawn();
    }

    /// <summary>Удаляет все маркеры, созданные текущим игроком.</summary>
    public static void ClearAllMyMarkers()
    {
        var markers = FindObjectsByType<CellMarker>(FindObjectsInactive.Exclude);
        foreach (var m in markers)
        {
            if (m.IsOwner)
                m.RequestRemove();
        }
    }
}

/// <summary>Обработчик спавна CellMarker на клиенте.</summary>
public class CellMarkerSpawnHandler : INetworkPrefabInstanceHandler
{
    private GameObject _prefab;

    public CellMarkerSpawnHandler(GameObject prefab) { _prefab = prefab; }

    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        var go = Object.Instantiate(_prefab, position, rotation);
        return go.GetComponent<NetworkObject>();
    }

    public void Destroy(NetworkObject netObj)
    {
        Object.Destroy(netObj.gameObject);
    }
}
