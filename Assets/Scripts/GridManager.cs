using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Менеджер игрового поля: визуальная сетка, привязка позиций к клеткам.
/// Базовая сетка 100×100, отображаются только клетки в границах карты.
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
    public float wallHeight = 3f;
    public float wallThickness = 1f;
    public PhysicsMaterial wallPhysics;

    public int Width => gridWidth;
    public int Height => gridHeight;
    public float CellSize => cellSize;

    private GameObject _gridLinesParent;
    private GameObject _wallsParent;
    private List<GameObject> _hLines = new();
    private List<GameObject> _vLines = new();
    private Bounds _mapBounds;
    private bool _gridCreated;

    void Start()
    {
        // Форсируем тёмно-серый цвет (переопределяет сохранённый в сцене)
        gridColor = new Color(0.15f, 0.15f, 0.15f, 0.5f);
        GenerateFullGrid();
    }

    /// <summary>Обновляет границы карты — показывает только клетки внутри.</summary>
    public void SetBounds(Bounds mapBounds)
    {
        _mapBounds = mapBounds;
        if (!_gridCreated) GenerateFullGrid();
        // Сетка НЕ двигается — остаётся в (0,0,0)
        UpdateVisibleCells();
        if (createWalls) UpdateWalls(mapBounds);
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
        _gridLinesParent.transform.localPosition = new Vector3(0, yOffset, 0);

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
        float mapMinX = _mapBounds.min.x;
        float mapMaxX = _mapBounds.max.x;
        float mapMinZ = _mapBounds.min.z;
        float mapMaxZ = _mapBounds.max.z;

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

    // ═══ Стены ═══

    void UpdateWalls(Bounds b)
    {
        if (_wallsParent != null)
        {
            if (Application.isPlaying) Destroy(_wallsParent);
            else DestroyImmediate(_wallsParent);
        }

        _wallsParent = new GameObject("Walls");
        _wallsParent.transform.SetParent(transform);
        _wallsParent.transform.localPosition = b.center;

        float halfW = b.size.x / 2f;
        float halfH = b.size.z / 2f;
        float hw = halfW + wallThickness / 2f;
        float hh = halfH + wallThickness / 2f;
        float hy = wallHeight / 2f;
        float lenX = b.size.x + wallThickness * 2f;
        float lenZ = b.size.z + wallThickness * 2f;

        CreateWallObj("Wall_N", new Vector3(0, hy, hh), new Vector3(lenX, wallHeight, wallThickness));
        CreateWallObj("Wall_S", new Vector3(0, hy, -hh), new Vector3(lenX, wallHeight, wallThickness));
        CreateWallObj("Wall_E", new Vector3(hw, hy, 0), new Vector3(wallThickness, wallHeight, lenZ));
        CreateWallObj("Wall_W", new Vector3(-hw, hy, 0), new Vector3(wallThickness, wallHeight, lenZ));
    }

    void CreateWallObj(string name, Vector3 pos, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(_wallsParent.transform, worldPositionStays: false);
        wall.transform.localPosition = pos;
        wall.layer = gameObject.layer;
        BoxCollider bc = wall.AddComponent<BoxCollider>();
        bc.size = size;
        if (wallPhysics != null) bc.material = wallPhysics;
    }

    // ═══ Публичные методы ═══

    public Vector3 SnapToGrid(Vector3 position)
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        float x = Mathf.Round(position.x / cellSize - 0.5f) * cellSize + cellSize / 2f;
        float z = Mathf.Round(position.z / cellSize - 0.5f) * cellSize + cellSize / 2f;
        x = Mathf.Clamp(x, -halfW + cellSize / 2f, halfW - cellSize / 2f);
        z = Mathf.Clamp(z, -halfH + cellSize / 2f, halfH - cellSize / 2f);
        return new Vector3(x, position.y, z);
    }

    public Vector2Int GetGridPosition(Vector3 worldPosition)
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        if (worldPosition.x < -halfW || worldPosition.x > halfW ||
            worldPosition.z < -halfH || worldPosition.z > halfH)
            return new Vector2Int(-1, -1);
        int col = Mathf.Clamp(Mathf.FloorToInt((worldPosition.x + halfW) / cellSize), 0, gridWidth - 1);
        int row = Mathf.Clamp(Mathf.FloorToInt((worldPosition.z + halfH) / cellSize), 0, gridHeight - 1);
        return new Vector2Int(col, row);
    }

    public Vector3 GetCellCenter(int col, int row, float y = 0f)
    {
        if (col < 0 || col >= gridWidth || row < 0 || row >= gridHeight) return Vector3.zero;
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        return new Vector3(-halfW + (col + 0.5f) * cellSize, y, -halfH + (row + 0.5f) * cellSize);
    }
}
