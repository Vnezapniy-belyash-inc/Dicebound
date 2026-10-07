using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

/// <summary>Scene-local visual portal marker. Only the host activates a transition.</summary>
public sealed class SceneTransitionMarker : NetworkBehaviour
{
    private const uint PrefabHash = 1759032741u;
    private const string MSG_ACTIVATE = "SceneTransitionActivateV1";
    private static GameObject _template;
    private static NetworkManager _registeredManager;
    private static readonly System.Collections.Generic.HashSet<NetworkManager> RegisteredManagers = new();
    private string _transitionId;
    private GameObject _visual;
    private readonly System.Collections.Generic.Dictionary<ulong, float> _activationCooldown = new();
    public string TransitionId => _transitionId;
    public override void OnNetworkSpawn()
    {
        _transitionId = _netTransitionId.Value.ToString();
        OnEnabledChanged(!_netIsEnabled.Value, _netIsEnabled.Value);
        if (transform.Find("Visual") == null) BuildVisual(transform.position, _transitionId, _netTitle.Value.ToString());
        else
        {
            _visual = transform.Find("Visual").gameObject;
            Refresh(_netTitle.Value.ToString());
        }
        _netTransitionId.OnValueChanged += OnTransitionIdChanged;
        _netTitle.OnValueChanged += OnTitleChanged;
        _netPosition.OnValueChanged += OnPositionChanged;
        _netIsEnabled.OnValueChanged += OnEnabledChanged;
    }

    public override void OnNetworkDespawn()
    {
        _netTransitionId.OnValueChanged -= OnTransitionIdChanged;
        _netTitle.OnValueChanged -= OnTitleChanged;
        _netPosition.OnValueChanged -= OnPositionChanged;
        _netIsEnabled.OnValueChanged -= OnEnabledChanged;
    }

    private void OnTransitionIdChanged(FixedString128Bytes previous, FixedString128Bytes current)
    {
        _transitionId = current.ToString();
    }

    private void OnTitleChanged(FixedString128Bytes previous, FixedString128Bytes current)
    {
        Refresh(current.ToString());
    }
    private readonly NetworkVariable<FixedString128Bytes> _netTransitionId = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<FixedString128Bytes> _netTitle = new(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Vector3> _netPosition = new(Vector3.zero,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _netIsEnabled = new(true,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void OnPositionChanged(Vector3 previous, Vector3 current) => transform.position = current;
    private void OnEnabledChanged(bool previous, bool current)
    {
        var visual = transform.Find("Visual");
        if (visual != null && visual.gameObject.activeSelf != current) visual.gameObject.SetActive(current);
        var label = transform.Find("Label");
        if (label != null && label.gameObject.activeSelf != current) label.gameObject.SetActive(current);
    }

    private static GameObject Template
    {
        get
        {
            if (_template != null) return _template;
            _template = new GameObject("SceneTransitionMarkerTemplate");
            _template.SetActive(false);
            Object.DontDestroyOnLoad(_template);
            var networkObject = _template.AddComponent<NetworkObject>();
            NetworkPrefabHash.Set(networkObject, PrefabHash);
            _template.AddComponent<SceneTransitionMarker>();
            return _template;
        }
    }

    internal static GameObject GetTemplateForSpawn() => Template;

    internal static void RegisterHandler(NetworkManager manager)
    {
        if (manager == null || !manager.IsListening || _registeredManager == manager) return;
        manager.PrefabHandler.AddHandler(PrefabHash, new SceneTransitionMarkerSpawnHandler());
        _registeredManager = manager;
    }

    public static void EnsureRegistered()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return;
        _ = Template;
        RegisterHandler(nm);
        if (nm.CustomMessagingManager != null && RegisteredManagers.Add(nm))
        {
            nm.CustomMessagingManager.RegisterNamedMessageHandler(MSG_ACTIVATE, OnActivateRequest);
        }
    }

    private static void OnActivateRequest(ulong senderId, FastBufferReader reader)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsServer) return;
        reader.ReadValueSafe(out ulong markerNetworkObjectId);
        if (!nm.SpawnManager.SpawnedObjects.TryGetValue(markerNetworkObjectId, out NetworkObject networkObject)) return;
        var marker = networkObject.GetComponent<SceneTransitionMarker>();
        if (marker == null || string.IsNullOrEmpty(marker.TransitionId) || !marker._netIsEnabled.Value) return;
        if (!NetworkPermissions.IsHostClient(senderId))
        {
            var grid = Object.FindAnyObjectByType<GridManager>();
            var transition = Array.Find(SceneFileStore.GetActiveSceneTransitions(),
                item => item != null && item.id == marker.TransitionId);
            if (grid == null || transition == null) return;
            var token = TokenController.FindSceneToken(transition.id);
            if (token == null || !token.IsSpawned
                || Vector3.Distance(token.CommittedPosition, marker.transform.position) > grid.CellSize * 1.5f) return;
        }
        if (marker._activationCooldown.TryGetValue(senderId, out float lastRequest)
            && Time.unscaledTime - lastRequest < 0.5f) return;
        marker._activationCooldown[senderId] = Time.unscaledTime;
        SceneFileStore.UseSceneTransition(marker.TransitionId);
    }

