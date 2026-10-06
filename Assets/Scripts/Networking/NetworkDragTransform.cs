using Unity.Netcode.Components;
using UnityEngine;

/// <summary>Tracks the authoritative teleport that closes a drag and resets interpolation.</summary>
public class NetworkDragTransform : NetworkTransform
{
    private readonly int[] _commitTicks = new int[16];
    private readonly Vector3[] _commitPositions = new Vector3[16];
    private int _commitCount;
    public int LastTeleportTick { get; private set; } = int.MinValue;
    public Vector3 LastTeleportPosition { get; private set; }

    protected override void OnNetworkTransformStateUpdated(
        ref NetworkTransformState oldState, ref NetworkTransformState newState)
    {
        base.OnNetworkTransformStateUpdated(ref oldState, ref newState);
        if (!newState.IsTeleportingNextFrame) return;
        LastTeleportTick = newState.GetNetworkTick();
        LastTeleportPosition = newState.GetPosition();
        int index = _commitCount++ % _commitTicks.Length;
        _commitTicks[index] = LastTeleportTick;
        _commitPositions[index] = LastTeleportPosition;
    }

    public bool HasCommit(int tick, Vector3 position)
    {
        if (LastTeleportTick > tick) return true;
        // Multiple reliable commits can arrive in the same tick. Keep the matching one
        // even if a subsequent player has already committed another position.
        for (int i = 0; i < Mathf.Min(_commitCount, _commitTicks.Length); i++)
            if (_commitTicks[i] == tick && (_commitPositions[i] - position).sqrMagnitude < 0.000001f)
                return true;
        return false;
    }
}
