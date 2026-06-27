using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Тестовый скрипт — управление дайсами с клавиатуры:
///   Space   — спавнит все типы дайсов
///   R       — бросает все
///   C       — очищает все
///   1-7     — спавнит конкретный тип
/// На время теста. Потом можно удалить.
/// </summary>
public class DiceManagerTester : MonoBehaviour
{
    void Update()
    {
        if (DiceManager.Instance == null) return;
        Keyboard k = Keyboard.current;
        if (k == null) return;

        // Спавн всех типов
        if (k.spaceKey.wasPressedThisFrame)
            SpawnAll();

        // Бросок всех
        if (k.rKey.wasPressedThisFrame)
            DiceManager.Instance.RollAll();

        // Очистка
        if (k.cKey.wasPressedThisFrame)
            DiceManager.Instance.ClearAll();

        // Спавн конкретного
        if (k.digit1Key.wasPressedThisFrame) SpawnOne(DieType.d4);
        if (k.digit2Key.wasPressedThisFrame) SpawnOne(DieType.d6);
        if (k.digit3Key.wasPressedThisFrame) SpawnOne(DieType.d8);
        if (k.digit4Key.wasPressedThisFrame) SpawnOne(DieType.d10);
        if (k.digit5Key.wasPressedThisFrame) SpawnOne(DieType.d100);
        if (k.digit6Key.wasPressedThisFrame) SpawnOne(DieType.d12);
        if (k.digit7Key.wasPressedThisFrame) SpawnOne(DieType.d20);
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

    void SpawnOne(DieType type)
    {
        Dice d = DiceManager.Instance.SpawnDieAt(type, 0f, 0f);
        d.Roll(); // сразу кидаем
    }
}
