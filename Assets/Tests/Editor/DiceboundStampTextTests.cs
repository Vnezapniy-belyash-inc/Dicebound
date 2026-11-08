using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundStampTextTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static string Normalize(string text) => (string)Runtime("StampText").GetMethod("Normalize").Invoke(null, new object[] { text });

    private static string Wrap(string text, Func<string, float> measure, float width, out int lines)
    {
        var arguments = new object[] { text, measure, width, 0 };
        var wrapped = (string)Runtime("StampText").GetMethod("Wrap").Invoke(null, arguments);
        lines = (int)arguments[3];
        return wrapped;
    }

    [TestCase("")] [TestCase("   \r\n\t")] [TestCase(null)]
    public void EmptyStampIsRejected(string text) => Assert.That(Normalize(text), Is.Null);

    [Test]
    public void ServerNormalizesWhitespaceAndEnforcesLength()
    {
        Assert.That(Normalize("  Свой\n\tтекст  "), Is.EqualTo("Свой текст"));
        Assert.That(Normalize(new string('Я', 80)), Is.EqualTo(new string('Я', 80)));
        Assert.That(Normalize(new string('Я', 81)), Is.Null);
        Assert.That(Normalize("<b>текст</b>"), Is.EqualTo("<b>текст</b>"), "Markup must remain literal user text.");
    }

    [Test]
    public void StampWrapUsesMeasuredWidthAndPrefersWholeWords()
    {
        float Measure(string text)
        {
            float width = 0;
            foreach (char c in text) width += c == 'W' ? 3 : 1;
            return width;
        }

        Assert.That(Wrap("WW iiii", Measure, 8, out int lines), Is.EqualTo("WW\niiii"));
        Assert.That(lines, Is.EqualTo(2));
        Assert.That(Wrap("ii iiii", Measure, 8, out lines), Is.EqualTo("ii iiii"));
        Assert.That(lines, Is.EqualTo(1));
    }

    [Test]
    public void UnbrokenStampTextWrapsBetweenWholeTextElements()
    {
        const string text = "😀😀😀😀😀";
        string wrapped = Wrap(text, value => value.Length, 4, out int lines);
        Assert.That(wrapped, Is.EqualTo("😀😀\n😀😀\n😀"));
        Assert.That(lines, Is.EqualTo(3));
    }

    [Test]
    public void StampFrameGrowsWithTextAndStaysWide()
    {
        var effects = Runtime("FunEffects");
        var reactionType = effects.GetNestedType("Reaction", BindingFlags.NonPublic);
        var prepare = effects.GetMethod("PrepareStampText", BindingFlags.NonPublic | BindingFlags.Static);
        Vector2 MeasureFrame(string text)
        {
            var reaction = Activator.CreateInstance(reactionType, nonPublic: true);
            reactionType.GetField("Text").SetValue(reaction, text);
            prepare.Invoke(null, new[] { reaction });
            return (Vector2)reactionType.GetField("StampLayoutSize").GetValue(reaction);
        }

        var shortFrame = MeasureFrame("Да");
        var wideFrame = MeasureFrame("WWWWW");
        var wrappedFrame = MeasureFrame("Очень длинная надпись, которая должна занять несколько строк внутри рамки");
        Assert.That(wideFrame.x, Is.GreaterThan(shortFrame.x));
        Assert.That(wrappedFrame.y, Is.GreaterThan(shortFrame.y));
        Assert.That(wrappedFrame.x / wrappedFrame.y, Is.InRange(2f, 3.6f));
    }

    [Test]
    public void StampDialogBlocksGameplayAndCanCancelOrConfirm()
    {
        var go = new GameObject("Stamp dialog state test");
        try
        {
            var type = Runtime("FunEffects"); var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var component = go.AddComponent(type);
            type.GetMethod("Awake", flags).Invoke(component, null);
            var editing = type.GetField("_editingStamp", flags);
            var input = type.GetField("_stampInput", flags);
            editing.SetValue(component, true); input.SetValue(component, "  ");
            Assert.That(Runtime("GameplayInputGate").GetProperty("AllowsKeyboardHotkeys").GetValue(null), Is.False);
            Assert.That(Runtime("GameplayInputGate").GetProperty("AllowsWorldPointerInput").GetValue(null), Is.False);
            Assert.That(Runtime("GameplayInputGate").GetProperty("AllowsImGuiOverlays").GetValue(null), Is.False);
            type.GetMethod("ConfirmStampInput", flags).Invoke(component, null);
            Assert.That(editing.GetValue(component), Is.True, "Empty input must remain open.");
            input.SetValue(component, "свой текст");
            type.GetMethod("ConfirmStampInput", flags).Invoke(component, null);
            Assert.That(editing.GetValue(component), Is.False);
            editing.SetValue(component, true); input.SetValue(component, "отмена");
            type.GetMethod("CancelInput", flags).Invoke(component, null);
            Assert.That(editing.GetValue(component), Is.False);
            Assert.That(input.GetValue(component), Is.EqualTo(""));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
}
