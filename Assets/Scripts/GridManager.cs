using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Менеджер игрового поля: визуальная сетка, данные о размерах,
/// привязка позиций к клеткам.
/// </summary>
public class GridManager : MonoBehaviour
{
    [Header("Параметры сетки")]
    [Tooltip("Количество клеток по X")]
    public int gridWidth = 20;

    [Tooltip("Количество клеток по Z")]
    public int gridHeight = 20;

    [Tooltip("Размер одной клетки в юнитах")]
    public float cellSize = 1f;

    [Header("Визуал")]
    [Tooltip("Цвет линий сетки")]
    public Color gridColor = new Color(0.25f, 0.28f, 0.35f, 0.6f);

    [Tooltip("Толщина линий")]
    [Range(0.01f, 0.2f)]
    public float lineWidth = 0.04f;

    [Tooltip("Отступ сетки над плоскостью")]
    public float yOffset = 0.005f;

    [Tooltip("Цвет центральных линий")]
    public Color centerLineColor = new Color(0.4f, 0.5f, 0.7f, 0.7f);

    [Tooltip("Толщина центральных линий")]
    [Range(0.02f, 0.3f)]
    public float centerLineWidth = 0.06f;

    [Header("Стены")]
    [Tooltip("Создать невидимые стены-бортики по периметру")]
    public bool createWalls = true;

    [Tooltip("Высота стен")]
    public float wallHeight = 83f;

    [Tooltip("Толщина стен")]
    public float wallThickness = 0.3f;

    [Tooltip("PhysicMaterial для стен (упругость/трение)")]
    public PhysicsMaterial wallPhysics;

    [Header("Авторазмер")]
    [Tooltip("Подгонять размер геймборда под пропорции загруженной текстуры")]
    public bool autoResizeToTexture = true;

    [Tooltip("Сколько пикселей текстуры = 1 юнит мира")]
    public float pixelsPerUnit = 100f;

    // ─── Публичные свойства ───
    public int Width => gridWidth;
    public int Height => gridHeight;
    public float CellSize => cellSize;

    // Границы поля в мировых координатах
    public float MinX => -gridWidth * cellSize / 2f;
    public float MaxX =>  gridWidth * cellSize / 2f;
    public float MinZ => -gridHeight * cellSize / 2f;
    public float MaxZ => gridHeight * cellSize / 2f;

    private GameObject _gridLinesParent;
    private GameObject _wallsParent;
    private Texture2D _boardTexture;
    private int _initialGridWidth;
    private int _initialGridHeight;

    // ══════════════════════════════════════════════
    //  Инициализация
    // ══════════════════════════════════════════════

    void Start()
    {
        _initialGridWidth = gridWidth;
        _initialGridHeight = gridHeight;
        GenerateGridVisual();
        if (createWalls) GenerateWalls();
    }

    /// <summary>Перегенерировать сетку (можно вызвать из Editor).</summary>
    public void GenerateGridVisual()
    {
        // Удаляем старую сетку
        if (_gridLinesParent != null)
        {
            if (Application.isPlaying)
                Destroy(_gridLinesParent);
            else
                DestroyImmediate(_gridLinesParent);
        }

        _gridLinesParent = new GameObject("GridLines");
        _gridLinesParent.transform.SetParent(transform);
        _gridLinesParent.transform.localPosition = new Vector3(0, yOffset, 0);

        Material lineMat = CreateGridMaterial();

        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        int centerX = gridWidth / 2;
        int centerZ = gridHeight / 2;

        // ── Горизонтальные линии (вдоль X, на каждом Z-ряду) ──
        for (int z = 0; z <= gridHeight; z++)
        {
            float zPos = -halfH + z * cellSize;
            bool isCenter = (z == centerZ);

            CreateGridLine(
                new Vector3(-halfW, 0, zPos),
                new Vector3( halfW, 0, zPos),
                isCenter ? centerLineColor : gridColor,
                isCenter ? centerLineWidth : lineWidth,
                lineMat,
                $"H_{z}"
            );
        }

        // ── Вертикальные линии (вдоль Z, на каждом X-столбце) ──
        for (int x = 0; x <= gridWidth; x++)
        {
            float xPos = -halfW + x * cellSize;
            bool isCenter = (x == centerX);

            CreateGridLine(
                new Vector3(xPos, 0, -halfH),
                new Vector3(xPos, 0,  halfH),
                isCenter ? centerLineColor : gridColor,
                isCenter ? centerLineWidth : lineWidth,
                lineMat,
                $"V_{x}"
            );
        }
    }

