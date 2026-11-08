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

        _root.transform.position = new Vector3(11, 0, 5);
        ReceiveTeleport(networkTransform, 25, _root.transform.position);
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

    [UnityTest]
    public IEnumerator ReleasedDieUsesLocalPhysicsUntilTheFinalPoseArrives()
    {
        yield return new EnterPlayMode();
        _root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var collider = (MeshCollider)_root.AddComponent(typeof(MeshCollider));
        collider.convex = true;
        collider.sharedMesh = _root.GetComponent<MeshFilter>().sharedMesh;
        _root.GetComponent<BoxCollider>().enabled = false;
        var body = _root.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        var networkTransform = _root.AddComponent(Type.GetType("NetworkDragTransform, Assembly-CSharp", true));
        _type = Type.GetType("NetworkDragPreview, Assembly-CSharp", true);
        _preview = _root.AddComponent(_type);

        var release = new Vector3(0, 8, 0);
        Assert.That((bool)Call("Begin", 1), Is.True);
        Call("WaitForFinish", 1);
        var flight = (Rigidbody)Call("BeginPhysicalThrow", release, body, collider);
        flight.linearVelocity = Vector3.right * 8;
        flight.angularVelocity = Vector3.up * 5;
        Assert.That(collider.enabled, Is.False);

        Call("Complete", 1, 26, release, Quaternion.identity, false);
        ReceiveTeleport(networkTransform, 26, release);
        yield return new WaitForSecondsRealtime(.28f);
        float firstPosition = flight.position.x;
        yield return new WaitForSecondsRealtime(.12f);
        Assert.That(flight.position.x, Is.GreaterThan(firstPosition + .3f));
        Assert.That(Quaternion.Angle(flight.rotation, Quaternion.identity), Is.GreaterThan(1f));
        Assert.That(Read<bool>("IsActive"), Is.True, "The launch teleport cannot end the local flight.");

        flight.isKinematic = true;
        Vector3 finalPosition = flight.position;
        _root.transform.SetPositionAndRotation(finalPosition, flight.rotation);
        Call("CompletePhysicalThrow", 1, 27, finalPosition);
        yield return null;
        Assert.That(Read<bool>("IsActive"), Is.True, "Keep showing the settled die while the final pose is in transit.");
        ReceiveTeleport(networkTransform, 27, finalPosition);
        yield return null;
        Assert.That(Read<bool>("IsActive"), Is.False);
        Assert.That(collider.enabled, Is.True);
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(finalPosition));
        yield return new ExitPlayMode();
    }

    private static void ReceiveTeleport(Component networkTransform, int tick, Vector3 position)
    {
        var type = typeof(NetworkTransform.NetworkTransformState);
        const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        var state = new NetworkTransform.NetworkTransformState();
        object boxedState = state;
        type.GetField("NetworkTick", privateInstance).SetValue(boxedState, tick);
        object flags = type.GetField("FlagStates", privateInstance).GetValue(boxedState);
        const BindingFlags fields = privateInstance | BindingFlags.Public;
        flags.GetType().GetField("IsTeleportingNextFrame", fields).SetValue(flags, true);
        flags.GetType().GetField("HasPositionChange", fields).SetValue(flags, true);
        type.GetField("FlagStates", privateInstance).SetValue(boxedState, flags);
        type.GetField("PositionX", privateInstance).SetValue(boxedState, position.x);
        type.GetField("PositionY", privateInstance).SetValue(boxedState, position.y);
        type.GetField("PositionZ", privateInstance).SetValue(boxedState, position.z);
        networkTransform.GetType().GetMethod("OnNetworkTransformStateUpdated", privateInstance)
            .Invoke(networkTransform, new object[] { state, boxedState });
    }

    [UnityTest]
    public IEnumerator ConsecutiveKeyboardPredictionsStayAheadOfEarlierServerReplies()
    {
        yield return new EnterPlayMode();
        _root = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _type = Type.GetType("NetworkDragPreview, Assembly-CSharp", true);
        _preview = _root.AddComponent(_type);

        Assert.That((bool)Call("PredictStep", 1, Vector3.right), Is.True);
        Transform visual = Read<Transform>("DisplayTransform");
        Assert.That((bool)Call("PredictStep", 2, Vector3.right * 2), Is.True);
        Assert.That(Read<Transform>("DisplayTransform"), Is.SameAs(visual));
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(Vector3.right * 2));
        Assert.That(_root.transform.position, Is.EqualTo(Vector3.zero));

        Call("Complete", 1, 25, Vector3.right, Quaternion.identity, false);
        Call("Reject", 1);
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(Vector3.right * 2));
        Call("Complete", 2, 26, Vector3.right, Quaternion.identity, false);
        Assert.That(Read<Vector3>("Position"), Is.EqualTo(Vector3.right));

        Call("Stop");
        Assert.That((bool)Call("Begin", 3), Is.True);
        Assert.That((bool)Call("PredictStep", 4, Vector3.right), Is.False, "Keyboard input must not replace a mouse drag.");
        Call("Stop");
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
