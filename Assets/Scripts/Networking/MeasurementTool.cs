using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Инструмент измерения: линейка, радиус, квадрат, конус.
/// У каждого игрока своя независимая сессия — несколько инструментов одновременно.
/// Расстояние в футах: 1 клетка = 5 футов.
/// </summary>
public class MeasurementTool : NetworkBehaviour
{
    public enum Mode { Ruler, Circle, Square, Cone }
    private const int MaxAreaCells = 2000;
    private const int MarkersPerFrame = 16;

    [Header("Visual")]
    public float lineWidth = 0.06f;
    public float yOffset = 0.02f;
    public int circleSegments = 48;
    [Tooltip("Snap radius as fraction of cell size for sphere center vs corner")]
    public float sphereSnapTolerance = 0.4f;
    [Tooltip("Size of origin marker for sphere (cell center dot / intersection cross)")]
    public float sphereOriginMarkerSize = 0.12f;

    private NetworkList<MeasurementSnapshot> _sessions = new NetworkList<MeasurementSnapshot>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly Dictionary<ulong, MeasurementSessionVisual> _visuals = new();
    private MeasurementSnapshot _localSession;
    private bool _isDragging;
    private bool _pendingDeactivate;
    private float _lastSyncTime;
    private Camera _cam;

    public static MeasurementTool Instance { get; private set; }

    /// <summary>Локальный игрок использует инструмент.</summary>
    public bool IsLocalActive
    {
        get
        {
            var nm = NetworkManager.Singleton;
            return !_pendingDeactivate && nm != null && _localSession.Active &&
                _localSession.ClientId == nm.LocalClientId;
        }
    }

    /// <summary>Совместимость: активна ли локальная сессия.</summary>
    public bool IsActive => IsLocalActive;

    public Mode CurrentMode =>
        TryGetLocalSnapshot(out var snap) ? (Mode)snap.Mode : Mode.Ruler;

    public System.Action OnDragStart;
    public System.Action OnDragEnd;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Start()
    {
        _cam = Camera.main;

        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    private void OnServerStarted()
    {
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned)
            netObj.Spawn();
    }

    public override void OnNetworkSpawn()
    {
        _sessions.OnListChanged += OnSessionsChanged;
        RebuildAllVisuals();
    }

    public override void OnNetworkDespawn()
    {
        _sessions.OnListChanged -= OnSessionsChanged;
    }

    // ═══ Активация ═══

    public void Activate(int mode)
    {
        EffectPaintTool.Instance?.Deactivate();
        _pendingDeactivate = false;
        EnsureSpawned();
        if (!IsSpawned) return;

        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        if (IsServer)
        {
            ActivateInternal(nm.LocalClientId, mode);
        }
        else
        {
            // Optimistic local session so first click/drag works before list sync arrives.
            Color pc = PlayerColors.GetColor(nm.LocalClientId);
            _localSession = MeasurementSnapshot.Create(nm.LocalClientId, mode, pc);
            RequestActivateServerRpc(mode);
        }
    }

