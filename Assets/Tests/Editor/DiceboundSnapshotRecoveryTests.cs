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

    [TestCase(64, 96, 0)]
    [TestCase(64, 64, 3)]
    [TestCase(64, 0, 3)]
    public void PartialMapProgressRestoresOnlyTheConsecutiveRetryBudget(int before, int after, int expected)
    {
        var method = Runtime("MapSync").GetMethod("MapAttemptAfterProgress", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method.Invoke(null, new object[] { 3, before, after }), Is.EqualTo(expected));
    }

    [TestCase(false, 0, 1)]
    [TestCase(false, 5, 0)]
    [TestCase(true, 0, 33)]
    public void FreshMapChunkResetsStallRetriesAndReportsOnlyContiguousProgress(bool gap, int index, int contiguous)
    {
        var root = new GameObject("Map progress recovery test");
        try
        {
            var type = Runtime("MapSync");
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent(type);
            void Set(string field, object value) => type.GetField(field, Private).SetValue(sync, value);
            Set("_receivingMap", true); Set("_incomingVersion", 7);
            Set("_incomingTotalChunks", 100); Set("_incomingBytes", 100000); Set("_retryCount", 3);
            var chunks = (System.Collections.Generic.Dictionary<int, byte[]>)type.GetField("_incomingChunks", Private).GetValue(sync);
            if (gap) for (int i = 1; i <= 32; i++) chunks[i] = new byte[1000];
            using var writer = new FastBufferWriter(1012, Allocator.Temp);
            writer.WriteValueSafe(7); writer.WriteValueSafe(index); writer.WriteValueSafe(1000); writer.WriteBytesSafe(new byte[1000]);
            var receive = type.GetMethod("OnMapChunkReceived", Private);
            using (var reader = new FastBufferReader(writer, Allocator.Temp)) receive.Invoke(sync, new object[] { 0UL, reader });
            Assert.That(type.GetField("_retryCount", Private).GetValue(sync), Is.EqualTo(0));
            Assert.That(type.GetField("_contiguousReceived", Private).GetValue(sync), Is.EqualTo(contiguous));
            Set("_retryCount", 2);
            using (var reader = new FastBufferReader(writer, Allocator.Temp)) receive.Invoke(sync, new object[] { 0UL, reader });
            Assert.That(type.GetField("_retryCount", Private).GetValue(sync), Is.EqualTo(2), "Duplicate bytes are not new progress.");
            Assert.That(chunks.Count, Is.EqualTo(gap ? 33 : 1));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void DisposedMapTransferReleasesItsActiveSendSlot()
    {
        var root = new GameObject("Disposed map transfer test");
        try
        {
            var type = Runtime("MapSync");
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent(type);
            var routine = (System.Collections.IEnumerator)type.GetMethod("SendMapWithRetriesToClientRoutine").Invoke(sync, new object[] { 42UL });
            var active = (System.Collections.Generic.HashSet<(ulong, int)>)type.GetField("_activeSends", Private).GetValue(sync);
            Assert.That(routine.MoveNext(), Is.True);
            Assert.That(active.Contains((42UL, 0)), Is.True);
            ((IDisposable)routine).Dispose();
            Assert.That(active.Contains((42UL, 0)), Is.False, "Stopping a transfer must allow the same version to be retried.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void AbortedMapTransferCannotRemoveReplacementSlotOrAnotherClient()
    {
        var root = new GameObject("Replaced map transfer test");
        try
        {
            var type = Runtime("MapSync");
            root.AddComponent<NetworkObject>();
            var sync = root.AddComponent(type);
            var send = type.GetMethod("SendMapWithRetriesToClientRoutine");
            var old = (System.Collections.IEnumerator)send.Invoke(sync, new object[] { 42UL });
            var active = (System.Collections.Generic.HashSet<(ulong, int)>)type.GetField("_activeSends", Private).GetValue(sync);
            Assert.That(old.MoveNext(), Is.True);
            active.Add((43UL, 0));
            type.GetMethod("AbortTransferForClient").Invoke(sync, new object[] { 42UL });
            Assert.That(active.Contains((42UL, 0)), Is.False);
            Assert.That(active.Contains((43UL, 0)), Is.True);
            var replacement = (System.Collections.IEnumerator)send.Invoke(sync, new object[] { 42UL });
            Assert.That(replacement.MoveNext(), Is.True);
            ((IDisposable)old).Dispose();
            Assert.That(active.Contains((42UL, 0)), Is.True, "An old iterator must not release a replacement transfer.");
            ((IDisposable)replacement).Dispose();
            Assert.That(active.Contains((42UL, 0)), Is.False);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [TestCase(6, false)]
    [TestCase(7, true)]
    [TestCase(8, false)]
    public void WorldReadinessRequiresTheExpectedManifestAck(int acknowledgement, bool ready)
    {
        var root = new GameObject("World acknowledgement test");
        try
        {
            var type = Runtime("LateJoinSync");
            var sync = root.AddComponent(type);
            var expected = (System.Collections.Generic.Dictionary<ulong, int>)type.GetField("_sceneWorldExpectedVersions", Private).GetValue(sync);
            var acks = (System.Collections.Generic.Dictionary<ulong, int>)type.GetField("_sceneWorldAcks", Private).GetValue(sync);
            expected[42] = 7; acks[42] = acknowledgement;
            Assert.That(type.GetMethod("HasClientCurrentWorld").Invoke(sync, new object[] { 42UL }), Is.EqualTo(ready));
            Assert.That(type.GetMethod("HasClientCurrentWorld").Invoke(sync, new object[] { 43UL }), Is.EqualTo(false));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

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
