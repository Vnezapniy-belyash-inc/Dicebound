using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Менеджер игрового поля: визуальная сетка, привязка позиций к клеткам.
/// Базовая сетка 100×100, отображаются только клетки в границах карты.
/// Невидимые стены-коллайдеры следуют за границами карты (MapController.GetMapBounds).
/// </summary>
public class GridManager : MonoBehaviour
{
    [Header("Параметры сетки")]
    public int gridWidth = 100;
    public int gridHeight = 100;
    public float cellSize = 1f;

    [Header("Визуал")]
    public Color gridColor = new Color(0.15f, 0.15f, 0.15f, 0.5f); // 50% прозрачности
    [Range(0.01f, 0.2f)] public float lineWidth = 0.04f;
    public float yOffset = 0.005f;

    [Header("Стены")]
    public bool createWalls = true;
    public float wallHeight = 20f;
    [Tooltip("Толщина невидимых коллайдеров — должна быть достаточной, чтобы кубики не пролетали сквозь.")]
    public float wallThickness = 3f;
    public PhysicsMaterial wallPhysics;

    public int Width => gridWidth;
    public int Height => gridHeight;
    public float CellSize => cellSize;

    private GameObject _gridLinesParent;
    private GameObject _wallsParent;
    private readonly GameObject[] _wallObjs = new GameObject[4];
    private Vector3 _cachedWallSize = Vector3.zero;
    private List<GameObject> _hLines = new();
    private List<GameObject> _vLines = new();
    private Bounds _mapBounds;
    private Quaternion _mapRotation = Quaternion.identity;
    private bool _gridCreated;

    // Реестр занятых клеток (для предотвращения наложения токенов)
    private readonly HashSet<Vector2Int> _occupiedCells = new();

    void Start()
    {
        gridColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        GenerateFullGrid();
    }

    /// <summary>Обновляет границы карты — сетка и невидимые стены следуют за картой.</summary>
    public void SetBounds(Bounds mapBounds) => SetBounds(mapBounds, Quaternion.identity);

    public void SetBounds(Bounds mapBounds, Quaternion mapRotation)
    {
        if (_gridCreated && _gridLinesParent != null && Approximately(_mapBounds.center, mapBounds.center)
            && Approximately(_mapBounds.size, mapBounds.size)
            && Quaternion.Angle(_mapRotation, mapRotation) < 0.01f)
            return;
        _mapBounds = mapBounds;
        _mapRotation = mapRotation;
        if (!_gridCreated) GenerateFullGrid();
        _gridLinesParent.transform.position = _mapBounds.center + Vector3.up * yOffset;
        _gridLinesParent.transform.rotation = _mapRotation;
        UpdateVisibleCells();
        UpdateWalls(mapBounds);
    }

    // ═══ Создание полной сетки 100×100 (один раз) ═══

    void GenerateFullGrid()
    {
        if (yOffset < 0.005f) yOffset = 0.005f;
        if (_gridLinesParent != null)
        {
            if (Application.isPlaying) Destroy(_gridLinesParent);
            else DestroyImmediate(_gridLinesParent);
        }

        _gridLinesParent = new GameObject("GridLines");
        _gridLinesParent.transform.SetParent(transform);
        _gridLinesParent.transform.position = _mapBounds.center + Vector3.up * yOffset;
        _gridLinesParent.transform.rotation = _mapRotation;
        _hLines.Clear();
        _vLines.Clear();

        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;

        // Горизонтальные линии (вдоль X)
        for (int z = 0; z <= gridHeight; z++)
        {
            float zPos = -halfH + z * cellSize;
            var go = CreateGridLine(
                new Vector3(-halfW, 0, zPos),
                new Vector3(halfW, 0, zPos),
                gridColor, lineWidth, $"H_{z}");
            go.SetActive(false);
            _hLines.Add(go);
        }

        // Вертикальные линии (вдоль Z)
        for (int x = 0; x <= gridWidth; x++)
        {
            float xPos = -halfW + x * cellSize;
            var go = CreateGridLine(
                new Vector3(xPos, 0, -halfH),
                new Vector3(xPos, 0, halfH),
                gridColor, lineWidth, $"V_{x}");
            go.SetActive(false);
            _vLines.Add(go);
        }

        _gridCreated = true;
        Debug.Log($"[Grid] Generated {_hLines.Count}H × {_vLines.Count}V lines for {gridWidth}×{gridHeight} grid");
    }

