using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

public class DiceboundFogTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void PreviewAndGroupApplySameManualOverrides(bool paused)
    {
        var visible = new[] { true, false, true, false };
        var revealed = new[] { false, true, true, true };
        var hidden = new[] { true, false, true, false };
        TypeOf("FogManager").GetMethod("ApplyManualOverrides", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { visible, revealed, hidden, paused });
        Assert.That(visible, Is.EqualTo(new[] { false, !paused, false, !paused }));
        Assert.That(revealed, Is.EqualTo(new[] { false, true, true, true }), "Preview must not change stored reveal history.");
        Assert.That(hidden, Is.EqualTo(new[] { true, false, true, false }));
    }

    private static Type TypeOf(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Model() => Activator.CreateInstance(TypeOf("SceneGeometryModel"));
    private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
    private static object Engine(object model) => Activator.CreateInstance(TypeOf("FogVisibility"), Call(model, "Snapshot"));
    private static bool Sight(object engine, Vector2 from, Vector2 to) => (bool)Call(engine, "HasLineOfSight", from, to);

    [Test]
    public void ClosedDoorBlocksSightAndOpenDoorPassesIt()
    {
        var model = Model(); Call(model, "SetEdge", 1, 0, true, true, false, false);
        Assert.That(Sight(Engine(model), new Vector2(0.5f, 0.5f), new Vector2(1.5f, 0.5f)), Is.False);
        Call(model, "SetEdge", 1, 0, true, false, false, true);
        Assert.That(Sight(Engine(model), new Vector2(0.5f, 0.5f), new Vector2(1.5f, 0.5f)), Is.True);
    }
    [Test]
    public void WallDoesNotLeakAtDiagonalCorner()
    {
        var model = Model(); Call(model, "SetEdge", 1, 0, true, false, false, false);
        Assert.That(Sight(Engine(model), new Vector2(0.5f, 0.5f), new Vector2(1.5f, 1.5f)), Is.False);
    }
    [Test]
    public void PartialColumnAllowsSightPastItsActualContour()
    {
        var model = Model(); Call(model, "SetObstacle", 1, 0, true, 0.4f, false);
        var engine = Engine(model);
        Assert.That(Sight(engine, new Vector2(0.1f, 0.1f), new Vector2(2.9f, 0.1f)), Is.True);
        Assert.That(Sight(engine, new Vector2(0.1f, 0.5f), new Vector2(2.9f, 0.5f)), Is.False);
    }
    [Test]
    public void SquareBlocksWholeCell()
    {
        var model = Model(); Call(model, "SetObstacle", 1, 0, false, 1f, false);
        Assert.That(Sight(Engine(model), new Vector2(0.1f, 0.1f), new Vector2(2.9f, 0.1f)), Is.False);
    }
    private static bool[] Calculate(object engine, int width, int height, params (Vector2 Position, float Radius)[] sources)
    {
        var type = TypeOf("VisionSource"); var array = Array.CreateInstance(type, sources.Length);
        for (int i = 0; i < sources.Length; i++) array.SetValue(Activator.CreateInstance(type, sources[i].Position, sources[i].Radius), i);
        return (bool[])Call(engine, "Calculate", width, height, array);
    }
    [Test]
    public void GroupSharesUnionOfSourcesAndRespectsRadius()
    {
        var visible = Calculate(Engine(Model()), 4, 1, (new Vector2(0.5f, 0.5f), 0.7f), (new Vector2(3.5f, 0.5f), 0.7f));
        Assert.That(visible[2 * 16 + 2], Is.True); Assert.That(visible[2 * 16 + 14], Is.True);
        Assert.That(visible[2 * 16 + 6], Is.False);
    }
    [Test]
    public void ZeroVisionAndSourcesInsideOpaqueObstaclesRevealNothing()
    {
        var model = Model(); Call(model, "SetObstacle", 0, 0, true, 0.5f, false);
        var visible = Calculate(Engine(model), 2, 2, (new Vector2(0.5f, 0.5f), 10), (new Vector2(1.5f, 1.5f), 0));
        Assert.That(visible, Is.All.False);
    }
    [Test]
    public void PackedHistoryRoundTripsPartialLastByte()
    {
        var values = new bool[33]; values[0] = values[8] = values[32] = true;
        var type = TypeOf("FogVisibility"); var packed = (byte[])type.GetMethod("Pack").Invoke(null, new object[] { values });
        Assert.That(packed.Length, Is.EqualTo(5));
        var restored = (bool[])type.GetMethod("Unpack").Invoke(null, new object[] { packed, values.Length });
        Assert.That(restored, Is.EqualTo(values));
        Assert.Throws<TargetInvocationException>(() => type.GetMethod("Unpack").Invoke(null, new object[] { new byte[1], values.Length }));
    }
    [Test]
    public void FogShadersAreIncludedAndHaveNoCompilerErrors()
    {
        foreach (string name in new[] { "DiceboundFog", "DiceboundFogCover", "DiceboundMarkup" })
        {
            var shader = Resources.Load<Shader>(name); Assert.That(shader, Is.Not.Null);
            foreach (var message in ShaderUtil.GetShaderMessages(shader)) Assert.That(message.severity.ToString(), Is.Not.EqualTo("Error"), message.message);
        }
    }
}
