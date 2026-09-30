using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Маркер на клетку (эффект). Права: IsSpawner — свои; IsHost — все (вторая очистка).
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

    public const int EraseToolIndex = -1;

    private const string MSG_PAINT_CELL = "PaintCell";

    private static bool _paintHandlerRegistered;

    private NetworkVariable<int> _netTexIndex = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _netSpawnerClientId = new(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ulong SpawnerClientId => _netSpawnerClientId.Value;

    public bool IsSpawner =>
        NetworkManager.Singleton != null
        && SpawnerClientId != ulong.MaxValue
        && NetworkManager.Singleton.LocalClientId == SpawnerClientId;

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
        EnsurePaintHandlerRegistered();

        if (_handlerRegistered) return;

        var handler = new CellMarkerSpawnHandler();
        NetworkManager.Singleton.PrefabHandler.AddHandler(PrefabHash, handler);
        NetworkManager.Singleton.PrefabHandler.AddHandler(LegacyPrefabHash, new CellMarkerSpawnHandler());
        _handlerRegistered = true;
        Debug.Log($"[CellMarker] Prefab handler registered (hash={PrefabHash}, legacy={LegacyPrefabHash})");
    }

    public static void EnsurePaintHandlerRegistered()
    {
        if (_paintHandlerRegistered || NetworkManager.Singleton == null) return;

        var cmm = NetworkManager.Singleton.CustomMessagingManager;
        if (cmm == null) return;

        cmm.RegisterNamedMessageHandler(MSG_PAINT_CELL, OnPaintCellRequest);
        _paintHandlerRegistered = true;
    }

    private static void OnPaintCellRequest(ulong senderId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int x);
        reader.ReadValueSafe(out int y);
        reader.ReadValueSafe(out int textureIndex);
        ServerApplyCell(new Vector2Int(x, y), textureIndex, senderId);
    }

    public static void RequestApplyCell(Vector2Int cell, int textureIndex)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsConnectedClient) return;

        EnsurePaintHandlerRegistered();

        var writer = new FastBufferWriter(sizeof(int) * 3, Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(cell.x);
        writer.WriteValueSafe(cell.y);
        writer.WriteValueSafe(textureIndex);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            MSG_PAINT_CELL, NetworkManager.ServerClientId, writer);
        writer.Dispose();
    }

    public static void ServerApplyCell(Vector2Int cell, int textureIndex, ulong senderId)
    {
        if (!NetworkManager.Singleton.IsServer || cell.x < 0) return;

        var gm = Object.FindAnyObjectByType<GridManager>();
        if (gm == null) return;
        if (cell.y < 0 || cell.x >= gm.Width || cell.y >= gm.Height) return;
        if (!gm.IsPointOnMap(gm.GetCellCenter(cell.x, cell.y))) return;

        if (textureIndex == EraseToolIndex)
        {
            foreach (var marker in FindAtCell(cell, gm))
            {
                if (NetworkPermissions.CanRemoveSingleCellMarker(senderId, marker))
                    marker.GetComponent<NetworkObject>().Despawn();
            }

            return;
        }

        if (textureIndex < 0 || textureIndex >= TextureColors.Length) return;

        var previous = FindAtCell(cell, gm);
        foreach (var marker in previous)
            if (!NetworkPermissions.CanRemoveSingleCellMarker(senderId, marker))
                return;
        foreach (var marker in previous)
            marker.GetComponent<NetworkObject>().Despawn();

        Vector3 pos = gm.GetCellCenter(cell.x, cell.y, 0.015f);
        Spawn(pos, textureIndex, senderId);
    }

    public static System.Collections.Generic.List<CellMarker> FindAtCell(Vector2Int cell, GridManager gm)
    {
        var result = new System.Collections.Generic.List<CellMarker>();
        if (gm == null || cell.x < 0) return result;

        foreach (var marker in Object.FindObjectsByType<CellMarker>(FindObjectsInactive.Exclude))
        {
            if (marker == null || !marker.IsSpawned) continue;
            if (gm.GetGridPosition(marker.transform.position) == cell)
                result.Add(marker);
        }

        return result;
    }

    public static void ResetRegistration()
    {
        if (NetworkManager.Singleton != null)
        {
            if (_handlerRegistered)
            {
                NetworkManager.Singleton.PrefabHandler.RemoveHandler(PrefabHash);
                NetworkManager.Singleton.PrefabHandler.RemoveHandler(LegacyPrefabHash);
            }

            if (_paintHandlerRegistered && NetworkManager.Singleton.CustomMessagingManager != null)
                NetworkManager.Singleton.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_PAINT_CELL);
        }

        _handlerRegistered = false;
        _paintHandlerRegistered = false;
    }

    public static void Spawn(Vector3 position, int textureIndex, ulong spawnerClientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        EnsureRegistered();

        var go = Object.Instantiate(GetTemplate(), position, Quaternion.identity);
        go.SetActive(true);
        var netObj = go.GetComponent<NetworkObject>();
        netObj.SpawnWithObservers = false;
        netObj.Spawn();

        var marker = go.GetComponent<CellMarker>();
        marker._netSpawnerClientId.Value = spawnerClientId;
        marker._netTexIndex.Value = textureIndex;
        if (LateJoinSync.Instance != null) LateJoinSync.Instance.QueueWorldObject(netObj);
        else
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) netObj.NetworkShow(clientId);
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
        if (!IsSpawned) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (nm.IsServer)
        {
            if (NetworkPermissions.CanRemoveSingleCellMarker(nm.LocalClientId, this))
                GetComponent<NetworkObject>().Despawn();
        }
        else
        {
            RequestRemoveServerRpc();
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestRemoveServerRpc(RpcParams rpcParams = default)
    {
        if (NetworkPermissions.CanRemoveSingleCellMarker(rpcParams.Receive.SenderClientId, this))
            GetComponent<NetworkObject>().Despawn();
    }

    /// <summary>
    /// Remove only the local player's marks. GM-wide deletion is a separate action.
    /// </summary>
    public static void ClearAllMyMarkers()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        ulong localClientId = nm.LocalClientId;

        var markers = FindObjectsByType<CellMarker>(FindObjectsInactive.Exclude);
        foreach (var m in markers)
        {
            if (m == null || !m.IsSpawned || m.SpawnerClientId != localClientId)
                continue;

            if (nm.IsServer)
                m.GetComponent<NetworkObject>().Despawn();
            else
                m.RequestRemove();
        }
    }

    public static void ClearAllMarkersAsHost()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsHost || !nm.IsServer) return;
        foreach (var marker in FindObjectsByType<CellMarker>(FindObjectsInactive.Exclude))
            if (marker != null && marker.IsSpawned)
                marker.GetComponent<NetworkObject>().Despawn();
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