    GameObject CreateGridLine(Vector3 start, Vector3 end, Color color, float width, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(_gridLinesParent.transform, worldPositionStays: false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"));
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetFloat("_Blend", 0f);   // Alpha blending
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetInt("_SrcBlend", 5);   // SrcAlpha
        mat.SetInt("_DstBlend", 10);  // OneMinusSrcAlpha
        mat.SetInt("_ZWrite", 0);     // Off
        mat.color = color;
        mat.renderQueue = 3000;
        lr.material = mat;
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.useWorldSpace = false;
        lr.positionCount = 2;
        lr.SetPosition(0, start);
        lr.SetPosition(1, end);
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return go;
    }

    // ═══ Показ только клеток в границах карты ═══

    private float _lastLogTime;

    void UpdateVisibleCells()
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        float mapMinX = -_mapBounds.size.x * 0.5f;
        float mapMaxX = _mapBounds.size.x * 0.5f;
        float mapMinZ = -_mapBounds.size.z * 0.5f;
        float mapMaxZ = _mapBounds.size.z * 0.5f;

        for (int z = 0; z <= gridHeight; z++)
        {
            float zPos = -halfH + z * cellSize;
            bool vis = zPos >= mapMinZ && zPos <= mapMaxZ;
            GameObject line = _hLines[z];
            line.SetActive(vis);
            if (vis)
            {
                // Обрезаем линию по границам карты
                var lr = line.GetComponent<LineRenderer>();
                lr.SetPosition(0, new Vector3(mapMinX, 0, zPos));
                lr.SetPosition(1, new Vector3(mapMaxX, 0, zPos));
            }
        }

        for (int x = 0; x <= gridWidth; x++)
        {
            float xPos = -halfW + x * cellSize;
            bool vis = xPos >= mapMinX && xPos <= mapMaxX;
            GameObject line = _vLines[x];
            line.SetActive(vis);
            if (vis)
            {
                var lr = line.GetComponent<LineRenderer>();
                lr.SetPosition(0, new Vector3(xPos, 0, mapMinZ));
                lr.SetPosition(1, new Vector3(xPos, 0, mapMaxZ));
            }
        }

        // Лог раз в секунду при изменениях
        if (Time.time - _lastLogTime > 1f)
        {
            _lastLogTime = Time.time;
        }
    }

    // ═══ Стены (невидимые коллайдеры по границам карты) ═══

    void UpdateWalls(Bounds b)
    {
        if (!createWalls)
        {
            DestroyWalls();
            return;
        }

        if (b.size.x <= 0f || b.size.z <= 0f)
        {
            DestroyWalls();
            return;
        }

        Vector3 center = new Vector3(b.center.x, 0f, b.center.z);
        Vector3 size = new Vector3(b.size.x, wallHeight, b.size.z);

        if (_wallsParent == null)
        {
            _wallsParent = new GameObject("Walls");
            _wallsParent.transform.SetParent(transform);
            BuildWallColliders(size);
            _cachedWallSize = size;
        }

        _wallsParent.transform.position = center;
        _wallsParent.transform.rotation = _mapRotation;

        if (!Approximately(size, _cachedWallSize))
        {
            ResizeWallColliders(size);
            _cachedWallSize = size;
        }
    }

    void BuildWallColliders(Vector3 size)
    {
        ApplyWallGeometry(size);
    }

    void ResizeWallColliders(Vector3 size)
    {
        ApplyWallGeometry(size);
    }

    void ApplyWallGeometry(Vector3 size)
    {
        float halfW = size.x / 2f;
        float halfH = size.z / 2f;
        float thickness = Mathf.Max(wallThickness, 2f);
        float hw = halfW + thickness;
        float hh = halfH + thickness;
        float hy = wallHeight / 2f;
        float lenX = size.x + thickness * 2f;
        float lenZ = size.z + thickness * 2f;

        EnsureWallObj(0, "Wall_N", new Vector3(0f, hy, hh), new Vector3(lenX, wallHeight, thickness));
        EnsureWallObj(1, "Wall_S", new Vector3(0f, hy, -hh), new Vector3(lenX, wallHeight, thickness));
        EnsureWallObj(2, "Wall_E", new Vector3(hw, hy, 0f), new Vector3(thickness, wallHeight, lenZ));
        EnsureWallObj(3, "Wall_W", new Vector3(-hw, hy, 0f), new Vector3(thickness, wallHeight, lenZ));
    }

