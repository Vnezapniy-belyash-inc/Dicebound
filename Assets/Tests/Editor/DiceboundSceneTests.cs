using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundSceneTests
{
    private static Type TypeOf(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object New(string name) => Activator.CreateInstance(TypeOf(name));
    private static void Set(object item, string field, object value) => item.GetType().GetField(field).SetValue(item, value);
    private static T Get<T>(object item, string field) => (T)item.GetType().GetField(field).GetValue(item);
    private static object Call(object model, string method, params object[] args) => model.GetType().GetMethod(method).Invoke(model, args);

    [Test]
    public void DoorReplacesWallOnSameEdgeAndCanBeOpenedAndErased()
    {
        var model = New("SceneGeometryModel");
        Call(model, "SetEdge", 2, 3, true, false, false, false);
        var first = (Array)Call(model, "Snapshot").GetType().GetField("edges").GetValue(Call(model, "Snapshot"));
        string id = Get<string>(first.GetValue(0), "id");
        Call(model, "SetEdge", 2, 3, true, true, false, false);
        var snapshot = Call(model, "Snapshot");
        var edges = Get<Array>(snapshot, "edges");
        Assert.That(edges.Length, Is.EqualTo(1));
        Assert.That(Get<bool>(edges.GetValue(0), "door"), Is.True);
        Assert.That(Get<string>(edges.GetValue(0), "id"), Is.EqualTo(id));
        Call(model, "SetEdge", 2, 3, true, false, false, true);
        Assert.That(Get<bool>(edges.GetValue(0), "open"), Is.True);
        Call(model, "SetEdge", 2, 3, true, false, true, false);
        Assert.That(Get<Array>(Call(model, "Snapshot"), "edges").Length, Is.Zero);
    }

    [Test]
    public void PaintingSameEdgeTwiceDoesNotDuplicateIt()
    {
        var model = New("SceneGeometryModel");
        Assert.That(Call(model, "SetEdge", 0, 0, false, false, false, false), Is.EqualTo(true));
        Assert.That(Call(model, "SetEdge", 0, 0, false, false, false, false), Is.EqualTo(false));
        Assert.That(Get<Array>(Call(model, "Snapshot"), "edges").Length, Is.EqualTo(1));
    }

    [Test]
    public void ColumnReplacesSquareInSameCellAndKeepsPartialDiameter()
    {
        var model = New("SceneGeometryModel");
        Call(model, "SetObstacle", 1, 2, false, 1f, false);
        Call(model, "SetObstacle", 1, 2, true, 0.4f, false);
        var items = Get<Array>(Call(model, "Snapshot"), "obstacles");
        Assert.That(items.Length, Is.EqualTo(1));
        Assert.That(Get<bool>(items.GetValue(0), "round"), Is.True);
        Assert.That(Get<float>(items.GetValue(0), "diameter"), Is.EqualTo(0.4f));
        Call(model, "SetObstacle", 1, 2, false, 1f, true);
        Assert.That(Get<Array>(Call(model, "Snapshot"), "obstacles").Length, Is.Zero);
    }

    private static object Scene()
    {
        var scene = New("SceneDefinition");
        Set(scene, "sceneId", "test-scene");
        Set(scene, "gridWidth", 10); Set(scene, "gridHeight", 10); Set(scene, "cellSize", 1f);
        Set(scene, "mapImage", "placeholder");
        return scene;
    }
    private static void Validate(object scene) => TypeOf("SceneValidation").GetMethod("Validate", new[] { TypeOf("SceneDefinition") }).Invoke(null, new[] { scene });
    private static void Invalid(object scene)
    {
        var error = Assert.Throws<TargetInvocationException>(() => Validate(scene));
        Assert.That(error.InnerException, Is.TypeOf<FormatException>());
    }

    [Test] public void UnsupportedVersionIsRejectedBeforeApplication()
    { var scene = Scene(); Set(scene, "version", 999); Invalid(scene); }
    [Test] public void NonFiniteGridSizeIsRejected()
    { var scene = Scene(); Set(scene, "cellSize", float.NaN); Invalid(scene); }

    [Test]
    public void HistoryDimensionsAndPackedLengthAreValidatedBeforeImport()
    {
        var scene = Scene(); var fog = Get<object>(scene, "fog");
        Set(fog, "width", 9); Set(fog, "height", 10);
        Set(fog, "explored", Convert.ToBase64String(new byte[200])); Invalid(scene);
        Set(fog, "width", 10); Validate(scene);
        Set(fog, "explored", "AA=="); Invalid(scene);
    }

    [Test]
    public void DuplicateEdgesAndOutOfBoundsEdgesAreRejected()
    {
        var model = New("SceneGeometryModel");
        Call(model, "SetEdge", 10, 10, false, false, false, false);
        var scene = Scene(); Set(scene, "geometry", Call(model, "Snapshot")); Invalid(scene);
        model = New("SceneGeometryModel"); Call(model, "SetEdge", 0, 0, false, false, false, false);
        var geometry = Call(model, "Snapshot"); var edge = Get<Array>(geometry, "edges").GetValue(0);
        var edges = Array.CreateInstance(TypeOf("SceneEdge"), 2); edges.SetValue(edge, 0); edges.SetValue(edge, 1);
        Set(geometry, "edges", edges); Set(scene, "geometry", geometry); Invalid(scene);
    }

    [Test]
    public void JsonRoundTripKeepsDoorStateColumnShapeHiddenTokenAndAssets()
    {
        var model = New("SceneGeometryModel");
        Call(model, "SetEdge", 3, 4, true, true, false, false);
        Call(model, "SetEdge", 3, 4, true, false, false, true);
        Call(model, "SetObstacle", 5, 5, true, 0.3f, false);
        var scene = Scene(); Set(scene, "geometry", Call(model, "Snapshot"));
        var token = New("SceneToken"); Set(token, "id", "stable-token"); Set(token, "name", "Гоблин 2");
        Set(token, "nameBase", "Гоблин"); Set(token, "hidden", true); Set(token, "visionFeet", 30);
        Set(token, "position", new Vector3(2.5f, 0.25f, 3.5f)); Set(token, "scale", new Vector3(0.9f, 0.15f, 0.9f));
        Set(token, "portrait", "portrait-bytes");
        var tokens = Array.CreateInstance(TypeOf("SceneToken"), 1); tokens.SetValue(token, 0); Set(scene, "tokens", tokens);
        var masterToken = New("MasterTokenData");
        Set(masterToken, "tokenId", "stable-token");
        Set(masterToken, "currentHp", 7); Set(masterToken, "maxHp", 12);
        Set(masterToken, "armorClass", 15); Set(masterToken, "hideHp", false);
        Set(masterToken, "hideConditions", false);
        Set(masterToken, "conditionIds", new[] { "prone", "poisoned" });
        var masterTokens = Array.CreateInstance(TypeOf("MasterTokenData"), 1);
        masterTokens.SetValue(masterToken, 0);
        var masterData = Get<object>(scene, "masterData"); Set(masterData, "tokens", masterTokens);
        Validate(scene);
        var restored = JsonUtility.FromJson(JsonUtility.ToJson(scene), TypeOf("SceneDefinition")); Validate(restored);
        var restoredToken = Get<Array>(restored, "tokens").GetValue(0);
        Assert.That(Get<string>(restoredToken, "id"), Is.EqualTo("stable-token"));
        Assert.That(Get<string>(restoredToken, "name"), Is.EqualTo("Гоблин 2"));
        Assert.That(Get<bool>(restoredToken, "hidden"), Is.True);
        Assert.That(Get<int>(restoredToken, "visionFeet"), Is.EqualTo(30));
        Assert.That(Get<string>(restoredToken, "portrait"), Is.EqualTo("portrait-bytes"));
        var restoredMasterData = Get<object>(restored, "masterData");
        var restoredMasterToken = Get<Array>(restoredMasterData, "tokens").GetValue(0);
        Assert.That(Get<int>(restoredMasterToken, "currentHp"), Is.EqualTo(7));
        Assert.That(Get<int>(restoredMasterToken, "maxHp"), Is.EqualTo(12));
        Assert.That(Get<int>(restoredMasterToken, "armorClass"), Is.EqualTo(15));
        Assert.That(Get<bool>(restoredMasterToken, "hideHp"), Is.False);
        Assert.That(Get<string[]>(restoredMasterToken, "conditionIds"), Is.EqualTo(new[] { "prone", "poisoned" }));
        var geometry = Get<object>(restored, "geometry");
        Assert.That(Get<bool>(Get<Array>(geometry, "edges").GetValue(0), "open"), Is.True);
        Assert.That(Get<float>(Get<Array>(geometry, "obstacles").GetValue(0), "diameter"), Is.EqualTo(0.3f));
    }
}
