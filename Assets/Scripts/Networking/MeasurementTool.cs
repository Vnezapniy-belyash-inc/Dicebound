using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Инструмент измерения: линейка, радиус, квадрат, конус.
/// Любой игрок активирует, все видят. Снап к сетке.
/// Расстояние в футах: 1 клетка = 5 футов.
/// </summary>
public class MeasurementTool : NetworkBehaviour
{
    public enum Mode { Ruler, Circle, Square, Cone }

    [Header("Visual")]
    public float lineWidth = 0.06f;
    public float yOffset = 0.02f;
    public int circleSegments = 48;

    private NetworkVariable<Vector3> _netPointA = new(Vector3.zero,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> _netPointB = new(Vector3.zero,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<bool> _netActive = new(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<int> _netMode = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private NetworkVariable<Vector3> _netColor = new(Vector3.one,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private LineRenderer _lr;
    private bool _isDragging;
    private float _lastSyncTime;
    private Camera _cam;

    // Превью клеток
    private readonly List<GameObject> _previewCells = new();
    private Material _previewMat;

    public static MeasurementTool Instance { get; private set; }
    public bool IsActive => _netActive.Value;

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

        _lr = gameObject.AddComponent<LineRenderer>();
        _lr.material = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        _lr.startWidth = lineWidth;
        _lr.endWidth = lineWidth;
        _lr.useWorldSpace = true;
        _lr.enabled = false;
        _lr.positionCount = 0;
        _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _lr.receiveShadows = false;

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
        // Применить текущий цвет сразу (OnValueChanged не сработает на начальное значение)
        ApplyColor(_netColor.Value);

        _netActive.OnValueChanged += (old, val) => UpdateVisual();
        _netPointA.OnValueChanged += (old, val) => UpdateVisual();
        _netPointB.OnValueChanged += (old, val) => UpdateVisual();
        _netMode.OnValueChanged += (old, val) => UpdateVisual();
        _netColor.OnValueChanged += (old, val) => ApplyColor(val);

        // Обновить визуал для начального состояния (late-join)
        UpdateVisual();
    }

    private void ApplyColor(Vector3 rgb)
    {
        Color c = new Color(rgb.x, rgb.y, rgb.z, 0.85f);
        _lr.material.color = c;
        _lr.startColor = c;
        _lr.endColor = c;
    }

    // ═══ Активация ═══

    public void Activate(int mode)
    {
        if (!IsSpawned) return;
        if (IsServer)
        {
            ActivateInternal(mode, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            RequestActivateServerRpc(mode);
        }
    }

    [Rpc(SendTo.Server)]
    private void RequestActivateServerRpc(int mode, RpcParams rpcParams = default)
    {
        ActivateInternal(mode, rpcParams.Receive.SenderClientId);
    }

    private void ActivateInternal(int mode, ulong clientId)
    {
        _netActive.Value = true;
        _netMode.Value = mode;
        _netPointA.Value = Vector3.zero;
        _netPointB.Value = Vector3.zero;

        // Цвет игрока
        Color pc = PlayerColors.GetColor(clientId);
        _netColor.Value = new Vector3(pc.r, pc.g, pc.b);

        // Передать владение активирующему клиенту
        GetComponent<NetworkObject>().ChangeOwnership(clientId);
    }

    public void Deactivate()
    {
        if (IsServer)
            _netActive.Value = false;
        else
            RequestDeactivateServerRpc();
    }

    [Rpc(SendTo.Server)]
    private void RequestDeactivateServerRpc()
    {
        _netActive.Value = false;
    }

    // ═══ Ввод (только владелец) ═══

    private void Update()
    {
        if (!IsOwner || !_netActive.Value) return;

        // Не обрабатываем клики по UI
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true ||
            mouse.rightButton.wasPressedThisFrame)
        {
            Deactivate();
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector3? hit = RaycastGrid(mouse);
            if (hit.HasValue)
            {
                Vector3 p = SnapToGridCell(hit.Value);
                if (IsServer)
                {
                    _netPointA.Value = p;
                    _netPointB.Value = p;
                }
                else
                {
                    SetPointServerRpc(p, p);
                }
                _isDragging = true;
                OnDragStart?.Invoke();
            }
        }

        if (_isDragging && mouse.leftButton.isPressed)
        {
            Vector3? hit = RaycastGrid(mouse);
            if (hit.HasValue && Time.time - _lastSyncTime > 0.08f)
            {
                Vector3 p = SnapToGridCell(hit.Value);
                if (IsServer)
                    _netPointB.Value = p;
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
                Vector3 p = SnapToGridCell(hit.Value);
                if (IsServer)
                    _netPointB.Value = p;
                else
                    UpdatePointBServerRpc(p);
            }
            OnDragEnd?.Invoke();
        }
    }

    [Rpc(SendTo.Server)]
    private void SetPointServerRpc(Vector3 a, Vector3 b)
    {
        _netPointA.Value = a;
        _netPointB.Value = b;
    }

    [Rpc(SendTo.Server)]
    private void UpdatePointBServerRpc(Vector3 b)
    {
        _netPointB.Value = b;
    }

    // ═══ Визуал ═══

    private void UpdateVisual()
    {
        if (!_netActive.Value)
        {
            _lr.enabled = false;
            _lr.positionCount = 0;
            ClearPreviews();
            return;
        }

        _lr.enabled = true;
        Mode mode = (Mode)_netMode.Value;
        Vector3 a = _netPointA.Value;
        Vector3 b = _netPointB.Value;
        a.y = yOffset;
        b.y = yOffset;

        switch (mode)
        {
            case Mode.Ruler:
                _lr.positionCount = 2;
                _lr.SetPosition(0, a);
                _lr.SetPosition(1, b);
                break;

            case Mode.Circle:
                float radius = Vector3.Distance(a, b);
                DrawCircle(a, radius);
                break;

            case Mode.Square:
                DrawSquare(a, b);
                break;

            case Mode.Cone:
                DrawCone(a, b);
                break;
        }

        // Обновить превью клеток
        if (mode != Mode.Ruler)
            UpdatePreviews();
        else
            ClearPreviews();
    }

    // ═══ Превью клеток (серое мерцание) ═══

    private void UpdatePreviews()
    {
        var cells = GetCellsInArea();
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;

        // Удаляем лишние, создаём недостающие
        while (_previewCells.Count > cells.Count)
        {
            var go = _previewCells[_previewCells.Count - 1];
            _previewCells.RemoveAt(_previewCells.Count - 1);
            if (go != null) Destroy(go);
        }

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 pos = gm.GetCellCenter(cells[i].x, cells[i].y, yOffset);
            GameObject go;
            if (i < _previewCells.Count)
            {
                go = _previewCells[i];
                go.transform.position = pos;
            }
            else
            {
                go = CreatePreviewQuad(pos);
                _previewCells.Add(go);
            }
        }
    }

    private GameObject CreatePreviewQuad(Vector3 pos)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "PreviewCell";
        go.transform.position = pos;
        go.transform.rotation = Quaternion.Euler(90, 0, 0);
        go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

        var collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        // Кешируем материал
        if (_previewMat == null)
        {
            _previewMat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
            _previewMat.SetFloat("_Surface", 1f);
            _previewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _previewMat.SetInt("_SrcBlend", 5);
            _previewMat.SetInt("_DstBlend", 10);
            _previewMat.SetInt("_ZWrite", 0);
            _previewMat.renderQueue = 3000;
        }

        var mr = go.GetComponent<MeshRenderer>();
        mr.material = _previewMat;
        mr.material.color = new Color(0.4f, 0.4f, 0.4f, 0.3f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        var blinker = go.AddComponent<PreviewBlinker>();
        return go;
    }

    private void ClearPreviews()
    {
        foreach (var go in _previewCells)
        {
            if (go != null) Destroy(go);
        }
        _previewCells.Clear();
    }

    private void DrawCircle(Vector3 center, float radius)
    {
        _lr.positionCount = circleSegments + 1;
        for (int i = 0; i <= circleSegments; i++)
        {
            float angle = i * Mathf.PI * 2f / circleSegments;
            float x = center.x + Mathf.Cos(angle) * radius;
            float z = center.z + Mathf.Sin(angle) * radius;
            _lr.SetPosition(i, new Vector3(x, yOffset, z));
        }
    }

    private void DrawSquare(Vector3 corner, Vector3 opposite)
    {
        _lr.positionCount = 5;
        float halfCell = 0.5f;
        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null) halfCell = gm.CellSize / 2f;

        float minX = Mathf.Min(corner.x, opposite.x) - halfCell;
        float maxX = Mathf.Max(corner.x, opposite.x) + halfCell;
        float minZ = Mathf.Min(corner.z, opposite.z) - halfCell;
        float maxZ = Mathf.Max(corner.z, opposite.z) + halfCell;

        _lr.SetPosition(0, new Vector3(minX, yOffset, minZ));
        _lr.SetPosition(1, new Vector3(maxX, yOffset, minZ));
        _lr.SetPosition(2, new Vector3(maxX, yOffset, maxZ));
        _lr.SetPosition(3, new Vector3(minX, yOffset, maxZ));
        _lr.SetPosition(4, new Vector3(minX, yOffset, minZ));
    }

    private void DrawCone(Vector3 origin, Vector3 target)
    {
        Vector3 dir = target - origin;
        float length = dir.magnitude;
        if (length < 0.01f) { _lr.positionCount = 0; return; }
        dir /= length;

        // Перпендикуляр в плоскости XZ
        Vector3 perp = new Vector3(-dir.z, 0, dir.x).normalized;
        float halfWidth = length * 0.5f; // D&D: ширина = расстоянию

        Vector3 o = new Vector3(origin.x, yOffset, origin.z);
        Vector3 r = o + dir * length + perp * halfWidth;
        Vector3 l = o + dir * length - perp * halfWidth;

        // Замкнутый треугольник
        _lr.positionCount = 4;
        _lr.SetPosition(0, o);
        _lr.SetPosition(1, r);
        _lr.SetPosition(2, l);
        _lr.SetPosition(3, o);
    }

    // ═══ Метка расстояния (OnGUI) ═══

    private void OnGUI()
    {
        if (!_netActive.Value || _cam == null) return;

        Vector3 a = _netPointA.Value;
        Vector3 b = _netPointB.Value;
        if (a == Vector3.zero && b == Vector3.zero) return;

        // Метка у точки B (дальний конец)
        Vector3 screenPos = _cam.WorldToScreenPoint(b);
        if (screenPos.z < 0) return;

        float ft;
        Mode mode = (Mode)_netMode.Value;
        if (mode == Mode.Square)
        {
            // Для квадрата — длина стороны (центры клеток → +1 клетка)
            float w = Mathf.Abs(b.x - a.x);
            float h = Mathf.Abs(b.z - a.z);
            ft = (Mathf.Max(w, h) + 1f) * 5f;
        }
        else if (mode == Mode.Cone)
        {
            ft = Vector3.Distance(a, b) / 1f * 5f;
        }
        else
        {
            ft = Vector3.Distance(a, b) / 1f * 5f;
        }

        string label = $"{ft:F0} ft";

        Vector2 guiPos = new Vector2(screenPos.x, Screen.height - screenPos.y);
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 18;
        style.fontStyle = FontStyle.Bold;
        style.normal.textColor = Color.white;
        style.alignment = TextAnchor.MiddleCenter;

        Vector2 size = style.CalcSize(new GUIContent(label));
        Rect rect = new Rect(guiPos.x - size.x / 2f, guiPos.y - size.y - 8f, size.x + 16, size.y + 8);
        GUI.Box(rect, "");
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

    // ═══ Получение клеток в области ═══

    /// <summary>Возвращает все клетки внутри текущей измеряемой области.</summary>
    public List<Vector2Int> GetCellsInArea()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return new List<Vector2Int>();

        Mode mode = (Mode)_netMode.Value;
        Vector3 a = _netPointA.Value;
        Vector3 b = _netPointB.Value;

        return mode switch
        {
            Mode.Circle => GetCellsInCircle(gm, a, Vector3.Distance(a, b)),
            Mode.Square => GetCellsInSquare(gm, a, b),
            Mode.Cone => GetCellsInCone(gm, a, b),
            _ => new List<Vector2Int>(),
        };
    }

    private List<Vector2Int> GetCellsInCircle(GridManager gm, Vector3 center, float radius)
    {
        var cells = new List<Vector2Int>();
        if (radius < 0.01f) return cells;

        // Перебираем клетки в bounding box круга
        Vector2Int min = gm.GetGridPosition(center - new Vector3(radius, 0, radius));
        Vector2Int max = gm.GetGridPosition(center + new Vector3(radius, 0, radius));
        min.x = Mathf.Max(min.x, 0); min.y = Mathf.Max(min.y, 0);
        max.x = Mathf.Min(max.x, gm.Width - 1); max.y = Mathf.Min(max.y, gm.Height - 1);

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

    private List<Vector2Int> GetCellsInSquare(GridManager gm, Vector3 corner, Vector3 opposite)
    {
        var cells = new List<Vector2Int>();
        float minX = Mathf.Min(corner.x, opposite.x);
        float maxX = Mathf.Max(corner.x, opposite.x);
        float minZ = Mathf.Min(corner.z, opposite.z);
        float maxZ = Mathf.Max(corner.z, opposite.z);

        Vector2Int min = gm.GetGridPosition(new Vector3(minX, 0, minZ));
        Vector2Int max = gm.GetGridPosition(new Vector3(maxX, 0, maxZ));
        min.x = Mathf.Max(min.x, 0); min.y = Mathf.Max(min.y, 0);
        max.x = Mathf.Min(max.x, gm.Width - 1); max.y = Mathf.Min(max.y, gm.Height - 1);

        for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
                cells.Add(new Vector2Int(x, y));
        return cells;
    }

    private List<Vector2Int> GetCellsInCone(GridManager gm, Vector3 origin, Vector3 target)
    {
        var cells = new List<Vector2Int>();
        Vector3 dir3 = target - origin;
        float length = dir3.magnitude;
        if (length < 0.01f) return cells;

        Vector2 dir = new Vector2(dir3.x, dir3.z).normalized;
        Vector2 perp = new Vector2(-dir.y, dir.x);
        Vector2 o2 = new Vector2(origin.x, origin.z);

        // Перебираем клетки в bounding box конуса
        Vector2Int min = gm.GetGridPosition(origin - new Vector3(length, 0, length));
        Vector2Int max = gm.GetGridPosition(origin + new Vector3(length, 0, length));
        min.x = Mathf.Max(min.x, 0); min.y = Mathf.Max(min.y, 0);
        max.x = Mathf.Min(max.x, gm.Width - 1); max.y = Mathf.Min(max.y, gm.Height - 1);

        for (int x = min.x; x <= max.x; x++)
        {
            for (int y = min.y; y <= max.y; y++)
            {
                Vector3 cellCenter = gm.GetCellCenter(x, y);
                Vector2 p = new Vector2(cellCenter.x, cellCenter.z) - o2;
                float proj = Vector2.Dot(p, dir);
                if (proj < 0 || proj > length) continue;
                float perpDist = Mathf.Abs(Vector2.Dot(p, perp));
                if (perpDist <= proj * 0.5f) // D&D: half-width = distance/2
                    cells.Add(new Vector2Int(x, y));
            }
        }
        return cells;
    }

    /// <summary>Применить область: спавнит CellMarker на всех клетках и очищает измерение.</summary>
    public void ApplyArea(int textureIndex)
    {
        if (!IsOwner || !_netActive.Value) return;
        var cells = GetCellsInArea();
        if (cells.Count == 0) return;

        if (IsServer)
            SpawnMarkers(cells, textureIndex);
        else
            RequestApplyAreaServerRpc(SerializeCells(cells), textureIndex);

        Deactivate();
    }

    [Rpc(SendTo.Server)]
    private void RequestApplyAreaServerRpc(string cellData, int textureIndex)
    {
        var cells = DeserializeCells(cellData);
        SpawnMarkers(cells, textureIndex);
    }

    private void SpawnMarkers(List<Vector2Int> cells, int textureIndex)
    {
        foreach (var cell in cells)
        {
            var gm = FindAnyObjectByType<GridManager>();
            if (gm == null) continue;
            Vector3 pos = gm.GetCellCenter(cell.x, cell.y, 0.015f);
            CellMarker.Spawn(pos, textureIndex);
        }
    }

    private static string SerializeCells(List<Vector2Int> cells)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in cells)
            sb.Append($"{c.x},{c.y};");
        return sb.ToString();
    }

    private static List<Vector2Int> DeserializeCells(string data)
    {
        var list = new List<Vector2Int>();
        if (string.IsNullOrEmpty(data)) return list;
        foreach (var pair in data.Split(';', System.StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[0], out int x) && int.TryParse(parts[1], out int y))
                list.Add(new Vector2Int(x, y));
        }
        return list;
    }

    public Mode CurrentMode => (Mode)_netMode.Value;

    private new void OnDestroy()
    {
        ClearPreviews();
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
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
        // Медленное синхронизированное мерцание (Time.time для одинаковой фазы)
        float alpha = 0.15f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 4f));
        _mat.color = new Color(0.4f, 0.4f, 0.4f, alpha);
    }
}