    void EnsureWallObj(int index, string name, Vector3 localPos, Vector3 colliderSize)
    {
        GameObject wall = _wallObjs[index];
        if (wall == null)
        {
            wall = new GameObject(name);
            wall.transform.SetParent(_wallsParent.transform, worldPositionStays: false);
            wall.layer = LayerMask.NameToLayer("Ignore Raycast");
            var bc = wall.AddComponent<BoxCollider>();
            if (wallPhysics != null) bc.material = wallPhysics;
            _wallObjs[index] = wall;
        }

        wall.name = name;
        wall.layer = LayerMask.NameToLayer("Ignore Raycast");
        wall.transform.localPosition = localPos;
        wall.GetComponent<BoxCollider>().size = colliderSize;
    }

    static bool Approximately(Vector3 a, Vector3 b)
    {
        return Mathf.Abs(a.x - b.x) < 0.01f
            && Mathf.Abs(a.y - b.y) < 0.01f
            && Mathf.Abs(a.z - b.z) < 0.01f;
    }

    void DestroyWalls()
    {
        if (_wallsParent != null)
        {
            if (Application.isPlaying) Destroy(_wallsParent);
            else DestroyImmediate(_wallsParent);
            _wallsParent = null;
        }

        for (int i = 0; i < _wallObjs.Length; i++)
            _wallObjs[i] = null;

        _cachedWallSize = Vector3.zero;
    }

    // ═══ Публичные методы ═══

