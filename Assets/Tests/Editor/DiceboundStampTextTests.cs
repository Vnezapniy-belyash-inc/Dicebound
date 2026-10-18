using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundStampTextTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static string Normalize(string text) => (string)Runtime("StampText").GetMethod("Normalize").Invoke(null, new object[] { text });
    private static string Wrap(string text, float width) => (string)Runtime("StampText").GetMethod("Wrap").Invoke(null,
        new object[] { text, new Func<string, float>(s => s.Length), width });

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

    [TestCase("коротко", 20, "коротко")]
    [TestCase("длинная надпись", 10, "длинная\nнадпись")]
    [TestCase("abcdefghij", 5, "abcde\nfghij")]
    public void StampFitsOneOrTwoLines(string text, float width, string expected)
        => Assert.That(Wrap(text, width), Is.EqualTo(expected));

    [Test]
    public void LongTextNeverExceedsTwoLinesOrBreaksSurrogatePairs()
    {
        string text = "😀😀😀😀😀😀";
        string wrapped = Wrap(text, 4);
        Assert.That(wrapped.Split('\n').Length, Is.EqualTo(2));
        Assert.That(wrapped.Replace("\n", ""), Is.EqualTo(text));
        var strict = new System.Text.UTF8Encoding(false, true);
        Assert.DoesNotThrow(() => strict.GetBytes(wrapped));
        string longText = new string('Ж', 80);
        Assert.That(Wrap(longText, 10).Split('\n').Length, Is.EqualTo(2));
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
