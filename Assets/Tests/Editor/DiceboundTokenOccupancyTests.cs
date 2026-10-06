using System;
using NUnit.Framework;

public class DiceboundTokenOccupancyTests
{
    private object _cells;
    private Type _type;
    [SetUp]
    public void SetUp()
    {
        _type = Type.GetType("TokenCellOccupancy, Assembly-CSharp", true);
        _cells = Activator.CreateInstance(_type);
    }
    private object Call(string method, int x, int y) => _type.GetMethod(method).Invoke(_cells, new object[] { x, y });

    [Test]
    public void RemovingOneOfTwoRevealedTokensDoesNotFreeTheSharedCell()
    {
        Call("Add", 4, 5);
        Call("Add", 4, 5);
        Call("Release", 4, 5);
        Assert.That(Call("IsOccupied", 4, 5), Is.True);
        Assert.That(Call("TryOccupy", 4, 5), Is.False);
        Call("Release", 4, 5);
        Assert.That(Call("IsOccupied", 4, 5), Is.False);
    }

    [Test]
    public void HidingAndRevealingOverlappingTokensPreservesOtherReservations()
    {
        Call("Add", 4, 5);
        Call("Add", 4, 5);
        Call("Release", 4, 5); // Hide one token: hidden tokens do not register another reservation.
        Assert.That(Call("IsOccupied", 4, 5), Is.True);
        Call("Release", -1, -1); // Deleting/moving a hidden token releases no visible reservation.
        Assert.That(Call("IsOccupied", 4, 5), Is.True);
        Call("Add", 4, 5); // Reveal in place, without relocating.
        Call("Release", 4, 5);
        Assert.That(Call("IsOccupied", 4, 5), Is.True);
    }

    [Test]
    public void NormalPlacementCannotReserveAnOccupiedCell()
    {
        Assert.That(Call("TryOccupy", 1, 1), Is.True);
        Assert.That(Call("TryOccupy", 1, 1), Is.False);
        Call("Release", 1, 1);
        Assert.That(Call("TryOccupy", 1, 1), Is.True);
    }

    [Test]
    public void RebuildClearsPreviousReservationCounts()
    {
        Call("Add", 4, 5);
        Call("Add", 4, 5);
        _type.GetMethod("Clear").Invoke(_cells, null);
        Assert.That(Call("IsOccupied", 4, 5), Is.False);
    }
}