    void EnsureSpawned()
    {
        if (IsSpawned) return;
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && IsServer && !netObj.IsSpawned)
            netObj.Spawn();
    }

    [Rpc(SendTo.Server)]
    private void RequestActivateServerRpc(int mode, RpcParams rpcParams = default)
    {
        ActivateInternal(rpcParams.Receive.SenderClientId, mode);
    }

    private void ActivateInternal(ulong clientId, int mode)
    {
        Color pc = PlayerRegistry.GetServerPlayerColor(clientId);
        var snap = MeasurementSnapshot.Create(clientId, mode, pc);
        UpsertSession(snap);
        MirrorLocalSession(snap);
    }

    public void Deactivate()
    {
        if (!IsSpawned || !IsLocalActive) return;
        GameplayInputGate.MarkToolExit();
        _pendingDeactivate = true;
        _localSession = default;
        _isDragging = false;
        if (IsServer)
            DeactivateInternal(NetworkManager.Singleton.LocalClientId);
        else
            RequestDeactivateServerRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestDeactivateServerRpc(RpcParams rpcParams = default)
    {
        DeactivateInternal(rpcParams.Receive.SenderClientId);
    }

    private void DeactivateInternal(ulong clientId)
    {
        int idx = FindSessionIndex(clientId);
        if (idx < 0) return;

        ulong removedId = _sessions[idx].ClientId;
        _sessions.RemoveAt(idx);
        RemoveVisual(removedId);

        var nm = NetworkManager.Singleton;
        if (nm != null && removedId == nm.LocalClientId)
            _localSession = default;
    }

    private void UpsertSession(MeasurementSnapshot snap)
    {
        int idx = FindSessionIndex(snap.ClientId);
        if (idx >= 0)
            _sessions.Set(idx, snap, forceUpdate: true);
        else
            _sessions.Add(snap);
    }

    private int FindSessionIndex(ulong clientId)
    {
        for (int i = 0; i < _sessions.Count; i++)
        {
            if (_sessions[i].ClientId == clientId)
                return i;
        }

        return -1;
    }

    public bool TryGetLocalSnapshot(out MeasurementSnapshot snap)
    {
        snap = default;
        if (_pendingDeactivate) return false;
        var nm = NetworkManager.Singleton;
        if (nm == null) return false;

        if (_localSession.ClientId == nm.LocalClientId && _localSession.Active)
        {
            snap = _localSession;
            return true;
        }

        ulong localId = nm.LocalClientId;
        for (int i = 0; i < _sessions.Count; i++)
        {
            if (_sessions[i].ClientId == localId && _sessions[i].Active)
            {
                snap = _sessions[i];
                _localSession = snap;
                return true;
            }
        }

        return false;
    }

    void MirrorLocalSession(MeasurementSnapshot snap)
    {
        var nm = NetworkManager.Singleton;
        if (nm != null && snap.ClientId == nm.LocalClientId && snap.Active)
            _localSession = snap;
    }

    void ApplyLocalPoints(Vector3 a, Vector3 b, bool markReady = false)
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !_localSession.Active || _localSession.ClientId != nm.LocalClientId)
            return;

        var snap = _localSession;
        snap.PointA = a;
        snap.PointB = b;
        if (markReady || snap.HasPoints)
            snap.PointsReadyFlag = 1;
        _localSession = snap;
        ApplyVisual(snap);
    }

    // ═══ Ввод (локальная сессия) ═══

    private void Update()
    {
        if (!IsLocalActive) return;
        if (GameplayInputGate.AllowsKeyboardHotkeys &&
            Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            Deactivate();
            return;
        }
        if (!GameplayInputGate.AllowsWorldPointerInput)
        {
            if (_isDragging && Mouse.current?.leftButton.wasReleasedThisFrame == true)
                _isDragging = false;
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.wasPressedThisFrame)
        {
            Deactivate();
            _isDragging = false;
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector3? hit = RaycastGrid(mouse);
            if (hit.HasValue)
            {
                Vector3 p = SnapForMode(hit.Value);
                ApplyLocalPoints(p, p, markReady: true);
                if (IsServer)
                    SetPointsInternal(NetworkManager.Singleton.LocalClientId, p, p);
                else
                    SetPointServerRpc(p, p);

                _isDragging = true;
                OnDragStart?.Invoke();
            }
        }

        if (_isDragging && mouse.leftButton.isPressed)
        {
            Vector3? hit = RaycastGrid(mouse);
            if (hit.HasValue && Time.time - _lastSyncTime > 0.08f)
            {
                Vector3 p = SnapForMode(hit.Value);
                ApplyLocalPoints(_localSession.PointA, p);
                if (IsServer)
                    SetPointBInternal(NetworkManager.Singleton.LocalClientId, p);
                else
                    UpdatePointBServerRpc(p);

                _lastSyncTime = Time.time;
            }
        }

        if (_isDragging && mouse.leftButton.wasReleasedThisFrame)
        {
            _isDragging = false;
            Vector3? hit = RaycastGrid(mouse);
            if (hit.HasValue)
            {
                Vector3 p = SnapForMode(hit.Value);
                ApplyLocalPoints(_localSession.PointA, p);
                if (IsServer)
                    SetPointBInternal(NetworkManager.Singleton.LocalClientId, p);
                else
                    UpdatePointBServerRpc(p);
            }

            OnDragEnd?.Invoke();
        }
    }

    private Vector3 SnapForMode(Vector3 worldPos)
    {
        if (!TryGetLocalSnapshot(out var snap))
            return SnapToGridCell(worldPos);

        if ((Mode)snap.Mode == Mode.Circle)
        {
            var gm = FindAnyObjectByType<GridManager>();
            if (gm != null)
                return gm.SnapSpherePoint(worldPos, yOffset, out _, sphereSnapTolerance);
        }

        return SnapToGridCell(worldPos);
    }

    [Rpc(SendTo.Server)]
    private void SetPointServerRpc(Vector3 a, Vector3 b, RpcParams rpcParams = default)
    {
        SetPointsInternal(rpcParams.Receive.SenderClientId, a, b);
    }

    [Rpc(SendTo.Server)]
    private void UpdatePointBServerRpc(Vector3 b, RpcParams rpcParams = default)
    {
        SetPointBInternal(rpcParams.Receive.SenderClientId, b);
    }

    private void SetPointsInternal(ulong clientId, Vector3 a, Vector3 b)
    {
        int idx = FindSessionIndex(clientId);
        if (idx < 0 || !_sessions[idx].Active) return;

        var snap = _sessions[idx];
        snap.PointA = a;
        snap.PointB = b;
        snap.PointsReadyFlag = 1;
        CommitSession(snap);
    }

    private void SetPointBInternal(ulong clientId, Vector3 b)
    {
        int idx = FindSessionIndex(clientId);
        if (idx < 0 || !_sessions[idx].Active) return;

        var nm = NetworkManager.Singleton;
        bool isLocal = nm != null && clientId == nm.LocalClientId;

        var snap = _sessions[idx];
        if (isLocal && _localSession.HasPoints)
            snap.PointA = _localSession.PointA;
        snap.PointB = b;
        snap.PointsReadyFlag = 1;
        CommitSession(snap);
    }

    void CommitSession(MeasurementSnapshot snap)
    {
        int idx = FindSessionIndex(snap.ClientId);
        if (idx < 0) return;

        _sessions.Set(idx, snap, forceUpdate: true);

        var nm = NetworkManager.Singleton;
        if (nm != null && snap.ClientId == nm.LocalClientId)
            _localSession = snap;

        ApplyVisual(snap);
    }

    // ═══ Синхронизация визуала ═══

    private void OnSessionsChanged(NetworkListEvent<MeasurementSnapshot> changeEvent)
    {
        var nm = NetworkManager.Singleton;
        bool isLocal = nm != null && changeEvent.Value.ClientId == nm.LocalClientId;

        switch (changeEvent.Type)
        {
            case NetworkListEvent<MeasurementSnapshot>.EventType.Add:
            case NetworkListEvent<MeasurementSnapshot>.EventType.Insert:
            case NetworkListEvent<MeasurementSnapshot>.EventType.Value:
                if (isLocal)
                {
                    // Локальный визуал обновляем сами при вводе — не затираем из сети.
                    if (!changeEvent.Value.Active)
                        _localSession = default;
                    else if (!_localSession.HasPoints)
                        MirrorLocalSession(changeEvent.Value);
                }
                else
                {
                    ApplyVisual(changeEvent.Value);
                }
                break;

            case NetworkListEvent<MeasurementSnapshot>.EventType.RemoveAt:
                RemoveVisual(changeEvent.Value.ClientId);
                if (isLocal)
                {
                    _localSession = default;
                    _pendingDeactivate = false;
                }
                break;

            case NetworkListEvent<MeasurementSnapshot>.EventType.Clear:
                ClearAllVisuals();
                _localSession = default;
                _pendingDeactivate = false;
                break;

            case NetworkListEvent<MeasurementSnapshot>.EventType.Full:
                RebuildAllVisuals();
                break;
        }
    }

    private void RebuildAllVisuals()
    {
        ClearAllVisuals();

        var nm = NetworkManager.Singleton;
        for (int i = 0; i < _sessions.Count; i++)
        {
            var snap = _sessions[i];
            if (nm != null && snap.ClientId == nm.LocalClientId)
                MirrorLocalSession(snap);
            ApplyVisual(snap);
        }
    }

    private void ApplyVisual(MeasurementSnapshot snap)
    {
        if (!snap.Active || !snap.HasPoints)
        {
            if (_visuals.TryGetValue(snap.ClientId, out var hidden))
                hidden.Hide();
            return;
        }

        if (!_visuals.TryGetValue(snap.ClientId, out var visual))
        {
            visual = MeasurementSessionVisual.Create(
                transform, lineWidth, yOffset, circleSegments,
                sphereSnapTolerance, sphereOriginMarkerSize);
            _visuals[snap.ClientId] = visual;
        }

        visual.Apply(snap);
    }

    private void RemoveVisual(ulong clientId)
    {
        if (_visuals.TryGetValue(clientId, out var visual))
        {
            visual.Destroy();
            _visuals.Remove(clientId);
        }
    }

    private void ClearAllVisuals()
    {
        foreach (var visual in _visuals.Values)
            visual.Destroy();
        _visuals.Clear();
    }

    // ═══ Метки расстояния ═══

    private void OnGUI()
    {
        if (!GameplayInputGate.AllowsImGuiOverlays) return;
        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        var nm = NetworkManager.Singleton;
        var labels = new Dictionary<ulong, MeasurementSnapshot>();

        for (int i = 0; i < _sessions.Count; i++)
        {
            var snap = _sessions[i];
            if (!snap.Active || !snap.HasPoints) continue;
            labels[snap.ClientId] = snap;
        }

        if (nm != null
            && _localSession.Active
            && _localSession.HasPoints
            && _localSession.ClientId == nm.LocalClientId)
        {
            labels[nm.LocalClientId] = _localSession;
        }

        foreach (var snap in labels.Values)
            DrawDistanceLabel(snap);
    }

    private void DrawDistanceLabel(MeasurementSnapshot snap)
    {
        Vector3 a = snap.PointA;
        Vector3 b = snap.PointB;
        if (!snap.HasPoints) return;

        Vector3 screenPos = _cam.WorldToScreenPoint(b);
        if (screenPos.z < 0) return;

        float ft;
        var mode = (Mode)snap.Mode;
        if (mode == Mode.Square)
        {
            float w = Mathf.Abs(b.x - a.x);
            float h = Mathf.Abs(b.z - a.z);
            ft = (Mathf.Max(w, h) + 1f) * 5f;
        }
        else
        {
            ft = Vector3.Distance(a, b) * 5f;
        }

        string label = $"{ft:F0} ft";
        if (mode == Mode.Circle)
        {
            var gm = FindAnyObjectByType<GridManager>();
            bool isIntersection = GridManager.IsIntersectionPosition(a, gm);
            label += isIntersection ? " (corner)" : " (center)";
        }

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        Color playerColor = new Color(snap.ColorRgb.x, snap.ColorRgb.y, snap.ColorRgb.z);
        style.normal.textColor = playerColor;

        Vector2 size = style.CalcSize(new GUIContent(label));
        Rect rect = new Rect(guiPos.x - size.x / 2f - 8f, guiPos.y - size.y - 12f, size.x + 16f, size.y + 8f);

        var bgStyle = new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter
        };
        bgStyle.normal.background = Texture2D.whiteTexture;
        Color oldColor = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.65f);
        GUI.Box(rect, GUIContent.none, bgStyle);
        GUI.color = oldColor;

        var shadowStyle = new GUIStyle(style);
        shadowStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, shadowStyle);
        GUI.Label(rect, label, style);
    }

    // ═══ Утилиты ═══

    private Vector3? RaycastGrid(Mouse mouse)
    {
        if (_cam == null) return null;
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        Ray ray = _cam.ScreenPointToRay(mouse.position.ReadValue());
        if (plane.Raycast(ray, out float dist))
            return ray.GetPoint(dist);
        return null;
    }

    private Vector3 SnapToGridCell(Vector3 pos)
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return pos;
        Vector2Int cell = gm.GetGridPosition(pos);
        if (cell.x < 0) return pos;
        return gm.GetCellCenter(cell.x, cell.y, yOffset);
    }

    public static float DistanceInFeet(Vector3 a, Vector3 b, float cellSize = 1f)
    {
        return Vector3.Distance(a, b) / cellSize * 5f;
    }

    public List<Vector2Int> GetCellsInArea()
    {
        return TryGetLocalSnapshot(out var snap)
            ? GetCellsForSnapshot(snap)
            : new List<Vector2Int>();
    }

    public static List<Vector2Int> GetCellsForSnapshot(MeasurementSnapshot snap)
    {
        var gm = Object.FindAnyObjectByType<GridManager>();
        if (gm == null || !snap.Active || !snap.HasPoints) return new List<Vector2Int>();

        var mode = (Mode)snap.Mode;
        Vector3 a = snap.PointA;
        Vector3 b = snap.PointB;

        return mode switch
        {
            Mode.Circle => GetCellsInCircle(gm, a, Vector3.Distance(a, b)),
            Mode.Square => GetCellsInSquare(gm, a, b),
            Mode.Cone => GetCellsInCone(gm, a, b),
            _ => new List<Vector2Int>(),
        };
    }

    private static List<Vector2Int> GetCellsInCircle(GridManager gm, Vector3 center, float radius)
    {
        var cells = new List<Vector2Int>();
        if (radius < 0.01f) return cells;

        gm.GetCellRange(center - new Vector3(radius, 0, radius),
            center + new Vector3(radius, 0, radius), out Vector2Int min, out Vector2Int max);

        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector3 cellCenter = gm.GetCellCenter(x, y);
                if (Vector3.Distance(cellCenter, center) <= radius)
                    cells.Add(new Vector2Int(x, y));
            }
        }

        return cells;
    }

    private static List<Vector2Int> GetCellsInSquare(GridManager gm, Vector3 corner, Vector3 opposite)
    {
        var cells = new List<Vector2Int>();
        float minX = Mathf.Min(corner.x, opposite.x);
        float maxX = Mathf.Max(corner.x, opposite.x);
        float minZ = Mathf.Min(corner.z, opposite.z);
        float maxZ = Mathf.Max(corner.z, opposite.z);

        gm.GetCellRange(new Vector3(minX, 0, minZ), new Vector3(maxX, 0, maxZ),
            out Vector2Int min, out Vector2Int max);

        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                cells.Add(new Vector2Int(x, y));

        return cells;
    }

    private static List<Vector2Int> GetCellsInCone(GridManager gm, Vector3 origin, Vector3 target)
    {
        var cells = new List<Vector2Int>();
        Vector3 dir3 = target - origin;
        float length = dir3.magnitude;
        if (length < 0.01f) return cells;

        Vector2 dir = new Vector2(dir3.x, dir3.z).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x);
        Vector2 o2 = new Vector2(origin.x, origin.z);

        gm.GetCellRange(origin - new Vector3(length, 0, length),
            origin + new Vector3(length, 0, length), out Vector2Int min, out Vector2Int max);

        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector3 cellCenter = gm.GetCellCenter(x, y);
                Vector2 p = new Vector2(cellCenter.x, cellCenter.z) - o2;
                float proj = Vector2.Dot(p, dir);
                if (proj < 0 || proj > length) continue;
                float perpDist = Mathf.Abs(Vector2.Dot(p, perp));
                if (perpDist <= proj * 0.5f)
                    cells.Add(new Vector2Int(x, y));
            }
        }

        return cells;
    }

    /// <summary>Применить область: спавнит CellMarker на всех клетках и очищает измерение.</summary>
    public void ApplyArea(int textureIndex)
    {
        if (!IsLocalActive) return;

        if (!TryGetLocalSnapshot(out var snap)) return;
        var cells = GetCellsInArea();
        if (cells.Count == 0) return;
        if (cells.Count > MaxAreaCells)
        {
            DiceUI.Instance?.ShowToolNotice("Слишком большая область (максимум 2000 клеток).");
            return;
        }

        if (IsServer)
            SpawnMarkers(cells, textureIndex, NetworkManager.Singleton.LocalClientId);
        else
            RequestApplyAreaServerRpc(snap.PointA, snap.PointB, textureIndex);

        Deactivate();
    }

    [Rpc(SendTo.Server)]
    private void RequestApplyAreaServerRpc(Vector3 pointA, Vector3 pointB, int textureIndex,
        RpcParams rpcParams = default)
    {
        int index = FindSessionIndex(rpcParams.Receive.SenderClientId);
        if (index < 0 || !_sessions[index].Active || !_sessions[index].HasPoints
            || !Finite(pointA) || !Finite(pointB)) return;
        var snap = _sessions[index];
        snap.PointA = pointA;
        snap.PointB = pointB;
        var cells = GetCellsForSnapshot(snap);
        if (cells.Count == 0 || cells.Count > MaxAreaCells) return;
        SpawnMarkers(cells, textureIndex, rpcParams.Receive.SenderClientId);
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x)
        && !float.IsNaN(value.y) && !float.IsInfinity(value.y)
        && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private void SpawnMarkers(List<Vector2Int> cells, int textureIndex, ulong spawnerClientId)
    {
        if (!IsServer || textureIndex < 0 || textureIndex >= CellMarker.TextureColors.Length) return;
        StartCoroutine(SpawnMarkersRoutine(cells, textureIndex, spawnerClientId));
    }

    private System.Collections.IEnumerator SpawnMarkersRoutine(
        List<Vector2Int> cells, int textureIndex, ulong spawnerClientId)
    {
        int sent = 0;
        foreach (var cell in cells)
        {
            if (!IsServer) yield break;
            CellMarker.ServerApplyCell(cell, textureIndex, spawnerClientId);
            if (++sent % MarkersPerFrame == 0) yield return null;
        }
    }

    private new void OnDestroy()
    {
        ClearAllVisuals();
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        if (Instance == this)
            Instance = null;
    }
}

/// <summary>Мерцание серым для превью клеток.</summary>
public class PreviewBlinker : MonoBehaviour
{
    private Material _mat;

    private void Start()
    {
        _mat = GetComponent<MeshRenderer>().material;
    }

    private void Update()
    {
        float alpha = 0.15f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
        _mat.color = new Color(0.4f, 0.4f, 0.4f, alpha);
    }
}
