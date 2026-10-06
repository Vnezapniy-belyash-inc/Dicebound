using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.TestTools;

public class DiceboundDragVisualTests
{
    private GameObject _root;
    private Component _preview;
    private Type _type;

    private object Call(string method, params object[] arguments) =>
        _type.GetMethod(method).Invoke(_preview, arguments);
    private T Read<T>(string property) => (T)_type.GetProperty(property).GetValue(_preview);

    [UnityTest]
    public IEnumerator ServerUpdatesCannotMoveLocalVisualAndCompletionWaitsForTeleport()
    {
        yield return new EnterPlayMode();
        _root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var label = new GameObject("FaceLabel");
        label.transform.SetParent(_root.transform, false);
        label.transform.localPosition = Vector3.up;
        var text = label.AddComponent<TextMesh>();
        text.text = "20";
        var body = _root.GetComponent<MeshRenderer>();
        var transformType = Type.GetType("NetworkDragTransform, Assembly-CSharp", true);
        var networkTransform = _root.AddComponent(transformType);
        _type = Type.GetType("NetworkDragPreview, Assembly-CSharp", true);
        _preview = _root.AddComponent(_type);

        Assert.That((bool)Call("Begin", 1), Is.True);
        Call("Move", 1, new Vector3(10, 1, 5));
        Transform visual = Read<Transform>("DisplayTransform");
        Assert.That(body.forceRenderingOff, Is.True);
        Assert.That(visual.GetComponentsInChildren<Collider>(), Is.Empty);
        Assert.That(visual.GetComponentsInChildren<Unity.Netcode.NetworkObject>(), Is.Empty);
        Assert.That(visual.GetComponentInChildren<TextMesh>().text, Is.EqualTo("20"));

        // Reproduce the original bug: a delayed server state changes the real root.
        _root.transform.position = new Vector3(-10, 0, -5);
        yield return null;
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(new Vector3(10, 1, 5)));
        Assert.That(_root.transform.position, Is.EqualTo(new Vector3(-10, 0, -5)));
        Physics.SyncTransforms();
        var pickArguments = new object[] { new Ray(new Vector3(10, 10, 5), Vector3.down), Physics.DefaultRaycastLayers, null };
        Assert.That((bool)_type.GetMethod("Raycast").Invoke(_preview, pickArguments), Is.True);
        Assert.That(((RaycastHit)pickArguments[2]).collider, Is.EqualTo(_root.GetComponent<Collider>()));

        Call("WaitForFinish", 1);
        Call("Confirm", 1); // A late begin ACK must not undo the pending finish.
        Call("Complete", 1, 25, new Vector3(11, 0, 5), Quaternion.identity, false);
        Call("Reject", 1); // A duplicate rejection must not reveal an old root after a commit ACK.
        yield return null;
        Assert.That(Read<bool>("IsActive"), Is.True, "An ACK alone cannot expose an old root position.");
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(new Vector3(11, 0, 5)));

        var state = new NetworkTransform.NetworkTransformState();
        object boxedState = state;
        typeof(NetworkTransform.NetworkTransformState).GetField("NetworkTick", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxedState, 25);
        object flags = typeof(NetworkTransform.NetworkTransformState).GetField("FlagStates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(boxedState);
        flags.GetType().GetField("IsTeleportingNextFrame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(flags, true);
        flags.GetType().GetField("HasPositionChange", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(flags, true);
        typeof(NetworkTransform.NetworkTransformState).GetField("FlagStates", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxedState, flags);
        foreach (var axis in new[] { ("PositionX", 11f), ("PositionY", 0f), ("PositionZ", 5f) })
            typeof(NetworkTransform.NetworkTransformState).GetField(axis.Item1, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(boxedState, axis.Item2);
        _root.transform.position = new Vector3(11, 0, 5);
        transformType.GetMethod("OnNetworkTransformStateUpdated", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(networkTransform, new object[] { state, boxedState });
        yield return null;
        Assert.That(Read<bool>("IsActive"), Is.False);
        Assert.That(body.forceRenderingOff, Is.False);

        // An older acknowledgement cannot close a new gesture.
        Assert.That((bool)Call("Begin", 2), Is.True);
        Call("Complete", 1, 25, Vector3.zero, Quaternion.identity, false);
        Assert.That(Read<bool>("IsDragging"), Is.True);
        Call("Move", 2, new Vector3(20, 1, 5));
        Call("WaitForFinish", 2);
        Assert.That((bool)Call("Begin", 3), Is.True, "A pending server completion must not block a new gesture.");
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(new Vector3(20, 1, 5)));
        Call("Complete", 2, 25, Vector3.zero, Quaternion.identity, false);
        Assert.That(Read<bool>("IsDragging"), Is.True);
        Call("Stop");
        Assert.That(body.forceRenderingOff, Is.False);
        UnityEngine.Object.Destroy(_root);
        yield return null;
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (_root != null) UnityEngine.Object.Destroy(_root);
        yield return null;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
