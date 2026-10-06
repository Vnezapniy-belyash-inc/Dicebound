using System;
using System.Reflection;
using NUnit.Framework;

// Predefined Assembly-CSharp cannot be referenced by an asmdef. Reflection keeps the
// production scripts in their existing assembly and exercises the actual implementation.
public class DiceboundDragLeaseTests
{
    private object _lease;
    private Type _type;
    [SetUp]
    public void SetUp()
    {
        _type = Type.GetType("NetworkDragLease, Assembly-CSharp", true);
        _lease = Activator.CreateInstance(_type);
    }

    private bool Begin(ulong client, int gesture, double now) =>
        (bool)_type.GetMethod("TryBegin").Invoke(_lease, new object[] { client, gesture, now });
    private bool Move(ulong client, int gesture, int sequence, double now) =>
        (bool)_type.GetMethod("TryMove").Invoke(_lease, new object[] { client, gesture, sequence, now });
    private void Release() => _type.GetMethod("Release").Invoke(_lease, null);

    [Test]
    public void SimultaneousGrabHasOneWinner()
    {
        Assert.That(Begin(1, 1, 0), Is.True);
        Assert.That(Begin(2, 1, 0), Is.False);
        Assert.That(Move(2, 1, 1, 0.1), Is.False);
        Assert.That(Move(1, 1, 1, 0.1), Is.True);
    }

    [Test]
    public void ReorderedAndDuplicateMovesCannotRewindPosition()
    {
        Begin(1, 1, 0);
        Assert.That(Move(1, 1, 3, 0.1), Is.True);
        Assert.That(Move(1, 1, 1, 0.2), Is.False);
        Assert.That(Move(1, 1, 3, 0.3), Is.False);
        Assert.That(Move(1, 1, 4, 0.4), Is.True);
    }

    [Test]
    public void PreviousGestureCannotAffectNextGesture()
    {
        Begin(1, 1, 0);
        Release();
        Assert.That(Begin(1, 2, 0.1), Is.True);
        Assert.That(Move(1, 1, 99, 0.2), Is.False);
        Assert.That(Begin(1, 1, 0.2), Is.False);
        Assert.That(Move(1, 2, 1, 0.3), Is.True);
    }

    [Test]
    public void ReleasedGestureCannotBeResurrected()
    {
        Begin(1, 1, 0);
        Release();
        Assert.That(Move(1, 1, 1, 0.1), Is.False);
        Assert.That(Begin(1, 1, 0.2), Is.False);
        Assert.That(Begin(1, 2, 0.3), Is.True);
    }

    [Test]
    public void ExpiredLeaseRejectsMovementAndCanBeReleased()
    {
        Begin(1, 1, 0);
        Assert.That((bool)_type.GetMethod("HasExpired").Invoke(_lease, new object[] { 2.1 }), Is.True);
        Assert.That(Move(1, 1, 1, 2.1), Is.False);
        Release();
        Assert.That(Begin(2, 1, 2.2), Is.True);
    }

    [Test]
    public void MovementRenewsLeaseEvenWithMissingPackets()
    {
        Begin(1, 1, 0);
        Assert.That(Move(1, 1, 20, 1.9), Is.True);
        Assert.That((bool)_type.GetMethod("HasExpired").Invoke(_lease, new object[] { 3.8 }), Is.False);
        Assert.That((bool)_type.GetMethod("HasExpired").Invoke(_lease, new object[] { 4.0 }), Is.True);
    }

    [Test]
    public void RejectedBeginCannotAcquireLaterAndReconnectCanResetCounter()
    {
        Begin(1, 1, 0);
        Assert.That(Begin(2, 1, 0), Is.False);
        Release();
        Assert.That(Begin(2, 1, 0.1), Is.False);
        Assert.That(Begin(2, 2, 0.2), Is.True);
        Release();
        _type.GetMethod("ForgetClient").Invoke(_lease, new object[] { 2UL });
        Assert.That(Begin(2, 1, 0.3), Is.True);
    }
}

