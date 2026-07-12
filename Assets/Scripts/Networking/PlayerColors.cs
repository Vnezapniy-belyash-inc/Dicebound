using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Система цветов игроков. Привязана к нику, переживает переподключения.
/// </summary>
public static class PlayerColors
{
    private static readonly Color[] _palette =
    {
        new Color(0.9f, 0.3f, 0.3f), // красный
        new Color(0.3f, 0.6f, 0.9f), // синий
        new Color(0.3f, 0.9f, 0.4f), // зелёный
        new Color(0.9f, 0.8f, 0.2f), // жёлтый
        new Color(0.7f, 0.3f, 0.9f), // фиолетовый
        new Color(0.9f, 0.5f, 0.2f), // оранжевый
        new Color(0.2f, 0.8f, 0.9f), // циан
        new Color(0.9f, 0.4f, 0.7f), // розовый
        new Color(0.5f, 0.9f, 0.3f), // лайм
    };

    private static readonly Dictionary<string, Color> _nickColors = new();
    private static readonly Dictionary<ulong, string> _clientNicks = new();
    private static int _nextIndex;

    /// <summary>Назначает или возвращает цвет по нику.</summary>
    public static Color GetOrAssignColor(ulong clientId, string nickname)
    {
        // Уже есть цвет для этого ника?
        if (_nickColors.TryGetValue(nickname, out Color existing))
        {
            _clientNicks[clientId] = nickname;
            return existing;
        }

        // Новый цвет
        Color c = _palette[_nextIndex % _palette.Length];
        _nextIndex++;
        _nickColors[nickname] = c;
        _clientNicks[clientId] = nickname;
        return c;
    }

    /// <summary>Возвращает цвет по clientId.</summary>
    public static Color GetColor(ulong clientId)
    {
        if (_clientNicks.TryGetValue(clientId, out string nick) && _nickColors.TryGetValue(nick, out Color c))
            return c;
        return Color.gray;
    }

    /// <summary>Возвращает ник по clientId.</summary>
    public static string GetNickname(ulong clientId)
    {
        return _clientNicks.TryGetValue(clientId, out string nick) ? nick : null;
    }

    /// <summary>Сбрасывает все данные (при полном сбросе лобби).</summary>
    public static void Reset()
    {
        _nickColors.Clear();
        _clientNicks.Clear();
        _nextIndex = 0;
    }
}
