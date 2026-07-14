using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Маркер на клетку: цветной полупрозрачный Quad.
/// Спавнится через MeasurementTool.ApplyArea().
/// </summary>
public class CellMarker : NetworkBehaviour
{
    private const uint PrefabHash = 3847291051u;
    private const uint LegacyPrefabHash = 0;

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
    private static GameObject _template;
    private static bool _handlerRegistered;

    internal static GameObject GetTemplate()
    {
        if (_template == null || !_template)
            _template = CreateTemplate();
        return _template;
    }

    public static void EnsureRegistered()
    {
        if (NetworkManager.Singleton == null) return;

        GetTemplate();

        if (_handlerRegistered) return;

        var handler = new CellMarkerSpawnHandler();
        NetworkManager.Singleton.PrefabHandler.AddHandler(PrefabHash, handler);
        NetworkManager.Singleton.PrefabHandler.AddHandler(LegacyPrefabHash, new CellMarkerSpawnHandler());
        _handlerRegistered = true;
        Debug.Log($"[CellMarker] Prefab handler registered (hash={PrefabHash}, legacy={LegacyPrefabHash})");
    }

    public static void ResetRegistration()
    {
        if (NetworkManager.Singleton != null && _handlerRegistered)
        {
            NetworkManager.Singleton.PrefabHandler.RemoveHandler(PrefabHash);
            NetworkManager.Singleton.PrefabHandler.RemoveHandler(LegacyPrefabHash);
        }

        _handlerRegistered = false;
    }

    public static void Spawn(Vector3 position, int textureIndex)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        EnsureRegistered();

        var go = Object.Instantiate(GetTemplate(), position, Quaternion.identity);
        go.SetActive(true);
        var netObj = go.GetComponent<NetworkObject>();
        netObj.Spawn();

        var marker = go.GetComponent<CellMarker>();
        marker._netTexIndex.Value = textureIndex;
    }

    private static GameObject CreateTemplate()
    {
        var go = new GameObject("CellMarkerTemplate");
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);

        var netObj = go.AddComponent<NetworkObject>();
        NetworkPrefabHash.Set(netObj, PrefabHash);

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(go.transform);
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localRotation = Quaternion.Euler(90, 0, 0);
        quad.transform.localScale = new Vector3(0.95f, 0.95f, 1f);

        var collider = quad.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);

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

public class CellMarkerSpawnHandler : INetworkPrefabInstanceHandler
{
    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        var template = CellMarker.GetTemplate();
        if (template == null || !template)
            return null;

        var go = Object.Instantiate(template, position, rotation);
        go.SetActive(true);
        return go.GetComponent<NetworkObject>();
    }

    public void Destroy(NetworkObject netObj)
    {
        if (netObj != null)
            Object.Destroy(netObj.gameObject);
    }
}
