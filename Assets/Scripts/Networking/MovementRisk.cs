using UnityEngine;

public enum PlayerMovementPolicy { Free, Warn, Approval, Step }

/// <summary>Server-only decision. Exploring a short distance into darkness is allowed.</summary>
public static class MovementRisk
{
    public static Vector2Int SingleStep(Vector2Int from, Vector2Int target)
    {
        int dx = target.x - from.x, dy = target.y - from.y;
        if (dx == 0 && dy == 0) return from;
        return Mathf.Abs(dx) >= Mathf.Abs(dy)
            ? from + new Vector2Int(dx > 0 ? 1 : -1, 0)
            : from + new Vector2Int(0, dy > 0 ? 1 : -1);
    }
    public static bool RequiresReview(Vector2 from, Vector2 to, bool known, bool clearPath) =>
        (to - from).sqrMagnitude > 0.01f && (!clearPath || !known && (to - from).sqrMagnitude > 4.01f);
}
