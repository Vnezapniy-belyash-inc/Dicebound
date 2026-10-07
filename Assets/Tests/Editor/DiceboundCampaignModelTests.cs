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
    public void InitiativeSnapshotRetainsMoreThanThirtyParticipants()
    {
        var root = new GameObject("Large initiative test");
        try
        {
            var tracker = root.AddComponent(TypeOf("InitiativeTracker"));
            var rows = new string[40];
            string longName = new string('Ж', 100);
            for (int i = 0; i < rows.Length; i++)
                rows[i] = "{\"id\":" + (i + 1) + ",\"name\":\"" + (i == 0 ? longName : "Участник " + i)
                    + "\",\"initiative\":10,\"publicHp\":-1}";
            string json = "{\"currentIndex\":35,\"round\":3,\"activeParticipantId\":\"36\",\"entries\":["
                + string.Join(",", rows) + "]}";
            tracker.GetType().GetMethod("ParseData", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(tracker, new object[] { json });
            var battle = tracker.GetType().GetMethod("CaptureBattleState").Invoke(tracker, null);
            Assert.That(Get<Array>(battle, "participants").Length, Is.EqualTo(40));
            Assert.That(Get<string>(Get<Array>(battle, "participants").GetValue(0), "name"), Is.EqualTo(longName));
            Assert.That(Get<string>(battle, "activeParticipantId"), Is.EqualTo("36"));
            Assert.That(Get<int>(battle, "round"), Is.EqualTo(3));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    [Test]
    public void BattleRejectsDuplicateTokenReferences()
    {
        var scene = ValidScene("scene");
        var token = New("SceneToken");
        Set(token, "id", "token");
        var tokens = Array.CreateInstance(TypeOf("SceneToken"), 1);
        tokens.SetValue(token, 0);
        Set(scene, "tokens", tokens);
        var participants = Array.CreateInstance(TypeOf("BattleParticipant"), 2);
        for (int i = 0; i < 2; i++)
        {
            var participant = New("BattleParticipant");
            Set(participant, "id", i.ToString());
            Set(participant, "tokenId", "token");
            participants.SetValue(participant, i);
        }
        var battle = New("SceneBattleState");
        Set(battle, "participants", participants);
        var exception = Assert.Throws<TargetInvocationException>(() => TypeOf("SceneValidation")
            .GetMethod("ValidateBattle", BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new[] { battle, scene }));
        Assert.That(exception.InnerException, Is.TypeOf<FormatException>());
    }

    [Test]
    public void CharacterStatDetailsIncludeExpertiseInPassivePerception()
    {
        var root = new GameObject("Character import test");
        try
        {
            var character = root.AddComponent(TypeOf("CharacterData"));
            Set(character, "wisdom", 16);
            Set(character, "proficiencyBonus", 3);
            Set(character, "wisSaveProficient", true);
            var skills = Get<Array>(character, "skills");
            foreach (object skill in skills)
                if (Get<string>(skill, "name") == "Восприятие") Set(skill, "expertise", true);
            string details = (string)TypeOf("DmPanelUI").GetMethod("BuildCharacterStatDetails",
                BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { character });
            Assert.That(details, Does.Contain("МДР +6"));
            Assert.That(details, Does.Contain("Восприятие +9 (экспертиза)"));
            Assert.That(details, Does.Contain("Восприятие 19"));
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
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
    public void LegacySceneTransitionDefaultsToNoWorldMarker()
    {
        var scene = ValidScene("legacy");
        var transition = New("SceneTransition");
        Set(transition, "id", "old-door");
        Set(transition, "title", "Old door");
        Set(transition, "targetSceneId", "destination");
        var transitions = Array.CreateInstance(TypeOf("SceneTransition"), 1);
        transitions.SetValue(transition, 0);
        Set(scene, "transitions", transitions);

        TypeOf("SceneSaveMigration").GetMethod("UpgradeScene").Invoke(null, new[] { scene });

        Assert.That(Get<bool>(transition, "markerEnabled"), Is.False);
    }

    [Test]
    public void SceneUpgradePreservesEnabledTransitionMarker()
    {
        var scene = ValidScene("scene-with-marker");
        var transition = New("SceneTransition");
        Set(transition, "id", "door");
        Set(transition, "title", "Door");
        Set(transition, "targetSceneId", "destination");
        Set(transition, "markerEnabled", true);
        var transitions = Array.CreateInstance(TypeOf("SceneTransition"), 1);
        transitions.SetValue(transition, 0);
        Set(scene, "transitions", transitions);

        TypeOf("SceneSaveMigration").GetMethod("UpgradeScene").Invoke(null, new[] { scene });

        Assert.That(Get<bool>(transition, "markerEnabled"), Is.True);
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
        var activeScene = Get<object>(entries.GetValue(1), "scene");
        Set(activeScene, "mapAssetId", "map-forest");
        var battleParticipant = New("BattleParticipant");
        Set(battleParticipant, "id", "7");
        Set(battleParticipant, "name", "Следопыт");
        Set(battleParticipant, "colorHex", "#AABBCC");
        Set(battleParticipant, "initiative", 16);
        Set(battleParticipant, "hitPoints", 13);
        Set(battleParticipant, "hasHitPoints", true);
        var battleParticipants = Array.CreateInstance(TypeOf("BattleParticipant"), 1);
        battleParticipants.SetValue(battleParticipant, 0);
        var battle = New("SceneBattleState");
        Set(battle, "round", 3);
        Set(battle, "activeParticipantId", "7");
        Set(battle, "participants", battleParticipants);
        Set(entries.GetValue(1), "battle", battle);
        var transition = New("SceneTransition");
        Set(transition, "id", "door-to-b");
        Set(transition, "title", "В старый склеп");
        Set(transition, "targetSceneId", "scene-b");
        Set(transition, "x", 3);
        Set(transition, "y", 5);
        Set(transition, "markerEnabled", true);
        var transitions = Array.CreateInstance(TypeOf("SceneTransition"), 1);
        transitions.SetValue(transition, 0);
        Set(Get<object>(entries.GetValue(0), "scene"), "transitions", transitions);
        var mapAsset = New("CampaignMapAsset");
        Set(mapAsset, "id", "map-forest");
        Set(mapAsset, "name", "Лес");
        Set(mapAsset, "imageData", Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        var mapAssets = Array.CreateInstance(TypeOf("CampaignMapAsset"), 1);
        mapAssets.SetValue(mapAsset, 0);
        Set(campaign, "mapAssets", mapAssets);
        var statBlock = New("StatBlockDefinition");
        Set(statBlock, "id", "goblin-basic");
        Set(statBlock, "name", "Гоблин");
        Set(statBlock, "size", "Средний");
        Set(statBlock, "creatureType", "гуманоид");
        Set(statBlock, "alignment", "нейтрально-злой");
        Set(statBlock, "speed", "30 футов");
        Set(statBlock, "challengeRating", "1/4");
        Set(statBlock, "description", "");
        Set(statBlock, "publicFieldsMask", 17);
        var statBlocks = Array.CreateInstance(TypeOf("StatBlockDefinition"), 1);
        statBlocks.SetValue(statBlock, 0);
        Set(campaign, "statBlocks", statBlocks);
        var reference = New("ReferenceEntry");
        Set(reference, "id", "rules-rest");
        Set(reference, "title", "Отдых");
        Set(reference, "category", "Правила");
        Set(reference, "body", "Короткий отдых");
        Set(reference, "pinned", true);
        var tags = Array.CreateInstance(typeof(string), 1);
        tags.SetValue("отдых", 0);
        Set(reference, "tags", tags);
        var references = Array.CreateInstance(TypeOf("ReferenceEntry"), 1);
        references.SetValue(reference, 0);
        Set(campaign, "referenceEntries", references);

        string campaignPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        string scenePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            TypeOf("CampaignFileStore").GetMethod("Save").Invoke(null, new[] { campaignPath, campaign });
            var restored = TypeOf("CampaignFileStore").GetMethod("LoadCompatible")
                .Invoke(null, new object[] { campaignPath });
            Assert.That(Get<Array>(restored, "scenes").Length, Is.EqualTo(2));
            Assert.That(Get<string>(restored, "activeSceneId"), Is.EqualTo("scene-b"));
            var restoredTransitions = Get<Array>(Get<object>(Get<Array>(restored, "scenes").GetValue(0), "scene"), "transitions");
            Assert.That(restoredTransitions.Length, Is.EqualTo(1));
            Assert.That(Get<string>(restoredTransitions.GetValue(0), "targetSceneId"), Is.EqualTo("scene-b"));
            Assert.That(Get<bool>(restoredTransitions.GetValue(0), "markerEnabled"), Is.True);
            var restoredBattle = Get<object>(Get<Array>(restored, "scenes").GetValue(1), "battle");
            var restoredParticipant = Get<Array>(restoredBattle, "participants").GetValue(0);
            Assert.That(Get<string>(restoredParticipant, "name"), Is.EqualTo("Следопыт"));
            Assert.That(Get<int>(restoredParticipant, "hitPoints"), Is.EqualTo(13));
            Assert.That(Get<string>(restoredBattle, "activeParticipantId"), Is.EqualTo("7"));
            Assert.That(Get<Array>(restored, "statBlocks").Length, Is.EqualTo(1));
            Assert.That(Get<string>(Get<Array>(restored, "statBlocks").GetValue(0), "name"), Is.EqualTo("Гоблин"));
            Assert.That(Get<int>(Get<Array>(restored, "statBlocks").GetValue(0), "publicFieldsMask"), Is.EqualTo(17));
            Assert.That(Get<Array>(restored, "referenceEntries").Length, Is.EqualTo(1));
            var restoredReference = Get<Array>(restored, "referenceEntries").GetValue(0);
            Assert.That(Get<string>(restoredReference, "title"), Is.EqualTo("Отдых"));
            Assert.That(Get<bool>(restoredReference, "pinned"), Is.True);
            Assert.That(Get<Array>(restored, "mapAssets").Length, Is.EqualTo(1));
            var restoredScene = Get<object>(Get<Array>(restored, "scenes").GetValue(1), "scene");
            Assert.That(Get<string>(restoredScene, "mapAssetId"), Is.EqualTo("map-forest"));

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

    [Test]
    public void ReferenceLibraryExportFormatRoundTripsIndependently()
    {
        var entry = New("ReferenceEntry");
        Set(entry, "id", "ref-1");
        Set(entry, "title", "Локация");
        Set(entry, "category", "Места");
        Set(entry, "body", "Описание места");
        Set(entry, "pinned", true);
        var tags = Array.CreateInstance(typeof(string), 1);
        tags.SetValue("север", 0);
        Set(entry, "tags", tags);
        var entries = Array.CreateInstance(TypeOf("ReferenceEntry"), 1);
        entries.SetValue(entry, 0);
        var file = New("ReferenceLibraryFile");
        Set(file, "entries", entries);

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, UnityEngine.JsonUtility.ToJson(file));
            var restored = UnityEngine.JsonUtility.FromJson(File.ReadAllText(path), TypeOf("ReferenceLibraryFile"));
            var restoredEntry = Get<Array>(restored, "entries").GetValue(0);
            Assert.That(Get<string>(restoredEntry, "body"), Is.EqualTo("Описание места"));
            Assert.That(Get<bool>(restoredEntry, "pinned"), Is.True);
            Assert.That((string)Get<Array>(restoredEntry, "tags").GetValue(0), Is.EqualTo("север"));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
