using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class DiceboundCampaignModelTests
{
    private static Type TypeOf(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object New(string name) => Activator.CreateInstance(TypeOf(name));
    private static void Set(object item, string field, object value) => item.GetType().GetField(field).SetValue(item, value);
    private static T Get<T>(object item, string field) => (T)item.GetType().GetField(field).GetValue(item);

    private static object ValidScene(string id)
    {
        var scene = New("SceneDefinition");
        Set(scene, "version", 2);
        Set(scene, "sceneId", id);
        Set(scene, "title", id);
        Set(scene, "gridWidth", 4);
        Set(scene, "gridHeight", 4);
        Set(scene, "cellSize", 1f);
        Set(scene, "mapImage", Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        return scene;
    }

    [Test]
    public void LegacySceneUpgradesWithoutLosingExistingSceneData()
    {
        var scene = ValidScene("legacy");
        Set(scene, "version", 1);
        Set(scene, "sceneId", "");

        var upgraded = TypeOf("SceneSaveMigration").GetMethod("UpgradeScene").Invoke(null, new[] { scene });

        Assert.That(Get<int>(upgraded, "version"), Is.EqualTo(2));
        Assert.That(Get<string>(upgraded, "sceneId"), Is.Not.Empty);
        Assert.That(Get<string>(upgraded, "mapImage"), Is.EqualTo(Convert.ToBase64String(new byte[] { 1, 2, 3 })));
        TypeOf("SceneValidation").GetMethod("Validate", new[] { TypeOf("SceneDefinition") })
            .Invoke(null, new[] { upgraded });
    }

    [Test]
    public void LegacySceneBecomesSingleSceneCampaignWithBattleState()
    {
        var scene = ValidScene("legacy");
        Set(scene, "version", 1);
        Set(scene, "sceneId", "");

        var campaign = TypeOf("SceneSaveMigration").GetMethod("UpgradeSingleScene")
            .Invoke(null, new[] { TypeOf("SceneSaveMigration").GetMethod("UpgradeScene")
                .Invoke(null, new[] { scene }) });
        var scenes = Get<Array>(campaign, "scenes");
        Assert.That(scenes.Length, Is.EqualTo(1));
        Assert.That(Get<string>(campaign, "activeSceneId"),
            Is.EqualTo(Get<string>(scenes.GetValue(0), "sceneId")));
        Assert.That(Get<object>(scenes.GetValue(0), "battle"), Is.Not.Null);
        TypeOf("SceneValidation").GetMethod("Validate", new[] { TypeOf("CampaignDefinition") })
            .Invoke(null, new[] { campaign });
    }

    [Test]
    public void CampaignCanContainMultipleScenesAndKeepsMasterDataSeparate()
    {
        var campaign = New("CampaignDefinition");
        Set(campaign, "campaignId", "campaign-1");
        Set(campaign, "activeSceneId", "scene-b");
        var entries = Array.CreateInstance(TypeOf("CampaignScene"), 2);
        string[] ids = { "scene-a", "scene-b" };
        for (int index = 0; index < ids.Length; index++)
        {
            string id = ids[index];
            var entry = New("CampaignScene");
            Set(entry, "sceneId", id);
            Set(entry, "title", id);
            Set(entry, "scene", ValidScene(id));
            entries.SetValue(entry, index);
        }
        Set(campaign, "scenes", entries);

        TypeOf("SceneValidation").GetMethod("Validate", new[] { TypeOf("CampaignDefinition") })
            .Invoke(null, new[] { campaign });
        Assert.That(Get<object>(Get<object>(entries.GetValue(0), "scene"), "masterData"), Is.Not.Null);
    }

    [Test]
    public void DuplicateSceneIdsAreRejected()
    {
        var campaign = New("CampaignDefinition");
        Set(campaign, "campaignId", "campaign-1");
        Set(campaign, "activeSceneId", "scene-a");
        var entries = Array.CreateInstance(TypeOf("CampaignScene"), 2);
        for (int i = 0; i < entries.Length; i++)
        {
            var entry = New("CampaignScene");
            Set(entry, "sceneId", "scene-a");
            Set(entry, "title", "Scene " + i);
            Set(entry, "scene", ValidScene("scene-a"));
            entries.SetValue(entry, i);
        }
        Set(campaign, "scenes", entries);

        var error = Assert.Throws<TargetInvocationException>(() =>
            TypeOf("SceneValidation").GetMethod("Validate", new[] { TypeOf("CampaignDefinition") })
                .Invoke(null, new[] { campaign }));
        Assert.That(error.InnerException, Is.TypeOf<FormatException>());
    }

    [Test]
    public void CampaignStoreRoundTripsMultipleScenesAndLegacySceneImport()
    {
        var campaign = New("CampaignDefinition");
        Set(campaign, "campaignId", "campaign-1");
        Set(campaign, "activeSceneId", "scene-b");
        var entries = Array.CreateInstance(TypeOf("CampaignScene"), 2);
        foreach (int index in new[] { 0, 1 })
        {
            string id = index == 0 ? "scene-a" : "scene-b";
            var entry = New("CampaignScene");
            Set(entry, "sceneId", id);
            Set(entry, "title", id);
            Set(entry, "scene", ValidScene(id));
            entries.SetValue(entry, index);
        }
        Set(campaign, "scenes", entries);

        string campaignPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        string scenePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            TypeOf("CampaignFileStore").GetMethod("Save").Invoke(null, new[] { campaignPath, campaign });
            var restored = TypeOf("CampaignFileStore").GetMethod("LoadCompatible")
                .Invoke(null, new object[] { campaignPath });
            Assert.That(Get<Array>(restored, "scenes").Length, Is.EqualTo(2));
            Assert.That(Get<string>(restored, "activeSceneId"), Is.EqualTo("scene-b"));

            var legacy = ValidScene("legacy");
            Set(legacy, "version", 1);
            Set(legacy, "sceneId", "");
            File.WriteAllText(scenePath, UnityEngine.JsonUtility.ToJson(legacy));
            var imported = TypeOf("CampaignFileStore").GetMethod("LoadCompatible")
                .Invoke(null, new object[] { scenePath });
            Assert.That(Get<Array>(imported, "scenes").Length, Is.EqualTo(1));
            Assert.That(Get<string>(imported, "activeSceneId"), Is.Not.Empty);
        }
        finally
        {
            if (File.Exists(campaignPath)) File.Delete(campaignPath);
            if (File.Exists(scenePath)) File.Delete(scenePath);
        }
    }
}
