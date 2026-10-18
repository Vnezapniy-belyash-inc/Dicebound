using System;
using NUnit.Framework;

public class DiceboundInitiativeProjectionTests
{
    private static object Project(string[] entries, int active, Func<string, bool> visible)
    {
        var type = Type.GetType("InitiativeProjection, Assembly-CSharp", true);
        return type.GetMethod("Project").MakeGenericMethod(typeof(string)).Invoke(null, new object[] { entries, active, visible });
    }
    private static T Read<T>(object result, string field) => (T)result.GetType().GetField(field).GetValue(result);
    [Test]
    public void HiddenActorIsExcludedAndVisibleTurnIndexIsRemapped()
    {
        string[] source = { "Герой", "Секретный противник", "Союзник" };
        var view = Project(source, 2, name => name != "Секретный противник");
        Assert.That(Read<string[]>(view, "Entries"), Is.EqualTo(new[] { "Герой", "Союзник" }));
        Assert.That(Read<int>(view, "ActiveIndex"), Is.EqualTo(1));
        Assert.That(source[1], Is.EqualTo("Секретный противник"));
    }
    [Test]
    public void HiddenTurnDoesNotFalselySelectFirstVisibleActor()
    {
        var view = Project(new[] { "Герой", "Скрытый" }, 1, name => name == "Герой");
        Assert.That(Read<string[]>(view, "Entries"), Is.EqualTo(new[] { "Герой" }));
        Assert.That(Read<int>(view, "ActiveIndex"), Is.EqualTo(-1));
    }
    [Test]
    public void DifferentViewersReceiveTheirOwnAllowedOrder()
    {
        string[] source = { "А", "Б", "В" };
        Assert.That(Read<string[]>(Project(source, 1, name => name != "В"), "Entries"), Is.EqualTo(new[] { "А", "Б" }));
        Assert.That(Read<string[]>(Project(source, 1, name => name != "А"), "Entries"), Is.EqualTo(new[] { "Б", "В" }));
        Assert.That(Read<string[]>(Project(source, 1, _ => false), "Entries"), Is.Empty);
        Assert.That(Read<int>(Project(source, 1, _ => false), "ActiveIndex"), Is.EqualTo(-1));
    }
}
