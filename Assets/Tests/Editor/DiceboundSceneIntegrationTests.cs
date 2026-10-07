using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

public class DiceboundSceneIntegrationTests
{
    private NetworkManager _manager;
    private GameObject _root;
    private Texture2D _texture;
    private string _path;
    private static Type TypeOf(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Call(object item, string name, params object[] args) => item.GetType().GetMethod(name).Invoke(item, args);
    private static T Read<T>(object item, string name) => (T)item.GetType().GetProperty(name).GetValue(item);
    private Component Add(string type, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(_root.transform);
        return go.AddComponent(TypeOf(type));
    }

    [UnityTest]
    public IEnumerator SceneRestoresMapDoorColumnHiddenNamedTokenAndVision()
    {
        yield return new EnterPlayMode();
        _root = new GameObject("Scene test");
        var managerObject = new GameObject("Test network");
        _manager = managerObject.AddComponent<NetworkManager>();
        var transport = managerObject.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", 17879);
        _manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _manager.AddNetworkPrefab(prefab);
        var grid = Add("GridManager", "Grid");
        grid.GetType().GetField("gridWidth").SetValue(grid, 10); grid.GetType().GetField("gridHeight").SetValue(grid, 10);
        var tokenManager = Add("TokenManager", "Tokens");
        tokenManager.GetType().GetField("tokenPrefab").SetValue(tokenManager, prefab.GetComponent<NetworkObject>());
        var mapObject = new GameObject("Map"); mapObject.transform.SetParent(_root.transform);
        mapObject.AddComponent<NetworkObject>();
        var map = mapObject.AddComponent(TypeOf("MapController"));
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.transform.SetParent(mapObject.transform, false);
        map.GetType().GetField("mapPlane").SetValue(map, plane);
        var sync = mapObject.AddComponent(TypeOf("MapSync")); sync.GetType().GetField("mapController").SetValue(sync, map);
        var editor = Add("SceneEditor", "Editor");
        var fog = Add("FogManager", "Fog");
        yield return null;
        Assert.That(_manager.StartHost(), Is.True);
        yield return null;
        _texture = new Texture2D(4, 4); _texture.SetPixels(new Color[16]); _texture.Apply();
        Call(map, "ApplyImage", _texture.EncodeToPNG());
        var model = Read<object>(editor, "Model");
        Call(model, "SetEdge", 2, 2, false, true, false, false);
        Call(model, "SetEdge", 2, 2, false, false, false, true);
        Call(model, "SetObstacle", 3, 3, true, 0.4f, false);
        var token = Call(tokenManager, "CreateTokenAsHost", "Гоблин", true);
        TypeOf("GameMasterUndo").GetMethod("Clear").Invoke(null, null);
        Call(token, "ServerSetHealth", 12, 12, true);
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        var restoredMaster = Call(token, "CaptureMasterData");
        Assert.That((int)restoredMaster.GetType().GetField("currentHp").GetValue(restoredMaster), Is.EqualTo(0));
        Assert.That((int)restoredMaster.GetType().GetField("maxHp").GetValue(restoredMaster), Is.EqualTo(0));
        Call(token, "ServerSetArmorClass", 18, true);
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        restoredMaster = Call(token, "CaptureMasterData");
        Assert.That((int)restoredMaster.GetType().GetField("armorClass").GetValue(restoredMaster), Is.EqualTo(10));
        Assert.That(Read<int>(token, "NetworkVisibleCurrentHp"), Is.EqualTo(-1), "Hidden HP must not enter the shared initiative view.");
        Call(token, "ServerSetMasterVisibility", false, false);
        Assert.That(Read<int>(token, "NetworkVisibleCurrentHp"), Is.EqualTo(0));
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        restoredMaster = Call(token, "CaptureMasterData");
        Assert.That((bool)restoredMaster.GetType().GetField("hideHp").GetValue(restoredMaster), Is.True);
        Assert.That((bool)restoredMaster.GetType().GetField("hideConditions").GetValue(restoredMaster), Is.True);
        Assert.That(Read<int>(token, "NetworkVisibleCurrentHp"), Is.EqualTo(-1));
        Call(token, "ServerToggleCondition", "poisoned");
        Call(fog, "TogglePreview");
        Assert.That(Read<int>(token, "DisplayedCurrentHp"), Is.EqualTo(-1), "Player preview must hide private HP.");
        Assert.That(Read<int>(token, "DisplayedMaxHp"), Is.EqualTo(-1));
        Assert.That(Read<string[]>(token, "VisibleConditionIds"), Is.Empty, "Player preview must hide private conditions.");
        Call(fog, "TogglePreview");
        Assert.That(Read<string[]>(token, "VisibleConditionIds"), Does.Contain("poisoned"), "Master view must retain private conditions.");
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        restoredMaster = Call(token, "CaptureMasterData");
        Assert.That(((string[])restoredMaster.GetType().GetField("conditionIds").GetValue(restoredMaster)).Length, Is.EqualTo(0));
        TypeOf("GameMasterUndo").GetMethod("Clear").Invoke(null, null);
        Call(token, "RequestSetVision", 30); Call(token, "LoadImage", _texture.EncodeToPNG());
        Call(token, "RequestSetHidden", false); Call(fog, "TogglePause"); yield return null;
        Call(token, "RequestSetHidden", true); yield return null;
        string memory = JsonUtility.ToJson(Call(fog, "Capture", true));
        string stableId = Read<string>(token, "SceneId");
        _path = Path.Combine(Application.temporaryCachePath, "dicebound-scene-test-" + Guid.NewGuid().ToString("N") + ".json");
        TypeOf("SceneFileStore").GetMethod("Save").Invoke(null, new object[] { _path });
        Call(model, "SetEdge", 2, 2, false, false, true, false);
        Call(token, "RequestSetHidden", false);
        string savedJson = File.ReadAllText(_path);
        var broken = JsonUtility.FromJson(savedJson, TypeOf("SceneDefinition"));
        broken.GetType().GetField("mapImage").SetValue(broken, "invalid-base64");
        File.WriteAllText(_path, JsonUtility.ToJson(broken));
        Assert.Throws<TargetInvocationException>(() => TypeOf("SceneFileStore").GetMethod("Load").Invoke(null, new object[] { _path }));
        Assert.That(Read<bool>(token, "IsSpawned"), Is.True, "Invalid import must not delete existing tokens.");
        Assert.That(Read<bool>(token, "IsHidden"), Is.False, "Invalid import must preserve the current state.");
        File.WriteAllText(_path, savedJson);
        TypeOf("SceneFileStore").GetMethod("Load").Invoke(null, new object[] { _path });
        yield return null;
        var tokenType = TypeOf("TokenController");
        object restored = null;
        foreach (var behaviour in UnityEngine.Object.FindObjectsByType<NetworkBehaviour>(FindObjectsInactive.Exclude))
            if (behaviour.GetType() == tokenType && behaviour.IsSpawned) { Assert.That(restored, Is.Null); restored = behaviour; }
        Assert.That(restored, Is.Not.Null);
        Assert.That(Read<string>(restored, "TokenName"), Is.EqualTo("Гоблин"));
        Assert.That(Read<string>(restored, "SceneId"), Is.EqualTo(stableId));
        Assert.That(Read<bool>(restored, "IsHidden"), Is.True);
        var eligibleTransfers = (Array)TypeOf("DmPanelUI").GetMethod("GetActiveSceneTokens",
            BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null);
        Assert.That(eligibleTransfers.Length, Is.EqualTo(1), "The transfer list must include spawned NPCs even when hidden.");
        Assert.That(eligibleTransfers.GetValue(0), Is.SameAs(restored), "Token IDs must not be compared with the active scene ID.");
        Assert.That(Read<int>(restored, "VisionFeet"), Is.EqualTo(30));
        Assert.That(Call(restored, "GetPortraitJpg"), Is.Not.Null);
        var geometry = Call(model, "Snapshot");
        var edges = (Array)geometry.GetType().GetField("edges").GetValue(geometry);
        Assert.That(edges.Length, Is.EqualTo(1)); Assert.That(edges.GetValue(0).GetType().GetField("open").GetValue(edges.GetValue(0)), Is.EqualTo(true));
        Assert.That(Read<bool>(model, "RevealPaused"), Is.True);
        Assert.That(JsonUtility.ToJson(Call(fog, "Capture", true)), Is.EqualTo(memory));
        TypeOf("SceneFileStore").GetMethod("SaveOptions").Invoke(null, new object[] { _path, false, false });
        var fresh = JsonUtility.FromJson(File.ReadAllText(_path), TypeOf("SceneDefinition"));
        Assert.That((bool)fresh.GetType().GetField("includesPlayers").GetValue(fresh), Is.True);
        var freshFog = fresh.GetType().GetField("fog").GetValue(fresh);
        Assert.That(freshFog.GetType().GetField("explored").GetValue(freshFog), Is.EqualTo(""));
        Assert.That(JsonUtility.ToJson(Call(fog, "Capture", true)), Is.EqualTo(memory));
        TypeOf("SceneFileStore").GetMethod("Load").Invoke(null, new object[] { _path }); yield return null;
        var blankMemory = Call(fog, "Capture", true);
        var bits = Convert.FromBase64String((string)blankMemory.GetType().GetField("explored").GetValue(blankMemory));
        Assert.That(Array.TrueForAll(bits, value => value == 0), Is.True);
        Assert.That(Read<bool>(fog, "Paused"), Is.True);
        var tracker = Add("InitiativeTracker", "Initiative restore test");
        tracker.GetComponent<NetworkObject>().Spawn();
        var battle = Activator.CreateInstance(TypeOf("SceneBattleState"));
        var participantType = TypeOf("BattleParticipant");
        var participants = Array.CreateInstance(participantType, 2);
        string transferredId = Guid.NewGuid().ToString("N");
        for (int i = 0; i < 2; i++)
        {
            var participant = Activator.CreateInstance(participantType);
            participantType.GetField("id").SetValue(participant, i == 0 ? transferredId : "1");
            participantType.GetField("name").SetValue(participant, i == 0 ? "Перенесённый" : "Исходный");
            participantType.GetField("initiative").SetValue(participant, i == 0 ? 12 : 20);
            participantType.GetField("hasHitPoints").SetValue(participant, true);
            participantType.GetField("hitPoints").SetValue(participant, i == 0 ? 7 : 15);
            participants.SetValue(participant, i);
        }
        battle.GetType().GetField("participants").SetValue(battle, participants);
        battle.GetType().GetField("activeParticipantId").SetValue(battle, transferredId);
        battle.GetType().GetField("round").SetValue(battle, 4);
        var pendingIds = (System.Collections.Generic.Queue<string>)tracker.GetType().GetField("_awaitingInitiativeTokenIds",
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(tracker);
        pendingIds.Enqueue("old-scene-token");
        tracker.GetType().GetField("_awaitingInitiativeTokenId", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(tracker, "old-scene-token");
        Call(tracker, "RestoreBattleState", battle);
        Assert.That(pendingIds, Is.Empty, "Loading a battle must discard pending rolls from the previous scene.");
        Assert.That(tracker.GetType().GetField("_awaitingInitiativeTokenId", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(tracker), Is.Null);
        var capturedBattle = Call(tracker, "CaptureBattleState");
        var capturedParticipants = (Array)capturedBattle.GetType().GetField("participants").GetValue(capturedBattle);
        string activeId = (string)capturedBattle.GetType().GetField("activeParticipantId").GetValue(capturedBattle);
        var ids = new System.Collections.Generic.HashSet<string>();
        foreach (object participant in capturedParticipants)
        {
            string id = (string)participantType.GetField("id").GetValue(participant);
            Assert.That(ids.Add(id), Is.True, "Restored IDs must be unique.");
            string name = (string)participantType.GetField("name").GetValue(participant);
            Assert.That(participantType.GetField("hitPoints").GetValue(participant), Is.EqualTo(name == "Перенесённый" ? 7 : 15));
            if (id == activeId) Assert.That(name, Is.EqualTo("Перенесённый"));
        }
        Assert.That(ids.Contains(activeId), Is.True);
        Assert.That(capturedBattle.GetType().GetField("round").GetValue(capturedBattle), Is.EqualTo(4));
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        if (_manager != null) _manager.Shutdown();
        yield return null;
        if (_manager != null) UnityEngine.Object.Destroy(_manager.gameObject);
        if (_root != null) UnityEngine.Object.Destroy(_root);
        if (_texture != null) UnityEngine.Object.Destroy(_texture);
        if (!string.IsNullOrEmpty(_path) && File.Exists(_path)) File.Delete(_path);
        yield return null;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator FogUsesCommittedMovesAndRestoresHistoryPermissionsAndUndo()
    {
        yield return new EnterPlayMode();
        _root = new GameObject("Fog integration test");
        var managerObject = new GameObject("Test network");
        _manager = managerObject.AddComponent<NetworkManager>();
        var transport = managerObject.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", 17880);
        _manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _manager.AddNetworkPrefab(prefab);
        var grid = Add("GridManager", "Grid");
        grid.GetType().GetField("gridWidth").SetValue(grid, 8); grid.GetType().GetField("gridHeight").SetValue(grid, 4);
        var tokens = Add("TokenManager", "Tokens");
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        var editor = Add("SceneEditor", "Editor");
        var fog = Add("FogManager", "Fog");
        yield return null;
        Assert.That(_manager.StartHost(), Is.True);
        yield return null;
        yield return null;
        var source = Call(tokens, "CreateTokenAsHost", "Герой", false);
        Vector3 start = (Vector3)Call(grid, "GetCellCenter", 1, 1, 0.1f);
        Vector3 end = (Vector3)Call(grid, "GetCellCenter", 6, 1, 0.1f);
        Call(source, "RestorePosition", start); Call(source, "RequestSetVision", 5);
        yield return null;
        Assert.That(Call(fog, "IsVisible", start), Is.False, "Preparation must not reveal the scene.");
        Call(fog, "TogglePause"); yield return null;
        Assert.That(Call(fog, "IsVisible", start), Is.True);
        Assert.That(Call(source, "BeginDrag", 1), Is.True);
        Call(source, "MoveDrag", 1, end); yield return null;
        Assert.That(Read<Vector3>(source, "CommittedPosition"), Is.EqualTo(start));
        Assert.That(Call(fog, "IsVisible", end), Is.False, "Dragging is not a discovery operation.");
        Call(source, "CancelDrag", 1, end); yield return null;
        Assert.That(Read<Vector3>(source, "CommittedPosition"), Is.EqualTo(start));
        Assert.That(((Component)source).transform.position, Is.EqualTo(start));
        Assert.That(Call(source, "BeginDrag", 2), Is.True);
        Call(source, "EndDrag", 2, end); yield return null;
        Assert.That(Call(fog, "IsVisible", end), Is.True);
        Assert.That(Call(fog, "IsVisible", start), Is.False);
        var npc = Call(tokens, "CreateTokenAsHost", "NPC вне обзора", false);
        Call(npc, "RestorePosition", start); yield return null;
        Assert.That(Call(fog, "CanSeeToken", 0UL, npc, false), Is.True);
        Assert.That(Call(fog, "CanSeeToken", 0UL, npc, true), Is.False, "GM-owned NPCs must not leak into the player preview.");
        var history = Call(fog, "Capture", true);
        string explored = (string)history.GetType().GetField("explored").GetValue(history);
        Assert.That(explored, Is.Not.Empty);
        var blank = Call(fog, "Capture", false);
        Assert.That(blank.GetType().GetField("explored").GetValue(blank), Is.EqualTo(""));
        Assert.That(JsonUtility.ToJson(Call(fog, "Capture", true)), Is.EqualTo(JsonUtility.ToJson(history)), "A fresh export must not change live exploration.");
        var hidden = Call(tokens, "CreateTokenAsHost", "Скрытый", true);
        Call(hidden, "RestorePosition", start); Call(hidden, "RequestSetVision", 60); yield return null;
        Assert.That(Call(fog, "IsVisible", start), Is.False, "A hidden source must not reveal the map.");
        Call(source, "SetEveryoneCanMove", true);
        Assert.That(Call(source, "CanControlClient", 123UL), Is.True);
        Assert.That(Call(hidden, "CanControlClient", 123UL), Is.False);
        Assert.That(Call(fog, "CanSeeToken", 123UL, hidden, false), Is.False);
        string id = Read<string>(hidden, "SceneId");
        Call(hidden, "RequestSetHidden", false); Call(hidden, "RequestDespawn"); yield return null;
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null); yield return null;
        var restored = TypeOf("TokenController").GetMethod("FindSceneToken").Invoke(null, new object[] { id });
        Assert.That(restored, Is.Not.Null);
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null); yield return null;
        Assert.That(Read<bool>(restored, "IsHidden"), Is.True, "Undo must resolve stable IDs after recreation.");
        Call(fog, "Restore", history, true); yield return null;
        Assert.That(Read<bool>(fog, "Paused"), Is.True);
        Assert.That(Call(fog, "IsVisible", end), Is.False);
        Assert.That(Call(fog, "Capture", true).GetType().GetField("explored").GetValue(Call(fog, "Capture", true)), Is.EqualTo(explored));
        var pending = Activator.CreateInstance(TypeOf("SceneToken"));
        pending.GetType().GetField("id").SetValue(pending, Guid.NewGuid().ToString("N"));
        pending.GetType().GetField("name").SetValue(pending, "Вернувшийся герой");
        pending.GetType().GetField("ownerNickname").SetValue(pending, "Игрок ещё не подключился");
        pending.GetType().GetField("hero").SetValue(pending, true);
        pending.GetType().GetField("scale").SetValue(pending, Vector3.one);
        pending.GetType().GetField("position").SetValue(pending, new Vector3(3.5f, 0.1f, 2.5f));
        var waiting = Call(tokens, "RestoreSceneToken", pending, grid, null);
        Assert.That(Read<ulong>(waiting, "ControllerClientId"), Is.EqualTo(ulong.MaxValue));
        Assert.That(Read<bool>(waiting, "IsHero"), Is.True);
        Call(waiting, "ServerRestoreAssignment", 42UL);
        Call(waiting, "AssignController", 0UL);
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        Assert.That(Read<ulong>(waiting, "ControllerClientId"), Is.EqualTo(ulong.MaxValue), "Undo must leave a disconnected owner pending.");
        var savedWaiting = TypeOf("SceneFileStore").GetMethod("CaptureToken").Invoke(null, new[] { waiting, grid });
        Assert.That(savedWaiting.GetType().GetField("ownerNickname").GetValue(savedWaiting), Is.EqualTo("Игрок ещё не подключился"));
    }
}
