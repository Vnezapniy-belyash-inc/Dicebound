using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Синглтон-менеджер дайсов: спавн, массовый бросок, очистка.
/// Вешается на GameObject в сцене вручную.
/// </summary>
public class DiceManager : MonoBehaviour
{
    public static DiceManager Instance { get; private set; }

    [Header("Спавн")]
    [Tooltip("Высота спавна над полом")]
    public float spawnHeight = 1.5f;

    [Tooltip("Разброс позиции спавна")]
    public float spawnSpread = 0.3f;

    [Header("Столкновения")]
    [Tooltip("PhysicMaterial для трения/упругости")]
    public PhysicsMaterial dicePhysics;

    [Header("Отображение")]
    [Tooltip("Размер шрифта цифр")]
    public int fontSize = 48;

    // ─── список активных кубиков ───
    private readonly List<Dice> _activeDice = new();
    public IReadOnlyList<Dice> ActiveDice => _activeDice;

    /// <summary>Срабатывает когда любой кубик выдал результат.</summary>
    public event Action<Dice> OnAnyResult;

    // ══════════════════════════════════════════════
    //  Жизненный цикл
    // ══════════════════════════════════════════════

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ══════════════════════════════════════════════
    //  Спавн
    // ══════════════════════════════════════════════

    /// <summary>Создаёт кубик указанного типа в заданной позиции.</summary>
    public Dice SpawnDie(DieType type, Vector3 position)
    {
        GameObject go = new GameObject($"Die_{type}");
        go.transform.position = position + UnityEngine.Random.insideUnitSphere * spawnSpread;
        go.transform.rotation = UnityEngine.Random.rotation;

        Dice dice = go.AddComponent<Dice>();
        dice.fontSize = fontSize;
        dice.Initialize(type);
        dice.OnResultReady += OnDieResult;

        // PhysicMaterial
        if (dicePhysics != null)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) col.material = dicePhysics;
        }

        _activeDice.Add(dice);
        return dice;
    }

    /// <summary>Создаёт кубик над полом (позиция XY из аргумента, Y = spawnHeight).</summary>
    public Dice SpawnDieAt(DieType type, float x, float z)
    {
        return SpawnDie(type, new Vector3(x, spawnHeight, z));
    }

    // ══════════════════════════════════════════════
    //  Массовые операции
    // ══════════════════════════════════════════════

    /// <summary>Бросает все активные кубики.</summary>
    public void RollAll()
    {
        foreach (var d in _activeDice)
        {
            d.Roll();
        }
    }

    /// <summary>Удаляет все кубики со сцены.</summary>
    public void ClearAll()
    {
        for (int i = _activeDice.Count - 1; i >= 0; i--)
        {
            var d = _activeDice[i];
            if (d != null)
            {
                d.OnResultReady -= OnDieResult;
                Destroy(d.gameObject);
            }
        }
        _activeDice.Clear();
    }

    /// <summary>Удаляет конкретный кубик.</summary>
    public void RemoveDie(Dice dice)
    {
        if (dice == null) return;
        dice.OnResultReady -= OnDieResult;
        _activeDice.Remove(dice);
        Destroy(dice.gameObject);
    }

    // ══════════════════════════════════════════════

    void OnDieResult(Dice die)
    {
        OnAnyResult?.Invoke(die);
    }
}
