using System.Collections.Generic;

/// <summary>Server arbitration, independent of rendering and transport.</summary>
public sealed class NetworkDragLease
{
    public const double Duration = 2;
    public const ulong NoController = ulong.MaxValue;
    private readonly Dictionary<ulong, int> _lastGestures = new();
    public ulong Controller { get; private set; } = NoController;
    public int Gesture { get; private set; }
    public int LastMoveSequence { get; private set; }
    public double ExpiresAt { get; private set; }
    public bool IsHeld => Controller != NoController;

    public bool TryBegin(ulong client, int gesture, double now)
    {
        if (gesture <= 0 || (_lastGestures.TryGetValue(client, out int previous) && gesture <= previous))
            return false;
        // A rejected request must not become a successful stale request later.
        _lastGestures[client] = gesture;
        if (IsHeld) return false;
        Controller = client;
        Gesture = gesture;
        LastMoveSequence = -1;
        ExpiresAt = now + Duration;
        return true;
    }

    public bool CanControl(ulong client, int gesture, double now) =>
        IsHeld && Controller == client && Gesture == gesture && now <= ExpiresAt;

    public bool TryMove(ulong client, int gesture, int sequence, double now)
    {
        if (!CanControl(client, gesture, now) || sequence <= LastMoveSequence) return false;
        LastMoveSequence = sequence;
        ExpiresAt = now + Duration;
        return true;
    }

    public bool HasExpired(double now) => IsHeld && now > ExpiresAt;

    public void Release()
    {
        Controller = NoController;
        ExpiresAt = 0;
    }

    public void ForgetClient(ulong client) => _lastGestures.Remove(client);
}
