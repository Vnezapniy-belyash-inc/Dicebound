using System;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class DiceboundSnapshotRecoveryTests
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);

    [TestCase("SceneEditor", 5, true, 5)]
    [TestCase("SceneEditor", 4, true, -1)]
    [TestCase("SceneEditor", 5, false, -1)]
    [TestCase("FogManager", 5, true, 5)]
    [TestCase("FogManager", 4, true, -1)]
    [TestCase("FogManager", 5, false, -1)]
    public void ReplayedAppliedSnapshotSchedulesAckWithoutApplyingAgain(string name, int revision, bool applied, int expectedAck)
    {
        var root = new GameObject("Snapshot recovery test");
        Component receiver = null;
        try
        {
            var manager = root.AddComponent<NetworkManager>();
            var gridObject = new GameObject("Grid"); gridObject.transform.SetParent(root.transform);
            var grid = gridObject.AddComponent(Runtime("GridManager"));
            var receiverObject = new GameObject(name); receiverObject.transform.SetParent(root.transform);
            var type = Runtime(name); receiver = receiverObject.AddComponent(type);
            type.GetField("_network", Private).SetValue(receiver, manager);
            type.GetField("_grid", Private).SetValue(receiver, grid);
            type.GetField("_hasState", Private).SetValue(receiver, applied);
            type.GetField(name == "SceneEditor" ? "_receivedRevision" : "_received", Private).SetValue(receiver, 5);
            type.GetField("_pendingRevisionAck", Private).SetValue(receiver, -1);
            string dirtyField = name == "SceneEditor" ? "_dirty" : "_textureDirty";
            type.GetField(dirtyField, Private).SetValue(receiver, false);
            using var writer = new FastBufferWriter(512, Allocator.Temp);
            writer.WriteValueSafe(revision); writer.WriteValueSafe(10); writer.WriteValueSafe(10);
            if (name == "SceneEditor")
            {
                writer.WriteValueSafe(1f); writer.WriteValueSafe(Vector3.zero); writer.WriteValueSafe(Quaternion.identity);
                byte[] data = Encoding.UTF8.GetBytes("{\"edges\":[],\"obstacles\":[],\"revealPaused\":true}");
                writer.WriteValueSafe(data.Length); writer.WriteBytesSafe(data);
            }
            else
            {
                writer.WriteValueSafe(true);
                var codec = Runtime("FogStateCodec");
                var data = (byte[])codec.GetMethod("Encode").Invoke(null, new object[] { new bool[1600], new bool[1600] });
                writer.WriteValueSafe(data.Length); writer.WriteBytesSafe(data);
            }
            using var reader = new FastBufferReader(writer, Allocator.Temp);
            type.GetMethod(name == "SceneEditor" ? "ReceiveState" : "Receive", Private)
                .Invoke(receiver, new object[] { NetworkManager.ServerClientId, reader });
            Assert.That(type.GetField("_pendingRevisionAck", Private).GetValue(receiver), Is.EqualTo(expectedAck));
            Assert.That(type.GetField(dirtyField, Private).GetValue(receiver), Is.EqualTo(false), "A replay must not rebuild or replace the current snapshot.");
        }
        finally
        {
            if (receiver != null) receiver.GetType().GetField("_network", Private).SetValue(receiver, null);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
