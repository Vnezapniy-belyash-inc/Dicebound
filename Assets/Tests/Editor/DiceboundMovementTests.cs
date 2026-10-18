using System;
using NUnit.Framework;
using UnityEngine;

public class DiceboundMovementTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);

    [TestCase(100, 3, 4, 3)]
    [TestCase(-100, 3, 2, 3)]
    [TestCase(3, 100, 3, 4)]
    [TestCase(3, -100, 3, 2)]
    [TestCase(10, 10, 4, 3)]
    [TestCase(3, 3, 3, 3)]
    [TestCase(4, 30, 3, 4)]
    public void DragIsAtMostOneCardinalCell(int x, int y, int expectedX, int expectedY)
    {
        var result = Runtime("MovementRisk").GetMethod("SingleStep").Invoke(null,
            new object[] { new Vector2Int(3, 3), new Vector2Int(x, y) });
        Assert.That(result, Is.EqualTo(new Vector2Int(expectedX, expectedY)));
    }

    [TestCase(1, false, true, false)]
    [TestCase(2, false, true, false)]
    [TestCase(3, false, true, true)]
    [TestCase(30, true, true, false)]
    [TestCase(1, true, false, true)]
    [TestCase(0, false, false, false)]
    public void ReviewIsReservedForRiskyPaths(float distance, bool known, bool clear, bool expected)
    {
        var result = Runtime("MovementRisk").GetMethod("RequiresReview").Invoke(null,
            new object[] { Vector2.zero, new Vector2(distance, 0), known, clear });
        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void LegacyCampaignPreferencesKeepSafeDefaultsWhenOverwritten()
    {
        var campaign = Activator.CreateInstance(Runtime("CampaignDefinition"));
        JsonUtility.FromJsonOverwrite("{\"version\":1,\"title\":\"Старая кампания\"}", campaign);
        Assert.That(campaign.GetType().GetField("movementPolicy").GetValue(campaign), Is.EqualTo(3));
        Assert.That(campaign.GetType().GetField("autosave").GetValue(campaign), Is.True);
        Assert.That(campaign.GetType().GetField("exportFogHistory").GetValue(campaign), Is.True);
        JsonUtility.FromJsonOverwrite("{\"movementPolicy\":0,\"autosave\":false,\"exportFogHistory\":false}", campaign);
        Assert.That(campaign.GetType().GetField("movementPolicy").GetValue(campaign), Is.EqualTo(0));
        Assert.That(campaign.GetType().GetField("autosave").GetValue(campaign), Is.False);
    }
}
