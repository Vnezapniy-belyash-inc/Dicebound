using System;
using System.Text;
using NUnit.Framework;

public class DiceboundTokenNameTests
{
    private object _registry;
    private Type _type;
    [SetUp]
    public void SetUp()
    {
        _type = Type.GetType("TokenNameRegistry, Assembly-CSharp", true);
        _registry = Activator.CreateInstance(_type);
    }
    private string Name(string value, params string[] existing) =>
        (string)_type.GetMethod("Allocate").Invoke(_registry, new object[] { value, existing });

    [Test]
    public void RepeatedNamesGetSequentialSuffixes()
    {
        Assert.That(Name("Гоблин"), Is.EqualTo("Гоблин"));
        Assert.That(Name("Гоблин"), Is.EqualTo("Гоблин 2"));
        Assert.That(Name("Гоблин"), Is.EqualTo("Гоблин 3"));
        Assert.That(Name("Багбир"), Is.EqualTo("Багбир"));
    }

    [Test]
    public void DeletedNamesAreNotReusedUntilNewLobby()
    {
        Name("Goblin");
        Name("Goblin");
        Assert.That(Name("Goblin"), Is.EqualTo("Goblin 3"), "The live list can be empty after deletion.");
        _type.GetMethod("Clear").Invoke(_registry, null);
        Assert.That(Name("Goblin"), Is.EqualTo("Goblin"));
    }

    [Test]
    public void ExistingLoadedNamesAndExplicitSuffixesCannotCollide()
    {
        Assert.That(Name("Goblin", "Goblin", "Goblin 2", "Goblin 3"), Is.EqualTo("Goblin 4"));
        Assert.That(Name("Goblin 4"), Is.EqualTo("Goblin 4 2"));
    }

    [Test]
    public void WhitespaceAndCaseCannotCreateDuplicateNames()
    {
        Assert.That(Name("  Goblin \n King  "), Is.EqualTo("Goblin King"));
        Assert.That(Name("goblin king"), Is.EqualTo("goblin king 2"));
        Assert.That(Name("  \t "), Is.EqualTo("Токен"));
    }

    [Test]
    public void LongUnicodeNamesFitNetworkStringWithoutSplittingSurrogates()
    {
        string name = string.Concat(System.Linq.Enumerable.Repeat("Колонна😀", 30));
        string first = Name(name);
        string second = Name(name);
        Assert.That(Encoding.UTF8.GetByteCount(first), Is.LessThanOrEqualTo(96));
        Assert.That(Encoding.UTF8.GetByteCount(second), Is.LessThanOrEqualTo(125));
        Assert.That(first.EndsWith("\uD83D"), Is.False);
        Assert.That(second, Is.EqualTo(first + " 2"));
    }
}
