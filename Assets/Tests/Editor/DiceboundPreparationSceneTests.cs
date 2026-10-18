using System;
using NUnit.Framework;

public class DiceboundPreparationSceneTests
{
    private static Type Runtime(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    [Test]
    public void GridOnlyCampaignCanBePreparedBeforeImportingAMap()
    {
        var scene = Activator.CreateInstance(Runtime("SceneDefinition")); var type = scene.GetType();
        type.GetField("sceneId").SetValue(scene, "preparation");
        type.GetField("gridWidth").SetValue(scene, 8); type.GetField("gridHeight").SetValue(scene, 8);
        type.GetField("cellSize").SetValue(scene, 1f); type.GetField("mapImage").SetValue(scene, "");
        Runtime("SceneValidation").GetMethod("Validate", new[] { type }).Invoke(null, new[] { scene });
        var entry = Activator.CreateInstance(Runtime("CampaignScene"));
        entry.GetType().GetField("sceneId").SetValue(entry, "preparation");
        entry.GetType().GetField("title").SetValue(entry, "Подготовка");
        entry.GetType().GetField("scene").SetValue(entry, scene);
        var entries = Array.CreateInstance(entry.GetType(), 1); entries.SetValue(entry, 0);
        var campaign = Activator.CreateInstance(Runtime("CampaignDefinition"));
        campaign.GetType().GetField("campaignId").SetValue(campaign, "campaign");
        campaign.GetType().GetField("activeSceneId").SetValue(campaign, "preparation");
        campaign.GetType().GetField("scenes").SetValue(campaign, entries);
        Runtime("SceneValidation").GetMethod("Validate", new[] { campaign.GetType() }).Invoke(null, new[] { campaign });
        Runtime("CampaignFileStore").GetMethod("ValidateEmbeddedImageBudget").Invoke(null, new[] { campaign });
        Assert.That(type.GetField("mapImage").GetValue(scene), Is.EqualTo(""));
        Assert.That(campaign.GetType().GetField("movementPolicy").GetValue(campaign), Is.EqualTo(3));
    }
}
