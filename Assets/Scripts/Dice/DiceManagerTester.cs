using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Тестовый скрипт — управление дайсами с клавиатуры:
///   Space — спавнит все типы дайсов
///   R     — бросает все
///   C     — очищает все
/// </summary>
public class DiceManagerTester : MonoBehaviour
{
    void Update()
    {
        if (DiceManager.Instance == null) return;
        Keyboard k = Keyboard.current;
        if (k == null) return;

        if (k.spaceKey.wasPressedThisFrame)
            SpawnAll();

        if (k.rKey.wasPressedThisFrame)
            DiceManager.Instance.RollAll();

        if (k.cKey.wasPressedThisFrame)
            DiceManager.Instance.ClearAll();
    }

    void SpawnAll()
    {
        float x = -3f;
        foreach (DieType t in System.Enum.GetValues(typeof(DieType)))
        {
            DiceManager.Instance.SpawnDieAt(t, x, 0f);
            x += 1f;
        }
    }
}
