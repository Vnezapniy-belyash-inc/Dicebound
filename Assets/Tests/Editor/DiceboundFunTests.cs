using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundFunTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);

    [TestCase(0)] [TestCase(1)] [TestCase(2)]
    public void WheelSelectsEachSector(int sector)
    {
        float angle = sector * 120 * Mathf.Deg2Rad;
        var direction = new Vector2(Mathf.Sin(angle), -Mathf.Cos(angle)) * 100;
        Assert.That(Runtime("FunEffects").GetMethod("SelectSector").Invoke(null, new object[] { direction, 42f, 160f }), Is.EqualTo(sector));
    }

    [TestCase(0f)] [TestCase(42f)] [TestCase(161f)]
    public void CenterAndOutsideCancelWheel(float distance)
    {
        Assert.That(Runtime("FunEffects").GetMethod("SelectSector").Invoke(null, new object[] { new Vector2(0, -distance), 42f, 160f }), Is.EqualTo(-1));
    }

    [TestCase("tomato")] [TestCase("egg")] [TestCase("tomato-splat")] [TestCase("egg-splat")]
    public void AssetsHaveRealAlphaAndReadableBoundaries(string name)
    {
        var texture = Resources.Load<Texture2D>("Fun/" + name);
        Assert.That(texture, Is.Not.Null);
        Assert.That(texture.isReadable, Is.True);
        Assert.That(texture.width, Is.LessThanOrEqualTo(512));
        var pixels = texture.GetPixels32();
        int clear = 0, opaque = 0;
        foreach (var p in pixels) { if (p.a == 0) clear++; if (p.a > 128) opaque++; }
        Assert.That(clear, Is.GreaterThan(pixels.Length / 20));
        Assert.That(opaque, Is.GreaterThan(pixels.Length / 20));
    }

    [Test]
    public void TipsRemainForTwentySecondsAndDoNotImmediatelyRepeat()
    {
        var go = new GameObject("Tip timing test");
        try
        {
            var type = Runtime("HostSceneCurtain");
            var component = go.AddComponent(type);
            type.GetMethod("BuildUi", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, null);
            var update = type.GetMethod("UpdateLoadingTip", BindingFlags.NonPublic | BindingFlags.Instance);
            var index = type.GetField("_tipIndex", BindingFlags.NonPublic | BindingFlags.Instance);
            var tips = (string[])type.GetField("LoadingTips", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            type.GetMethod("SetCurtainDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(component, new object[] { true });
            var label = go.transform.Find("HostCurtainCanvas/Overlay/LoadingTip/Text").GetComponent<UnityEngine.UI.Text>();
            Assert.That(label.text, Is.Not.Empty, "The actual visible label must be populated immediately, before the next Update.");
            type.GetField("_nextTipAt", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(component, 0f);
            Assert.That(tips.Length, Is.EqualTo(10));
            foreach (string tip in tips) Assert.That(tip, Is.EqualTo(tip.ToLowerInvariant()));
            update.Invoke(component, new object[] { 100f });
            int first = (int)index.GetValue(component);
            Assert.That(label.text, Is.EqualTo(tips[first]));
            update.Invoke(component, new object[] { 119.9f });
            Assert.That(index.GetValue(component), Is.EqualTo(first));
            update.Invoke(component, new object[] { 120f });
            Assert.That(index.GetValue(component), Is.Not.EqualTo(first));
            Assert.That(label.text, Is.EqualTo(tips[(int)index.GetValue(component)]), "Changing the index must update the displayed UI text, not only the timer.");
            Assert.That(go.transform.Find("HostCurtainCanvas/Overlay/LoadingTip/Text"), Is.Not.Null,
                "Tips must exist above the full-screen UGUI image as well as the IMGUI fallback.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [Test]
    public void VisibleNativeCurtainOwnsTheCaptionAndFallbackDoesNotReplaceIt()
    {
        var go = new GameObject("Curtain rendering path test");
        try
        {
            var type = Runtime("HostSceneCurtain");
            var flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var component = go.AddComponent(type);
            type.GetMethod("BuildUi", flags).Invoke(component, null);
            type.GetMethod("SetCurtainDown", flags).Invoke(component, new object[] { true });
            type.GetMethod("SetClientOverlayVisible", flags).Invoke(component, new object[] { true });
            Assert.That(type.GetProperty("HasVisibleClientOverlay", flags).GetValue(component), Is.True);
            var overlay = go.transform.Find("HostCurtainCanvas/Overlay");
            var label = overlay.Find("LoadingTip/Text").GetComponent<UnityEngine.UI.Text>();
            Assert.That(label.isActiveAndEnabled, Is.True);
            Assert.That(label.text, Is.Not.Empty);
            Canvas.ForceUpdateCanvases();
            Assert.That(label.cachedTextGenerator.characterCountVisible, Is.GreaterThan(0),
                "The visible caption must actually produce text geometry inside the native canvas.");
            Assert.That(overlay.Find("ExitToLobby"), Is.Not.Null, "The native rendering path must preserve the exit button.");
            go.transform.Find("HostCurtainCanvas").GetComponent<Canvas>().enabled = false;
            Assert.That(type.GetProperty("HasVisibleClientOverlay", flags).GetValue(component), Is.False,
                "Disabling the native canvas must enable the complete IMGUI fallback, including the caption.");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

}
