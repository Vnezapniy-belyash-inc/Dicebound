using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundArrivalPlacementTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Scene(int width, int height)
    {
        var scene = Activator.CreateInstance(Runtime("SceneDefinition"));
        scene.GetType().GetField("gridWidth").SetValue(scene, width);
        scene.GetType().GetField("gridHeight").SetValue(scene, height);
        return scene;
    }
    private static Vector2Int[] Plan(object scene, int count, Vector2Int? arrival) =>
        (Vector2Int[])Runtime("SceneArrivalPlacement").GetMethod("Plan").Invoke(null, new object[] { scene, count, arrival });

    [Test]
    public void GroupUsesNearestUniqueCellsWithStableTieOrder()
    {
        var cells = Plan(Scene(3, 3), 5, new Vector2Int(1, 1));
        Assert.That(cells, Is.EqualTo(new[] { new Vector2Int(1, 1), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(2, 1), new Vector2Int(1, 2) }));
    }
    [Test]
    public void OccupiedAndObstacleCellsAreExcludedWithoutChangingTheScene()
    {
        var scene = Scene(2, 2); var sceneType = scene.GetType();
        var token = Activator.CreateInstance(Runtime("SceneToken")); token.GetType().GetField("position").SetValue(token, new Vector3(.5f, 0, .5f));
        var tokens = Array.CreateInstance(token.GetType(), 1); tokens.SetValue(token, 0); sceneType.GetField("tokens").SetValue(scene, tokens);
        var geometry = sceneType.GetField("geometry").GetValue(scene);
        var obstacle = Activator.CreateInstance(Runtime("SceneObstacle")); obstacle.GetType().GetField("x").SetValue(obstacle, 1);
        var obstacles = Array.CreateInstance(obstacle.GetType(), 1); obstacles.SetValue(obstacle, 0); geometry.GetType().GetField("obstacles").SetValue(geometry, obstacles);
        Assert.That(Plan(scene, 2, Vector2Int.zero), Is.EqualTo(new[] { new Vector2Int(0, 1), new Vector2Int(1, 1) }));
        Assert.Throws<TargetInvocationException>(() => Plan(scene, 3, Vector2Int.zero));
        Assert.That(sceneType.GetField("tokens").GetValue(scene), Is.SameAs(tokens));
        Assert.That(token.GetType().GetField("position").GetValue(token), Is.EqualTo(new Vector3(.5f, 0, .5f)));
    }
    [Test]
    public void LegacyTransitionKeepsCenterFirstPlacement()
    {
        Assert.That(Plan(Scene(3, 3), 4, null), Is.EqualTo(new[] { new Vector2Int(1, 1), new Vector2Int(2, 1), new Vector2Int(0, 1), new Vector2Int(1, 2) }));
    }
    [TestCase(-1, 0)]
    [TestCase(2, 0)]
    [TestCase(0, 2)]
    public void OutOfBoundsArrivalIsRejected(int x, int y)
    {
        var error = Assert.Throws<TargetInvocationException>(() => Plan(Scene(2, 2), 1, new Vector2Int(x, y)));
        Assert.That(error.InnerException, Is.TypeOf<ArgumentException>());
    }
}
