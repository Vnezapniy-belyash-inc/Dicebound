using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Plans the entire group before any campaign data is changed.</summary>
public static class SceneArrivalPlacement
{
    public static Vector2Int[] Plan(SceneDefinition scene, int count, Vector2Int? arrival)
    {
        if (scene == null || scene.gridWidth < 1 || scene.gridHeight < 1 || scene.gridWidth > 256 || scene.gridHeight > 256 || count < 0)
            throw new ArgumentException("Некорректные параметры сцены или группы.");
        if (arrival.HasValue && (arrival.Value.x < 0 || arrival.Value.y < 0 || arrival.Value.x >= scene.gridWidth || arrival.Value.y >= scene.gridHeight))
            throw new ArgumentException("Точка прибытия находится за пределами целевой сцены.");
        var occupied = new HashSet<Vector2Int>();
        foreach (var token in scene.tokens ?? Array.Empty<SceneToken>())
            if (token != null) occupied.Add(new Vector2Int(Mathf.FloorToInt(token.position.x), Mathf.FloorToInt(token.position.z)));
        foreach (var obstacle in scene.geometry?.obstacles ?? Array.Empty<SceneObstacle>())
            if (obstacle != null) occupied.Add(new Vector2Int(obstacle.x, obstacle.y));
        var candidates = new List<Vector2Int>();
        int cells = scene.gridWidth * scene.gridHeight;
        for (int i = 0; i < cells; i++)
        {
            var cell = arrival.HasValue ? new Vector2Int(i % scene.gridWidth, i / scene.gridWidth)
                : new Vector2Int((scene.gridWidth / 2 + i % scene.gridWidth) % scene.gridWidth,
                    (scene.gridHeight / 2 + i / scene.gridWidth) % scene.gridHeight);
            if (!occupied.Contains(cell)) candidates.Add(cell);
        }
        if (arrival.HasValue)
        {
            var origin = arrival.Value;
            candidates.Sort((a, b) =>
            {
                int distance = (a - origin).sqrMagnitude.CompareTo((b - origin).sqrMagnitude);
                return distance != 0 ? distance : a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x);
            });
        }
        if (candidates.Count < count) throw new InvalidOperationException("На карте назначения не осталось свободных клеток для всей группы.");
        return candidates.GetRange(0, count).ToArray();
    }
}