    public Vector3 SnapToGrid(Vector3 position)
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        Vector3 local = WorldToGridLocal(position);
        float x = Mathf.Round(local.x / cellSize - 0.5f) * cellSize + cellSize / 2f;
        float z = Mathf.Round(local.z / cellSize - 0.5f) * cellSize + cellSize / 2f;
        x = Mathf.Clamp(x, -halfW + cellSize / 2f, halfW - cellSize / 2f);
        z = Mathf.Clamp(z, -halfH + cellSize / 2f, halfH - cellSize / 2f);
        return GridLocalToWorld(new Vector3(x, 0, z), position.y);
    }

    public Vector2Int GetGridPosition(Vector3 worldPosition)
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        Vector3 local = WorldToGridLocal(worldPosition);
        if (local.x < -halfW || local.x > halfW ||
            local.z < -halfH || local.z > halfH)
            return new Vector2Int(-1, -1);
        int col = Mathf.Clamp(Mathf.FloorToInt((local.x + halfW) / cellSize), 0, gridWidth - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt((local.z + halfH) / cellSize), 0, gridHeight - 1);
        return new Vector2Int(col, row);
    }

    public Vector3 GetCellCenter(int col, int row, float y = 0f)
    {
        if (col < 0 || col >= gridWidth || row < 0 || row >= gridHeight) return Vector3.zero;
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        return GridLocalToWorld(new Vector3(-halfW + (col + 0.5f) * cellSize, 0,
            -halfH + (row + 0.5f) * cellSize), y);
    }

    private Vector3 WorldToGridLocal(Vector3 world) =>
        Quaternion.Inverse(_mapRotation) * (world - _mapBounds.center);

    public bool IsPointOnMap(Vector3 world)
    {
        Vector3 local = WorldToGridLocal(world);
        return Mathf.Abs(local.x) <= _mapBounds.size.x * 0.5f &&
            Mathf.Abs(local.z) <= _mapBounds.size.z * 0.5f;
    }

    private Vector3 GridLocalToWorld(Vector3 local, float worldY)
    {
        Vector3 world = _mapBounds.center + _mapRotation * local;
        world.y = worldY;
        return world;
    }

    public void GetCellRange(Vector3 worldMin, Vector3 worldMax, out Vector2Int min, out Vector2Int max)
    {
        float minX = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        for (int x = 0; x < 2; x++)
        for (int z = 0; z < 2; z++)
        {
            Vector3 local = WorldToGridLocal(new Vector3(x == 0 ? worldMin.x : worldMax.x,
                0, z == 0 ? worldMin.z : worldMax.z));
            minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
            minZ = Mathf.Min(minZ, local.z); maxZ = Mathf.Max(maxZ, local.z);
        }
        float halfW = gridWidth * cellSize * 0.5f;
        float halfH = gridHeight * cellSize * 0.5f;
        if (maxX < -halfW || minX > halfW || maxZ < -halfH || minZ > halfH)
        {
            min = new Vector2Int(0, 0);
            max = new Vector2Int(-1, -1);
            return;
        }
        min = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((minX + halfW) / cellSize), 0, gridWidth - 1),
            Mathf.Clamp(Mathf.FloorToInt((minZ + halfH) / cellSize), 0, gridHeight - 1));
        max = new Vector2Int(Mathf.Clamp(Mathf.FloorToInt((maxX + halfW) / cellSize), 0, gridWidth - 1),
            Mathf.Clamp(Mathf.FloorToInt((maxZ + halfH) / cellSize), 0, gridHeight - 1));
    }

    public enum SphereSnapKind { CellCenter, Intersection }

    /// <summary>
    /// Snap sphere origin/radius handle to cell center or grid intersection.
    /// Uses tolerance zones so the user does not need pixel-perfect clicks.
    /// </summary>
    public Vector3 SnapSpherePoint(Vector3 worldPos, float y, out SphereSnapKind kind, float toleranceFraction = 0.4f)
    {
        kind = SphereSnapKind.CellCenter;
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        float tolerance = cellSize * toleranceFraction;

        Vector2Int cell = GetGridPosition(worldPos);
        Vector3 center = cell.x >= 0 ? GetCellCenter(cell.x, cell.y, y) : worldPos;

        Vector3 local = WorldToGridLocal(worldPos);
        int ix = Mathf.Clamp(Mathf.RoundToInt((local.x + halfW) / cellSize), 0, gridWidth);
        int iz = Mathf.Clamp(Mathf.RoundToInt((local.z + halfH) / cellSize), 0, gridHeight);
        Vector3 intersection = GridLocalToWorld(new Vector3(-halfW + ix * cellSize, 0,
            -halfH + iz * cellSize), y);

        float distCenter = HorizontalDistance(worldPos, center);
        float distIntersection = HorizontalDistance(worldPos, intersection);

        bool nearCenter = distCenter <= tolerance;
        bool nearIntersection = distIntersection <= tolerance;

        if (nearCenter && nearIntersection)
        {
            if (distCenter <= distIntersection)
            {
                kind = SphereSnapKind.CellCenter;
                return center;
            }

            kind = SphereSnapKind.Intersection;
            return intersection;
        }

        if (nearCenter)
        {
            kind = SphereSnapKind.CellCenter;
            return center;
        }

        if (nearIntersection)
        {
            kind = SphereSnapKind.Intersection;
            return intersection;
        }

        // Fallback: snap to whichever grid anchor is closer.
        if (distCenter <= distIntersection)
        {
            kind = SphereSnapKind.CellCenter;
            return center;
        }

        kind = SphereSnapKind.Intersection;
        return intersection;
    }

    public static bool IsIntersectionPosition(Vector3 position, GridManager gm)
    {
        if (gm == null) return false;

        float halfW = gm.gridWidth * gm.cellSize / 2f;
        float halfH = gm.gridHeight * gm.cellSize / 2f;
        Vector3 local = gm.WorldToGridLocal(position);
        float relX = (local.x + halfW) / gm.cellSize;
        float relZ = (local.z + halfH) / gm.cellSize;
        const float eps = 0.001f;

        return Mathf.Abs(relX - Mathf.Round(relX)) < eps
            && Mathf.Abs(relZ - Mathf.Round(relZ)) < eps;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    // ═══ Занятость клеток ═══

    public bool IsCellOccupied(Vector2Int cell) => _occupiedCells.Contains(cell);

    public bool TryOccupyCell(Vector2Int cell)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= gridWidth || cell.y >= gridHeight)
            return false;
        return _occupiedCells.Add(cell);
    }

    public void ReleaseCell(Vector2Int cell) => _occupiedCells.Remove(cell);

    public void ClearOccupiedCells() => _occupiedCells.Clear();

    /// <summary>Ищет ближайшую свободную клетку по спирали (радиус до maxRadius).</summary>
    public Vector2Int FindNearestFreeCell(Vector2Int desired, int maxRadius = 3)
    {
        if (!IsCellOccupied(desired)) return desired;

        // Спиральный поиск
        for (int r = 1; r <= maxRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;
                    Vector2Int candidate = new(desired.x + dx, desired.y + dy);
                    if (candidate.x >= 0 && candidate.x < gridWidth &&
                        candidate.y >= 0 && candidate.y < gridHeight &&
                        !IsCellOccupied(candidate))
                        return candidate;
                }
            }
        }
        return desired; // нет свободных — остаёмся на месте
    }
}