    void CreateGridLine(Vector3 start, Vector3 end, Color color, float width, Material mat, string name)
    {
        GameObject lineGO = new GameObject(name);
        lineGO.transform.SetParent(_gridLinesParent.transform, worldPositionStays: false);

        LineRenderer lr = lineGO.AddComponent<LineRenderer>();
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
    }

    Material CreateGridMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.color = Color.white; // цвет задаётся через LineRenderer
        return mat;
    }

    // ══════════════════════════════════════════════
    //  Стены
    // ══════════════════════════════════════════════

    /// <summary>Создаёт 4 невидимые стены-коллайдера по периметру поля.</summary>
    public void GenerateWalls()
    {
        // Удаляем старые стены
        if (_wallsParent != null)
        {
            if (Application.isPlaying)
                Destroy(_wallsParent);
            else
                DestroyImmediate(_wallsParent);
        }

        _wallsParent = new GameObject("Walls");
        _wallsParent.transform.SetParent(transform);
        _wallsParent.transform.localPosition = Vector3.zero;

        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;
        float hw = halfW + wallThickness / 2f;
        float hh = halfH + wallThickness / 2f;
        float hy = wallHeight / 2f;

        // Длина стен с учётом углов (чуть длиннее чтобы перекрыть углы)
        float wallLengthX = gridWidth * cellSize + wallThickness * 2f;
        float wallLengthZ = gridHeight * cellSize + wallThickness * 2f;

        // ── Северная стена (Z+) ──
        CreateWall("Wall_N", new Vector3(0, hy,  hh), new Vector3(wallLengthX, wallHeight, wallThickness));
        // ── Южная стена (Z-) ──
        CreateWall("Wall_S", new Vector3(0, hy, -hh), new Vector3(wallLengthX, wallHeight, wallThickness));
        // ── Восточная стена (X+) ──
        CreateWall("Wall_E", new Vector3( hw, hy, 0), new Vector3(wallThickness, wallHeight, wallLengthZ));
        // ── Западная стена (X-) ──
        CreateWall("Wall_W", new Vector3(-hw, hy, 0), new Vector3(wallThickness, wallHeight, wallLengthZ));
    }

    void CreateWall(string name, Vector3 localPos, Vector3 size)
    {
        GameObject wall = new GameObject(name);
        wall.transform.SetParent(_wallsParent.transform, worldPositionStays: false);
        wall.transform.localPosition = localPos;
        wall.layer = gameObject.layer;

        BoxCollider bc = wall.AddComponent<BoxCollider>();
        bc.size = size;
        bc.isTrigger = false;

        if (wallPhysics != null)
            bc.material = wallPhysics;
    }

    // ══════════════════════════════════════════════
    //  Публичные методы
    // ══════════════════════════════════════════════

    /// <summary>
    /// Привязывает позицию к центру ближайшей клетки.
    /// </summary>
    public Vector3 SnapToGrid(Vector3 position)
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;

        float x = Mathf.Round(position.x / cellSize) * cellSize;
        float z = Mathf.Round(position.z / cellSize) * cellSize;

        // Не даём выйти за границы поля
        x = Mathf.Clamp(x, -halfW + cellSize / 2f, halfW - cellSize / 2f);
        z = Mathf.Clamp(z, -halfH + cellSize / 2f, halfH - cellSize / 2f);

        return new Vector3(x, position.y, z);
    }

    /// <summary>
    /// Конвертирует мировые координаты в индексы клетки (столбец, ряд).
    /// Возвращает (-1, -1) если позиция вне поля.
    /// </summary>
    public Vector2Int GetGridPosition(Vector3 worldPosition)
    {
        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;

        // Проверка границ
        if (worldPosition.x < -halfW || worldPosition.x > halfW ||
            worldPosition.z < -halfH || worldPosition.z > halfH)
        {
            return new Vector2Int(-1, -1);
        }

        int col = Mathf.FloorToInt((worldPosition.x + halfW) / cellSize);
        int row = Mathf.FloorToInt((worldPosition.z + halfH) / cellSize);

        // Защита от выхода за границы
        col = Mathf.Clamp(col, 0, gridWidth - 1);
        row = Mathf.Clamp(row, 0, gridHeight - 1);

        return new Vector2Int(col, row);
    }

    /// <summary>
    /// Возвращает центр клетки по её индексам в мировых координатах.
    /// </summary>
    public Vector3 GetCellCenter(int col, int row, float y = 0f)
    {
        if (col < 0 || col >= gridWidth || row < 0 || row >= gridHeight)
            return Vector3.zero;

        float halfW = gridWidth * cellSize / 2f;
        float halfH = gridHeight * cellSize / 2f;

        float x = -halfW + (col + 0.5f) * cellSize;
        float z = -halfH + (row + 0.5f) * cellSize;

        return new Vector3(x, y, z);
    }

    // ══════════════════════════════════════════════
    //  Текстура
    // ══════════════════════════════════════════════

    /// <summary>Устанавливает текстуру на GameBoard. Автоматически убирает альфа-канал.</summary>
    public void SetBoardTexture(Texture2D texture)
    {
        if (texture == null) return;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr == null)
        {
            Debug.LogWarning("GridManager: MeshRenderer not found on GameBoard");
            return;
        }

        // Убираем прозрачность — заливаем альфу в 1
        StripAlpha(texture);

        // Сохраняем оригинал для поворотов
        if (_boardTexture != null && _boardTexture != texture)
            Destroy(_boardTexture);
        _boardTexture = texture;

        // Форсим opaque-режим материала
        mr.material.SetFloat("_Surface", 0f);
        mr.material.mainTexture = texture;

        // Авторазмер геймборда под пропорции текстуры
        if (autoResizeToTexture)
            FitBoardToTexture(texture);

        Debug.Log($"GridManager: texture applied ({texture.width}x{texture.height}), alpha stripped");
    }

    /// <summary>Поворачивает текстуру геймборда на 90°.</summary>
    public void RotateBoardTexture(bool clockwise)
    {
        if (_boardTexture == null) return;

        int w = _boardTexture.width;
        int h = _boardTexture.height;
        Texture2D rotated = new Texture2D(h, w, _boardTexture.format, false);

        Color[] src = _boardTexture.GetPixels();
        Color[] dst = new Color[src.Length];

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int si = y * w + x;
                int di;
                if (clockwise)
                    di = x * h + (h - 1 - y);   // (x, y) → (h-1-y, x)
                else
                    di = (w - 1 - x) * h + y;   // (x, y) → (y, w-1-x)
                dst[di] = src[si];
            }
        }

        rotated.SetPixels(dst);
        rotated.Apply();

        // Заменяем сохранённую и применяем
        Destroy(_boardTexture);
        _boardTexture = rotated;

        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null)
            mr.material.mainTexture = rotated;
    }

    /// <summary>Подгоняет размер геймборда, сетки и стен под пропорции текстуры.</summary>
    void FitBoardToTexture(Texture2D tex)
    {
        float aspect = (float)tex.width / tex.height;

        // Ширина фиксирована по X, высоту (Z) подгоняем под aspect ratio
        float boardX = _initialGridWidth * cellSize;
        float boardZ = boardX / aspect;

        // Округляем gridHeight до целых клеток
        gridHeight = Mathf.Max(1, Mathf.RoundToInt(boardZ / cellSize));

        // Масштабируем Plane (он 10×10 в юнитах)
        float sx = boardX / 10f;
        float sz = (gridHeight * cellSize) / 10f;
        transform.localScale = new Vector3(sx, 1f, sz);

        // Перестраиваем сетку и стены под новый размер
        GenerateGridVisual();
        if (createWalls) GenerateWalls();

        Debug.Log($"GridManager: board resized to {boardX:F1}x{gridHeight * cellSize:F1} ({gridWidth}x{gridHeight} cells)");
    }

    /// <summary>Возвращает исходный размер геймборда.</summary>
    public void RestoreBoardSize()
    {
        gridWidth = _initialGridWidth;
        gridHeight = _initialGridHeight;

        float sx = (gridWidth * cellSize) / 10f;
        float sz = (gridHeight * cellSize) / 10f;
        transform.localScale = new Vector3(sx, 1f, sz);

        GenerateGridVisual();
        if (createWalls) GenerateWalls();

        Debug.Log($"GridManager: board restored to {gridWidth}x{gridHeight}");
    }

    /// <summary>Заливает альфа-канал в 1.0 по всем пикселям.</summary>
    static void StripAlpha(Texture2D tex)
    {
        Color[] pixels = tex.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
            pixels[i].a = 1f;
        tex.SetPixels(pixels);
        tex.Apply();
    }

    // ══════════════════════════════════════════════
    //  Editor
    // ══════════════════════════════════════════════

#if UNITY_EDITOR
    void OnValidate()
    {
        // Не перестраиваем в рантайме через OnValidate
        if (!Application.isPlaying) return;

        if (_gridLinesParent != null)
            GenerateGridVisual();
    }
#endif
}
