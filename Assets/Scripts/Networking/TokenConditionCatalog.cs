using System;

/// <summary>Stable IDs for the built-in token conditions.</summary>
public static class TokenConditionCatalog
{
    public static readonly string[] Ids =
    {
        "blinded", "charmed", "deafened", "exhaustion", "frightened",
        "grappled", "incapacitated", "invisible", "paralyzed", "petrified",
        "poisoned", "prone", "restrained", "stunned", "unconscious"
    };

    private static readonly string[] Names =
    {
        "Ослеплён", "Очарован", "Оглушён слухом", "Истощение", "Испуган",
        "Схвачен", "Недееспособен", "Невидим", "Парализован", "Окаменел",
        "Отравлен", "Сбит с ног", "Опутан", "Ошеломлён", "Без сознания"
    };

    public static string DisplayName(string id)
    {
        int index = Array.IndexOf(Ids, id);
        return index >= 0 ? Names[index] : id ?? string.Empty;
    }
}
