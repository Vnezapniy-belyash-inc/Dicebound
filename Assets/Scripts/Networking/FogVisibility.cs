using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct VisionSource
{
    public readonly Vector2 Position;
    public readonly float Radius;
    public VisionSource(Vector2 position, float radius) { Position = position; Radius = radius; }
}

/// <summary>Pure grid-space visibility. Rays traverse cells; circular columns use their real radius.</summary>
public sealed class FogVisibility
{
    public const int SamplesPerCell = 4;
    private readonly HashSet<(int, int, bool)> _walls = new();
    private readonly Dictionary<(int, int), SceneObstacle> _obstacles = new();
    public FogVisibility(SceneGeometry geometry)
    {
        foreach (var edge in geometry.edges)
            if (!edge.door || !edge.open) _walls.Add((edge.x, edge.y, edge.vertical));
        foreach (var obstacle in geometry.obstacles) _obstacles[(obstacle.x, obstacle.y)] = obstacle;
    }
    private bool Contains(SceneObstacle obstacle, Vector2 point)
    {
        if (!obstacle.round) return true;
        return (point - new Vector2(obstacle.x + 0.5f, obstacle.y + 0.5f)).sqrMagnitude <= obstacle.diameter * obstacle.diameter * 0.25f;
    }
    public bool InsideObstacle(Vector2 point) => _obstacles.TryGetValue((Mathf.FloorToInt(point.x), Mathf.FloorToInt(point.y)), out var obstacle) && Contains(obstacle, point);
    private bool BlocksCell(int x, int y, Vector2 from, Vector2 to)
    {
        if (!_obstacles.TryGetValue((x, y), out var obstacle)) return false;
        if (!obstacle.round) return true;
        Vector2 delta = to - from;
        float fraction = delta.sqrMagnitude <= 0.000001f ? 0 : Mathf.Clamp01(Vector2.Dot(new Vector2(x + 0.5f, y + 0.5f) - from, delta) / delta.sqrMagnitude);
        return Contains(obstacle, from + fraction * delta);
    }
    public bool HasLineOfSight(Vector2 from, Vector2 to)
    {
        int x = Mathf.FloorToInt(from.x), y = Mathf.FloorToInt(from.y);
        int endX = Mathf.FloorToInt(to.x), endY = Mathf.FloorToInt(to.y);
        Vector2 delta = to - from;
        int sx = Math.Sign(delta.x), sy = Math.Sign(delta.y);
        float dx = sx == 0 ? float.PositiveInfinity : 1 / Mathf.Abs(delta.x);
        float dy = sy == 0 ? float.PositiveInfinity : 1 / Mathf.Abs(delta.y);
        float tx = sx == 0 ? float.PositiveInfinity : (sx > 0 ? x + 1 - from.x : from.x - x) * dx;
        float ty = sy == 0 ? float.PositiveInfinity : (sy > 0 ? y + 1 - from.y : from.y - y) * dy;
        for (int steps = 0; steps < 1024; steps++)
        {
            if (BlocksCell(x, y, from, to)) return false;
            if (x == endX && y == endY) return true;
            if (Mathf.Abs(tx - ty) < 0.00001f)
            {
                int bx = sx > 0 ? x + 1 : x, by = sy > 0 ? y + 1 : y;
                // A wall touching the crossed corner must not leak a diagonal sight ray.
                if (_walls.Contains((bx, y, true)) || _walls.Contains((bx, y + sy, true))
                    || _walls.Contains((x, by, false)) || _walls.Contains((x + sx, by, false))) return false;
                x += sx; y += sy; tx += dx; ty += dy;
            }
            else if (tx < ty)
            {
                if (_walls.Contains((sx > 0 ? x + 1 : x, y, true))) return false;
                x += sx; tx += dx;
            }
            else
            {
                if (_walls.Contains((x, sy > 0 ? y + 1 : y, false))) return false;
                y += sy; ty += dy;
            }
        }
        return false;
    }
    public bool[] Calculate(int width, int height, IEnumerable<VisionSource> sources)
    {
        int pixelWidth = width * SamplesPerCell, pixelHeight = height * SamplesPerCell;
        var visible = new bool[pixelWidth * pixelHeight];
        foreach (var source in sources)
        {
            if (source.Radius <= 0 || InsideObstacle(source.Position)) continue;
            int minX = Mathf.Clamp(Mathf.FloorToInt((source.Position.x - source.Radius) * SamplesPerCell), 0, pixelWidth - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt((source.Position.x + source.Radius) * SamplesPerCell), 0, pixelWidth - 1);
            int minY = Mathf.Clamp(Mathf.FloorToInt((source.Position.y - source.Radius) * SamplesPerCell), 0, pixelHeight - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt((source.Position.y + source.Radius) * SamplesPerCell), 0, pixelHeight - 1);
            for (int y = minY; y <= maxY; y++) for (int x = minX; x <= maxX; x++)
            {
                int index = y * pixelWidth + x;
                if (visible[index]) continue;
                var target = new Vector2((x + 0.5f) / SamplesPerCell, (y + 0.5f) / SamplesPerCell);
                if ((target - source.Position).sqrMagnitude <= source.Radius * source.Radius && HasLineOfSight(source.Position, target)) visible[index] = true;
            }
        }
        return visible;
    }
    public static byte[] Pack(bool[] values)
    {
        var bytes = new byte[(values.Length + 7) / 8];
        for (int i = 0; i < values.Length; i++) if (values[i]) bytes[i / 8] |= (byte)(1 << (i % 8));
        return bytes;
    }
    public static bool[] Unpack(byte[] bytes, int count)
    {
        if (bytes.Length != (count + 7) / 8) throw new FormatException("Некорректная длина истории тумана.");
        var values = new bool[count];
        for (int i = 0; i < count; i++) values[i] = (bytes[i / 8] & (1 << (i % 8))) != 0;
        return values;
    }
}
