using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Система цветов игроков. Каждому clientId назначается уникальный цвет.
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

    private static readonly Dictionary<ulong, Color> _playerColors = new();
    private static int _nextIndex;

    /// <summary>Назначает цвет игроку при подключении.</summary>
    public static Color AssignColor(ulong clientId)
    {
        Color c = _palette[_nextIndex % _palette.Length];
        _playerColors[clientId] = c;
        _nextIndex++;
        return c;
    }

    /// <summary>Возвращает цвет игрока (или серый если нет).</summary>
    public static Color GetColor(ulong clientId)
    {
        return _playerColors.TryGetValue(clientId, out Color c) ? c : Color.gray;
    }
}
