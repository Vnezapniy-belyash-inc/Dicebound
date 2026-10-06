using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded last-action undo. Restorations do not recursively create new history entries.</summary>
public static class GameMasterUndo
{
    private static readonly List<(string Label, Action Restore)> _actions = new();
    public static bool IsRestoring { get; private set; }
    public static string NextLabel => _actions.Count == 0 ? "Нет действий" : _actions[_actions.Count - 1].Label;
    public static void Record(string label, Action restore)
    {
        if (IsRestoring || Unity.Netcode.NetworkManager.Singleton?.IsHost != true) return;
        if (_actions.Count >= 20) _actions.RemoveAt(0);
        _actions.Add((label, restore));
    }
    public static void Undo()
    {
        if (Unity.Netcode.NetworkManager.Singleton?.IsHost != true || _actions.Count == 0) return;
        var action = _actions[_actions.Count - 1]; _actions.RemoveAt(_actions.Count - 1);
        IsRestoring = true;
        try { action.Restore(); DiceUI.Instance?.ShowToolNotice("Отменено: " + action.Label); }
        catch (Exception ex) { Debug.LogWarning("[Undo] " + ex.Message); DiceUI.Instance?.ShowToolNotice("Не удалось отменить: " + ex.Message); }
        finally { IsRestoring = false; }
    }
    public static void Clear() => _actions.Clear();
}
