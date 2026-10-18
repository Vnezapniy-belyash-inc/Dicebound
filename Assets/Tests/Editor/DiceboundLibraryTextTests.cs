using System;
using System.Reflection;
using NUnit.Framework;

public class DiceboundLibraryTextTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Call(string type, string method, params object[] args) => Runtime(type).GetMethod(method).Invoke(null, args);

    [Test]
    public void ActionsPreserveParagraphsColonsSlashesAndUnicode()
    {
        var type = Runtime("StatBlockAction");
        var action = Activator.CreateInstance(type);
        string name = "Укус: ядовитый 🐍";
        string description = "Первая строка\nВторая: строка\r\nC:\\notes\\бой.\n  с отступом  ";
        type.GetField("name").SetValue(action, name); type.GetField("description").SetValue(action, description);
        var actions = Array.CreateInstance(type, 1); actions.SetValue(action, 0);
        string text = (string)Call("StatBlockActionText", "Format", actions);
        var restored = (Array)Call("StatBlockActionText", "Parse", text);
        Assert.That(restored.Length, Is.EqualTo(1));
        Assert.That(type.GetField("name").GetValue(restored.GetValue(0)), Is.EqualTo(name));
        Assert.That(type.GetField("description").GetValue(restored.GetValue(0)), Is.EqualTo(description));
    }
    [Test]
    public void OrdinaryActionsRemainReadableAndMalformedRowsExplainTheirNumber()
    {
        var restored = (Array)Call("StatBlockActionText", "Parse", "Укус: 1к6 урона\n\nБег: ещё 30 футов");
        Assert.That(restored.Length, Is.EqualTo(2));
        var error = Assert.Throws<TargetInvocationException>(() => Call("StatBlockActionText", "Parse", "Укус: 1к6\nнет разделителя"));
        Assert.That(error.InnerException, Is.TypeOf<FormatException>());
        Assert.That(error.InnerException.Message, Does.Contain("2"));
    }
    [Test]
    public void TagsPreserveCommasQuotesWhitespaceAndLineBreaks()
    {
        string[] tags = { "место, север", "NPC \"Клык\"", " два пробела ", "первый\nвторой", "обычный" };
        string text = (string)Call("ReferenceTagText", "Format", (object)tags);
        Assert.That((string[])Call("ReferenceTagText", "Parse", text), Is.EqualTo(tags));
        Assert.That((string[])Call("ReferenceTagText", "Parse", " север, , юг "), Is.EqualTo(new[] { "север", "юг" }));
    }
    [TestCase("\"незакрытый тег")]
    [TestCase("\"тег\"лишний текст")]
    public void MalformedTagQuotesAreRejectedWithoutGuessing(string text)
    {
        var error = Assert.Throws<TargetInvocationException>(() => Call("ReferenceTagText", "Parse", text));
        Assert.That(error.InnerException, Is.TypeOf<FormatException>());
    }
}
