using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class SceneEdge
{
    public string id;
    public int x, y;
    public bool vertical;
    public bool door, open;
}
[Serializable] public sealed class SceneObstacle
{
    public string id;
    public int x, y;
    public bool round;
    public float diameter = 0.5f;
}
[Serializable] public sealed class SceneGeometry
{
    public SceneEdge[] edges = Array.Empty<SceneEdge>();
    public SceneObstacle[] obstacles = Array.Empty<SceneObstacle>();
    public bool revealPaused = true;
}
[Serializable] public sealed class SceneToken
{
    public string id, name, nameBase, portrait;
    public Vector3 position, scale;
    public bool hidden;
    public bool hero, everyoneCanMove, unassigned;
    public string ownerNickname;
    public int visionFeet;
}
[Serializable] public sealed class SavedFog
{
    public bool enabled = true;
    public int width, height;
    public string explored = "", revealed = "", hidden = "";
    public static bool[] Decode(string value, int count)
    {
        if (string.IsNullOrEmpty(value)) return new bool[count];
        int bytes = (count + 7) / 8;
        if (value.Length != ((bytes + 2) / 3) * 4) throw new FormatException("Некорректный размер истории тумана.");
        return FogVisibility.Unpack(Convert.FromBase64String(value), count);
    }
}
[Serializable] public sealed class SceneDefinition
{
    public int version = 1;
    public string title = "Сцена";
    public int gridWidth, gridHeight;
    public float cellSize;
    public Vector3 gridPosition, gridRotation, mapPosition, mapRotation;
    public float mapScale = 1;
    public string mapImage;
    public SceneGeometry geometry = new();
    public SceneToken[] tokens = Array.Empty<SceneToken>();
    public SavedFog fog = new();
    public bool includesPlayers;
}

/// <summary>Validation completes before replacing the current table. No file or network IDs are trusted.</summary>
public static class SceneValidation
{
    public const int MaxFileBytes = 96 * 1024 * 1024;
    public const int MaxEdges = 3000;
    public const int MaxObstacles = 512;
    public const int MaxTokens = 256;
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    private static void Require(bool valid, string message) { if (!valid) throw new FormatException(message); }
    public static void Geometry(SceneGeometry geometry, int width, int height)
    {
        Require(geometry != null && geometry.edges != null && geometry.obstacles != null, "Нет данных разметки.");
        Require(geometry.edges.Length <= MaxEdges && geometry.obstacles.Length <= MaxObstacles, "Слишком много объектов разметки.");
        var ids = new HashSet<string>();
        var edges = new HashSet<(int, int, bool)>();
        foreach (var edge in geometry.edges)
        {
            Require(edge != null && !string.IsNullOrWhiteSpace(edge.id) && edge.id.Length <= 64 && ids.Add(edge.id), "Некорректный ID стены.");
            Require(edge.x >= 0 && edge.y >= 0 && edge.x <= (edge.vertical ? width : width - 1)
                && edge.y <= (edge.vertical ? height - 1 : height) && edges.Add((edge.x, edge.y, edge.vertical)), "Некорректное или повторное ребро.");
            Require(edge.door || !edge.open, "Открытой может быть только дверь.");
        }
        var cells = new HashSet<(int, int)>();
        foreach (var obstacle in geometry.obstacles)
        {
            Require(obstacle != null && !string.IsNullOrWhiteSpace(obstacle.id) && obstacle.id.Length <= 64 && ids.Add(obstacle.id), "Некорректный ID препятствия.");
            Require(obstacle.x >= 0 && obstacle.x < width && obstacle.y >= 0 && obstacle.y < height
                && cells.Add((obstacle.x, obstacle.y)), "Некорректная или повторная клетка препятствия.");
            Require(Finite(obstacle.diameter) && obstacle.diameter >= 0.1f && obstacle.diameter <= 1, "Диаметр колонны должен быть 0.1–1 клетки.");
        }
    }
    public static void Validate(SceneDefinition scene)
    {
        Require(scene != null && scene.version == 1, "Неподдерживаемая версия сцены.");
        Require(!string.IsNullOrEmpty(scene.mapImage), "Сначала загрузите изображение карты.");
        Require(scene.gridWidth > 0 && scene.gridWidth <= 256 && scene.gridHeight > 0 && scene.gridHeight <= 256,
            "Размер сетки должен быть 1–256 клеток.");
        Require(Finite(scene.cellSize) && scene.cellSize >= 0.1f && scene.cellSize <= 10, "Некорректный размер клетки.");
        Require(Finite(scene.gridPosition) && Finite(scene.gridRotation) && Finite(scene.mapPosition) && Finite(scene.mapRotation)
            && scene.gridPosition.sqrMagnitude < 100000000 && scene.mapPosition.sqrMagnitude < 100000000,
            "Некорректное преобразование карты.");
        Require(Finite(scene.mapScale) && scene.mapScale >= 0.1f && scene.mapScale <= 5, "Некорректный масштаб карты.");
        Geometry(scene.geometry, scene.gridWidth, scene.gridHeight);
        if (scene.fog != null)
        {
            bool history = !string.IsNullOrEmpty(scene.fog.explored) || !string.IsNullOrEmpty(scene.fog.revealed) || !string.IsNullOrEmpty(scene.fog.hidden);
            if (history) Require(scene.fog.width == scene.gridWidth && scene.fog.height == scene.gridHeight, "История не соответствует размеру сетки.");
            int count = scene.gridWidth * scene.gridHeight * 16;
            SavedFog.Decode(scene.fog.explored, count); SavedFog.Decode(scene.fog.revealed, count); SavedFog.Decode(scene.fog.hidden, count);
        }
        Require(scene.tokens != null && scene.tokens.Length <= MaxTokens, "Слишком много токенов.");
        var ids = new HashSet<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in scene.tokens)
        {
            Require(token != null && !string.IsNullOrWhiteSpace(token.id) && token.id.Length <= 64 && ids.Add(token.id), "Некорректный ID токена.");
            Require(!string.IsNullOrWhiteSpace(token.name) && System.Text.Encoding.UTF8.GetByteCount(token.name) <= 125 && names.Add(token.name), "Повторное или слишком длинное имя токена.");
            Require(Finite(token.position) && token.position.x >= 0 && token.position.x < scene.gridWidth
                && token.position.z >= 0 && token.position.z < scene.gridHeight && Mathf.Abs(token.position.y) <= 100,
                "Токен за пределами сетки.");
            Require(Finite(token.scale) && token.scale.x > 0 && token.scale.x <= 10 && token.scale.y > 0
                && token.scale.y <= 10 && token.scale.z > 0 && token.scale.z <= 10, "Некорректный размер токена.");
            Require(token.visionFeet >= 0 && token.visionFeet <= TokenController.MaxVisionFeet, "Некорректное зрение токена.");
            Require(token.ownerNickname == null || token.ownerNickname.Length <= 64, "Слишком длинное имя владельца.");
        }
    }
}

