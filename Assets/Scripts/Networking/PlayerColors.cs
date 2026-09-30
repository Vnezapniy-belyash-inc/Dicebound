using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local cache of the server-synced player registry.
/// Populated only via PlayerRegistry sync messages.
/// </summary>
public static class PlayerColors
{
    private static readonly Dictionary<ulong, string> _clientNicks = new();
    private static readonly Dictionary<ulong, Color> _clientColors = new();

    public static event Action Changed;

    public static void SetPlayer(ulong clientId, string nickname, Color color)
    {
        if (_clientNicks.TryGetValue(clientId, out string oldNickname)
            && oldNickname == nickname
            && _clientColors.TryGetValue(clientId, out Color oldColor)
            && oldColor == color)
            return;

        _clientNicks[clientId] = nickname;
        _clientColors[clientId] = color;
        Changed?.Invoke();
    }

    public static void RemoveClient(ulong clientId)
    {
        bool changed = _clientNicks.Remove(clientId);
        changed |= _clientColors.Remove(clientId);
        if (changed) Changed?.Invoke();
    }

    public static Color GetColor(ulong clientId)
    {
        return _clientColors.TryGetValue(clientId, out Color c) ? c : Color.gray;
    }

    public static string GetNickname(ulong clientId)
    {
        return _clientNicks.TryGetValue(clientId, out string nick) ? nick : null;
    }

    public static IReadOnlyDictionary<ulong, string> GetAllPlayers() => _clientNicks;

    public static void Reset()
    {
        _clientNicks.Clear();
        _clientColors.Clear();
        Changed?.Invoke();
    }
}