    public void Refresh(string title)
    {
        _netTitle.Value = new FixedString128Bytes(title ?? string.Empty);
        if (_visual == null || !_visual) return;
        var label = _visual.transform.parent.Find("Label");
        var text = label != null ? label.GetComponent<TextMesh>() : null;
        if (text != null) text.text = string.IsNullOrWhiteSpace(title) ? "Переход" : title;
    }

    public void SetMarkerPosition(Vector3 position)
    {
        if (IsSpawned && IsServer) _netPosition.Value = position;
        else transform.position = position;
    }

    public void SetMarkerEnabled(bool enabled)
    {
        if (IsSpawned && IsServer) _netIsEnabled.Value = enabled;
        else gameObject.SetActive(enabled);
    }

    public static void Spawn(Vector3 position, string transitionId, string title)
    {
        EnsureRegistered();
        if (NetworkManager.Singleton?.IsServer != true) return;
        var root = Object.Instantiate(Template, position, Quaternion.identity);
        root.name = "SceneTransitionMarker_" + transitionId;
        root.SetActive(true);
        var marker = root.GetComponent<SceneTransitionMarker>();
        marker._netTransitionId.Value = new FixedString128Bytes(transitionId);
        marker._netTitle.Value = new FixedString128Bytes(title ?? string.Empty);
        marker._netPosition.Value = position;
        marker._netIsEnabled.Value = true;
        marker.BuildVisual(position, transitionId, title);
        var netObj = root.GetComponent<NetworkObject>();
        netObj.SpawnWithObservers = false;
        netObj.Spawn();
        if (LateJoinSync.Instance != null) LateJoinSync.Instance.QueueWorldObject(netObj);
        else
            foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
                if (clientId != NetworkManager.ServerClientId) netObj.NetworkShow(clientId);
    }

    public static void ResetRegistration()
    {
        var nm = _registeredManager;
        if (nm != null)
        {
            try { nm.PrefabHandler.RemoveHandler(PrefabHash); }
            catch { }
            if (RegisteredManagers.Remove(nm) && nm.CustomMessagingManager != null)
                nm.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_ACTIVATE);
        }
        _registeredManager = null;
    }

    private void BuildVisual(Vector3 position, string transitionId, string title)
    {
        _transitionId = transitionId;
        transform.position = position;
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        visual.name = "Visual";
        visual.transform.SetParent(transform, false);
        visual.transform.localScale = new Vector3(0.42f, 0.012f, 0.42f);
        var collider = visual.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        var renderer = visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                var material = new Material(shader);
                material.color = new Color(0.15f, 0.85f, 0.95f, 0.82f);
                renderer.material = material;
            }
        }
        var label = new GameObject("Label", typeof(TextMesh));
        label.transform.SetParent(transform, false);
        label.transform.localPosition = new Vector3(0, 0.18f, 0);
        label.transform.localScale = Vector3.one * 0.02f;
        var text = label.GetComponent<TextMesh>();
        text.text = string.IsNullOrWhiteSpace(title) ? "Переход" : title;
        text.anchor = TextAnchor.LowerCenter;
        text.alignment = TextAlignment.Center;
        text.color = Color.white;
        text.fontSize = 48;
        _visual = visual;
    }

    private void OnMouseDown()
    {
        if (!IsSpawned || !GameplayInputGate.AllowsWorldPointerInput
            || SceneEditor.IsEditing || FogManager.IsManualEditing || MeasurementTool.Instance?.IsLocalActive == true
            || EffectPaintTool.Instance?.IsActive == true || GameplayInputGate.IsPointerOverUI
            || string.IsNullOrWhiteSpace(_transitionId)) return;
        var nm = NetworkManager.Singleton;
        if (nm?.IsServer == true) SceneFileStore.UseSceneTransition(_transitionId);
        else if (nm != null && nm.IsConnectedClient)
        {
            using var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
            writer.WriteValueSafe(NetworkObjectId);
            nm.CustomMessagingManager.SendNamedMessage(MSG_ACTIVATE, NetworkManager.ServerClientId, writer);
        }
    }
}

public sealed class SceneTransitionMarkerSpawnHandler : INetworkPrefabInstanceHandler
{
    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        SceneTransitionMarker.RegisterHandler(NetworkManager.Singleton);
        var template = SceneTransitionMarker.GetTemplateForSpawn();
        if (template == null) return null;
        var go = Object.Instantiate(template, position, rotation);
        go.SetActive(true);
        return go.GetComponent<NetworkObject>();
    }

    public void Destroy(NetworkObject netObj)
    {
        if (netObj != null) Object.Destroy(netObj.gameObject);
    }
}