/// <summary>One edge has exactly one wall or door; one cell has at most one solid obstacle.</summary>
public sealed class SceneGeometryModel
{
    private readonly Dictionary<(int, int, bool), SceneEdge> _edges = new();
    private readonly Dictionary<(int, int), SceneObstacle> _obstacles = new();
    public bool RevealPaused { get; set; } = true;
    public bool SetEdge(int x, int y, bool vertical, bool door, bool erase, bool toggle = false)
    {
        var key = (x, y, vertical);
        if (erase) return _edges.Remove(key);
        if (_edges.TryGetValue(key, out var edge))
        {
            if (toggle) { if (!edge.door) return false; edge.open = !edge.open; return true; }
            if (edge.door == door) return false;
            edge.door = door; edge.open = false; return true;
        }
        if (toggle || _edges.Count >= SceneValidation.MaxEdges) return false;
        _edges[key] = new SceneEdge { id = Guid.NewGuid().ToString("N"), x = x, y = y, vertical = vertical, door = door };
        return true;
    }
    public bool SetObstacle(int x, int y, bool round, float diameter, bool erase)
    {
        var key = (x, y);
        if (erase) return _obstacles.Remove(key);
        if (_obstacles.TryGetValue(key, out var old) && old.round == round && old.diameter == diameter) return false;
        if (!_obstacles.ContainsKey(key) && _obstacles.Count >= SceneValidation.MaxObstacles) return false;
        _obstacles[key] = new SceneObstacle { id = old?.id ?? Guid.NewGuid().ToString("N"), x = x, y = y, round = round, diameter = diameter };
        return true;
    }
    public SceneGeometry Snapshot() => new SceneGeometry { edges = new List<SceneEdge>(_edges.Values).ToArray(),
        obstacles = new List<SceneObstacle>(_obstacles.Values).ToArray(), revealPaused = RevealPaused };
    public void Replace(SceneGeometry geometry)
    {
        _edges.Clear(); _obstacles.Clear();
        foreach (var edge in geometry.edges) _edges.Add((edge.x, edge.y, edge.vertical), edge);
        foreach (var obstacle in geometry.obstacles) _obstacles.Add((obstacle.x, obstacle.y), obstacle);
        RevealPaused = geometry.revealPaused;
    }
}
