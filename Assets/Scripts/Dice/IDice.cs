using UnityEngine;

/// <summary>
/// Общий интерфейс для локальных (Dice) и сетевых (NetworkDice) кубиков.
/// Используется DiceDragHandler для drag-and-drop.
/// </summary>
public interface IDice
{
    DieType DieType { get; }
    int Result { get; }
    bool IsRolling { get; set; }
    bool HasResult { get; }

    /// <summary>Сброс состояния перед броском (без приложения физики).</summary>
    void StartRoll();

    /// <summary>Бросок с приложением физических сил.</summary>
    void Roll();

    /// <summary>Rigidbody кубика.</summary>
    Rigidbody Rigidbody { get; }

    /// <summary>Transform кубика.</summary>
    Transform transform { get; }

    /// <summary>GameObject кубика.</summary>
    GameObject gameObject { get; }
}
