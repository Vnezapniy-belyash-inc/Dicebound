using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
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
    private string[] _fixturePaths = Array.Empty<string>();
    private static Type TypeOf(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private static object Call(object item, string name, params object[] args) => item.GetType().GetMethod(name).Invoke(item, args);
    private static T Read<T>(object item, string name) => (T)item.GetType().GetProperty(name).GetValue(item);
    private Component Add(string type, string name)
    {
        var go = new GameObject(name); go.transform.SetParent(_root.transform);
        return go.AddComponent(TypeOf(type));
    }

    private void CreateMapCatalogHost()
    {
        _root = new GameObject("Map catalog test");
        _manager = new GameObject("Map catalog host").AddComponent<NetworkManager>();
        var transport = _manager.gameObject.AddComponent<UnityTransport>(); transport.SetConnectionData("127.0.0.1", 17879);
        _manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        var grid = Add("GridManager", "Grid");
        grid.GetType().GetField("gridWidth").SetValue(grid, 10); grid.GetType().GetField("gridHeight").SetValue(grid, 10);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _manager.AddNetworkPrefab(prefab);
        var tokens = Add("TokenManager", "Tokens");
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        var mapObject = new GameObject("Map"); mapObject.transform.SetParent(_root.transform);
        mapObject.AddComponent<NetworkObject>();
        var map = mapObject.AddComponent(TypeOf("MapController"));
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.transform.SetParent(mapObject.transform, false);
        map.GetType().GetField("mapPlane").SetValue(map, plane);
        var sync = mapObject.AddComponent(TypeOf("MapSync")); sync.GetType().GetField("mapController").SetValue(sync, map);
        Add("SceneEditor", "Editor"); Add("FogManager", "Fog");
    }

    private void CreateMapCatalogImage()
    {
        _texture = new Texture2D(4, 4); _texture.SetPixels(new Color[16]); _texture.Apply();
        var map = TypeOf("MapController").GetProperty("Instance").GetValue(null);
        Call(map, "ApplyImage", _texture.EncodeToPNG());
    }

    [UnityTest]
    public IEnumerator MapCatalogRejectedBatchDoesNotCreateOrChangeCampaign()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateMapCatalogHost(); yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        CreateMapCatalogImage();
        VerifyMapCatalogRejectedBatch();
    }

    private void VerifyMapCatalogRejectedBatch()
    {
        var store = TypeOf("SceneFileStore");
        var campaignField = store.GetField("_campaign", BindingFlags.NonPublic | BindingFlags.Static);
        var dirtyField = store.GetField("_campaignDirty", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(campaignField.GetValue(null), Is.Null);
        bool beforeDirty = (bool)dirtyField.GetValue(null);
        Assert.Throws<TargetInvocationException>(() => store.GetMethod("ImportMapAssets").Invoke(null,
            new object[] { new[] { "valid.png", "empty.png" }, new[] { _texture.EncodeToPNG(), Array.Empty<byte>() } }));
        Assert.That(campaignField.GetValue(null), Is.Null, "A rejected import must not create a campaign.");
        Assert.That(dirtyField.GetValue(null), Is.EqualTo(beforeDirty));
        store.GetMethod("EnsureCampaign").Invoke(null, null);
        string before = JsonUtility.ToJson(campaignField.GetValue(null));
        beforeDirty = (bool)dirtyField.GetValue(null);
        var map = TypeOf("MapController").GetProperty("Instance").GetValue(null);
        Call(map, "SetScale", 1.3f);
        Assert.Throws<TargetInvocationException>(() => store.GetMethod("ImportMapAssets").Invoke(null,
            new object[] { new[] { "valid.png", "empty.png" }, new[] { _texture.EncodeToPNG(), Array.Empty<byte>() } }));
        Assert.That(JsonUtility.ToJson(campaignField.GetValue(null)), Is.EqualTo(before), "Rejection must not capture live edits into stored metadata.");
        Assert.That(dirtyField.GetValue(null), Is.EqualTo(beforeDirty));
    }

    [UnityTest]
    public IEnumerator MapCatalogCorruptImageRejectsWholeBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateMapCatalogHost(); yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        CreateMapCatalogImage();
        VerifyMapCatalogCorruptImage();
    }

    private void VerifyMapCatalogCorruptImage()
    {
        var store = TypeOf("SceneFileStore");
        var field = store.GetField("_campaign", BindingFlags.NonPublic | BindingFlags.Static);
        var exception = Assert.Throws<TargetInvocationException>(() => store.GetMethod("ImportMapAssets").Invoke(null,
            new object[] { new[] { "valid.png", "corrupt.png" }, new[] { _texture.EncodeToPNG(), new byte[] { 1, 2, 3 } } }));
        Assert.That(exception.InnerException, Is.TypeOf<FormatException>());
        Assert.That(field.GetValue(null), Is.Null);
        Assert.That(((Array)store.GetMethod("GetMapAssets").Invoke(null, null)).Length, Is.Zero);
    }

    [UnityTest]
    public IEnumerator MapCatalogFileBatchAndFolderPreserveAtomicityAndPersistence()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateMapCatalogHost(); yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        CreateMapCatalogImage();
        VerifyMapCatalogFiles();
    }

    private void VerifyMapCatalogFiles()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "MapCatalogFileTest-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        _fixturePaths = new[] { Path.Combine(folder, "valid.png"), Path.Combine(folder, "corrupt.png"), Path.Combine(folder, "note.txt") };
        File.WriteAllBytes(_fixturePaths[0], _texture.EncodeToPNG());
        File.WriteAllBytes(_fixturePaths[1], new byte[] { 1, 2, 3 });
        File.WriteAllText(_fixturePaths[2], "Not a map");
        var mapType = TypeOf("MapController");
        var map = mapType.GetProperty("Instance").GetValue(null);
        var paths = (string[])mapType.GetMethod("CollectImagePathsFromFolder", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { folder });
        Assert.That(paths.Length, Is.EqualTo(2));
        Assert.That(paths, Is.EqualTo(new[] { _fixturePaths[1], _fixturePaths[0] }), "Editor folder collection is sorted and excludes text files.");
        var error = Assert.Throws<TargetInvocationException>(() => Call(map, "ImportImagePathsToLibrary", (object)paths));
        Assert.That(error.InnerException, Is.TypeOf<FormatException>());
        var store = TypeOf("SceneFileStore");
        Assert.That(store.GetProperty("HasCampaign").GetValue(null), Is.EqualTo(false));
        File.WriteAllBytes(_fixturePaths[1], _texture.EncodeToPNG());
        Assert.That(Call(map, "ImportImagePathsToLibrary", (object)paths), Is.EqualTo(2));
        var assets = (Array)store.GetMethod("GetMapAssets").Invoke(null, null);
        Assert.That(assets.Length, Is.EqualTo(2));
        string firstId = (string)assets.GetValue(0).GetType().GetField("id").GetValue(assets.GetValue(0));
        string secondId = (string)assets.GetValue(1).GetType().GetField("id").GetValue(assets.GetValue(1));
        store.GetMethod("SelectMapAsset").Invoke(null, new object[] { firstId });
        error = Assert.Throws<TargetInvocationException>(() => store.GetMethod("DeleteMapAsset").Invoke(null, new object[] { firstId }));
        Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(((Array)store.GetMethod("GetMapAssets").Invoke(null, null)).Length, Is.EqualTo(2));
        store.GetMethod("DeleteMapAsset").Invoke(null, new object[] { secondId });
        string expected = JsonUtility.ToJson(store.GetMethod("CaptureCampaign").Invoke(null, new object[] { null }));
        _path = Path.Combine(folder, "campaign.json");
        store.GetMethod("SaveCampaign").Invoke(null, new object[] { _path, null });
        store.GetMethod("LoadCampaign").Invoke(null, new object[] { _path });
        Assert.That(JsonUtility.ToJson(store.GetMethod("CaptureCampaign").Invoke(null, new object[] { null })), Is.EqualTo(expected));
        Assert.That(((Array)store.GetMethod("GetMapAssets").Invoke(null, null)).Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator MapCatalogDuplicateNamesAndCountLimitsAreTransactional()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateMapCatalogHost(); yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        CreateMapCatalogImage();
        VerifyMapCatalogNamesAndLimits();
    }

    private void VerifyMapCatalogNamesAndLimits()
    {
        var store = TypeOf("SceneFileStore");
        var import = store.GetMethod("ImportMapAssets");
        byte[] png = _texture.EncodeToPNG();
        Assert.That(import.Invoke(null, new object[] { new[] { "Forest.png", "forest.jpg", "Forest.png" }, new[] { png, png, png } }), Is.EqualTo(3));
        var assets = (Array)store.GetMethod("GetMapAssets").Invoke(null, null);
        Assert.That(assets.GetValue(0).GetType().GetField("name").GetValue(assets.GetValue(0)), Is.EqualTo("Forest"));
        Assert.That(assets.GetValue(1).GetType().GetField("name").GetValue(assets.GetValue(1)), Is.EqualTo("forest (2)"));
        Assert.That(assets.GetValue(2).GetType().GetField("name").GetValue(assets.GetValue(2)), Is.EqualTo("Forest (3)"));
        var field = store.GetField("_campaign", BindingFlags.NonPublic | BindingFlags.Static);
        string before = JsonUtility.ToJson(field.GetValue(null));
        Assert.Throws<TargetInvocationException>(() => import.Invoke(null, new object[] { new string[33], new byte[33][] }));
        Assert.That(JsonUtility.ToJson(field.GetValue(null)), Is.EqualTo(before));
        for (int offset = 3; offset < 256;)
        {
            int count = Math.Min(32, 256 - offset);
            var names = new string[count]; var images = new byte[count][];
            for (int i = 0; i < count; i++) { names[i] = "capacity-" + (offset + i) + ".png"; images[i] = png; }
            Assert.That(import.Invoke(null, new object[] { names, images }), Is.EqualTo(count));
            offset += count;
        }
        Assert.That(((Array)store.GetMethod("GetMapAssets").Invoke(null, null)).Length, Is.EqualTo(256));
        before = JsonUtility.ToJson(field.GetValue(null));
        var error = Assert.Throws<TargetInvocationException>(() => import.Invoke(null, new object[] { new[] { "overflow.png" }, new[] { png } }));
        Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        Assert.That(JsonUtility.ToJson(field.GetValue(null)), Is.EqualTo(before));
    }

    [UnityTest]
    public IEnumerator MapCatalogPreparedByteBudgetRejectsEntireBatch()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateMapCatalogHost(); yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        CreateMapCatalogImage();
        VerifyMapCatalogByteBudget();
    }

    private void VerifyMapCatalogByteBudget()
    {
        UnityEngine.Object.Destroy(_texture);
        _texture = new Texture2D(2048, 2048, TextureFormat.RGB24, false);
        var raw = new byte[2048 * 2048 * 3]; new System.Random(73812).NextBytes(raw);
        _texture.LoadRawTextureData(raw); _texture.Apply();
        byte[] png = _texture.EncodeToPNG();
        Assert.That(png.Length, Is.LessThanOrEqualTo(16 * 1024 * 1024));
        Assert.That(png.LongLength * 8, Is.GreaterThan(96L * 1024 * 1024));
        var names = new string[8]; var images = new byte[8][];
        for (int i = 0; i < 8; i++) { names[i] = "large-" + i + ".png"; images[i] = png; }
        var store = TypeOf("SceneFileStore");
        var error = Assert.Throws<TargetInvocationException>(() => store.GetMethod("ImportMapAssets").Invoke(null, new object[] { names, images }));
        Assert.That(error.InnerException, Is.TypeOf<FormatException>());
        Assert.That(error.InnerException.Message, Does.Contain("96 МБ"));
        Assert.That(store.GetProperty("HasCampaign").GetValue(null), Is.EqualTo(false));
    }

    [UnityTest]
    public IEnumerator MapCatalogAddingCurrentMapRejectsWithoutLosingExistingLink()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        CreateMapCatalogHost(); yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        CreateMapCatalogImage();
        VerifyAddingCurrentMapAtomicity();
    }

    private void VerifyAddingCurrentMapAtomicity()
    {
        var store = TypeOf("SceneFileStore");
        store.GetMethod("AddCurrentMapAsset").Invoke(null, new object[] { "Original" });
        _path = Path.Combine(Application.temporaryCachePath, "current-map-test-" + Guid.NewGuid().ToString("N") + ".json");
        store.GetMethod("SaveCampaign").Invoke(null, new object[] { _path, null });
        var campaignField = store.GetField("_campaign", BindingFlags.NonPublic | BindingFlags.Static);
        var linkField = store.GetField("_currentMapAssetId", BindingFlags.NonPublic | BindingFlags.Static);
        var dirtyField = store.GetField("_campaignDirty", BindingFlags.NonPublic | BindingFlags.Static);
        string before = JsonUtility.ToJson(campaignField.GetValue(null));
        string originalLink = (string)linkField.GetValue(null);
        Assert.That(originalLink, Is.Not.Null.And.Not.Empty);
        Assert.That(dirtyField.GetValue(null), Is.EqualTo(false));
        var grid = _root.GetComponentInChildren(TypeOf("GridManager"));
        var widthField = grid.GetType().GetField("gridWidth");
        widthField.SetValue(grid, 300); // Force scene validation failure after name validation.
        try
        {
            Assert.Throws<TargetInvocationException>(() => store.GetMethod("AddCurrentMapAsset").Invoke(null, new object[] { "Rejected" }));
            Assert.That(linkField.GetValue(null), Is.EqualTo(originalLink), "Rejected Add Current must preserve the previous asset link.");
            Assert.That(JsonUtility.ToJson(campaignField.GetValue(null)), Is.EqualTo(before));
            Assert.That(dirtyField.GetValue(null), Is.EqualTo(false));
        }
        finally { widthField.SetValue(grid, 10); }
    }

    [UnityTest]
    public IEnumerator CampaignSceneCrudPreservesCopiesAndClearsIncomingTransitions()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        _root = new GameObject("Campaign CRUD test");
        _manager = new GameObject("Campaign CRUD host").AddComponent<NetworkManager>();
        var transport = _manager.gameObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17879);
        _manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _manager.AddNetworkPrefab(prefab);
        var grid = Add("GridManager", "Grid");
        grid.GetType().GetField("gridWidth").SetValue(grid, 10);
        grid.GetType().GetField("gridHeight").SetValue(grid, 10);
        var tokens = Add("TokenManager", "Tokens");
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        var mapObject = new GameObject("Map"); mapObject.transform.SetParent(_root.transform);
        mapObject.AddComponent<NetworkObject>();
        var map = mapObject.AddComponent(TypeOf("MapController"));
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.transform.SetParent(mapObject.transform, false);
        map.GetType().GetField("mapPlane").SetValue(map, plane);
        var sync = mapObject.AddComponent(TypeOf("MapSync")); sync.GetType().GetField("mapController").SetValue(sync, map);
        Add("SceneEditor", "Editor"); Add("FogManager", "Fog");
        yield return null;
        Assert.That(_manager.StartHost(), Is.True); yield return null;
        _texture = new Texture2D(4, 4); _texture.SetPixels(new Color[16]); _texture.Apply();
        Call(map, "ApplyImage", _texture.EncodeToPNG());
        Call(tokens, "CreateTokenAsHost", "Original NPC", true);
        var store = TypeOf("SceneFileStore");
        store.GetMethod("EnsureCampaign").Invoke(null, null);
        string originalId = (string)store.GetProperty("ActiveSceneId").GetValue(null);
        store.GetMethod("RenameActiveScene").Invoke(null, new object[] { "  Original  " });
        var scenes = (Array)store.GetMethod("GetCampaignScenes").Invoke(null, null);
        Assert.That(scenes.Length, Is.EqualTo(1));
        Assert.That(scenes.GetValue(0).GetType().GetField("title").GetValue(scenes.GetValue(0)), Is.EqualTo("Original"));
        store.GetMethod("CreateSceneCopy").Invoke(null, null); yield return null;
        string copyId = (string)store.GetProperty("ActiveSceneId").GetValue(null);
        Assert.That(copyId, Is.Not.EqualTo(originalId));
        scenes = (Array)store.GetMethod("GetCampaignScenes").Invoke(null, null);
        Assert.That(scenes.Length, Is.EqualTo(2));
        var copy = scenes.GetValue(1); var copiedScene = copy.GetType().GetField("scene").GetValue(copy);
        Assert.That(((Array)copiedScene.GetType().GetField("tokens").GetValue(copiedScene)).Length, Is.EqualTo(1));
        Assert.That(((Array)copiedScene.GetType().GetField("transitions").GetValue(copiedScene)).Length, Is.Zero);
        store.GetMethod("RenameActiveScene").Invoke(null, new object[] { "Copy" });
        Assert.That(scenes.GetValue(0).GetType().GetField("title").GetValue(scenes.GetValue(0)), Is.EqualTo("Original"));
        store.GetMethod("CreateEmptyScene").Invoke(null, null); yield return null;
        string emptyId = (string)store.GetProperty("ActiveSceneId").GetValue(null);
        scenes = (Array)store.GetMethod("GetCampaignScenes").Invoke(null, null);
        Assert.That(scenes.Length, Is.EqualTo(3));
        var empty = scenes.GetValue(2); var emptyScene = empty.GetType().GetField("scene").GetValue(empty);
        Assert.That(((Array)emptyScene.GetType().GetField("tokens").GetValue(emptyScene)).Length, Is.Zero);
        store.GetMethod("CycleScene").Invoke(null, new object[] { 1 }); yield return null;
        Assert.That(store.GetProperty("ActiveSceneId").GetValue(null), Is.EqualTo(originalId), "Forward wrap selects original.");
        store.GetMethod("AddSceneTransition").Invoke(null, new object[] { "To empty", emptyId, 1, 1 });
        store.GetMethod("CycleScene").Invoke(null, new object[] { -1 }); yield return null;
        Assert.That(store.GetProperty("ActiveSceneId").GetValue(null), Is.EqualTo(emptyId), "Backward wrap selects last.");
        store.GetMethod("DeleteActiveScene").Invoke(null, null); yield return null;
        Assert.That(store.GetProperty("ActiveSceneId").GetValue(null), Is.EqualTo(originalId));
        Assert.That(((Array)store.GetMethod("GetActiveSceneTransitions").Invoke(null, null)).Length, Is.Zero);
        store.GetMethod("DeleteActiveScene").Invoke(null, null); yield return null;
        Assert.That(store.GetProperty("ActiveSceneId").GetValue(null), Is.EqualTo(copyId));
        store.GetMethod("DeleteActiveScene").Invoke(null, null); yield return null;
        Assert.That(((Array)store.GetMethod("GetCampaignScenes").Invoke(null, null)).Length, Is.EqualTo(1));
        Assert.That(store.GetProperty("ActiveSceneId").GetValue(null), Is.EqualTo(copyId), "Last scene cannot be deleted.");
        _path = Path.Combine(Application.temporaryCachePath, "campaign-crud-test.json");
        store.GetMethod("SaveCampaign").Invoke(null, new object[] { _path, "CRUD test" });
        store.GetMethod("LoadCampaign").Invoke(null, new object[] { _path }); yield return null;
        Assert.That(store.GetProperty("ActiveSceneId").GetValue(null), Is.EqualTo(copyId));
        Assert.That(((Array)store.GetMethod("GetCampaignScenes").Invoke(null, null)).Length, Is.EqualTo(1));
    }

    [UnityTest]
    public IEnumerator PortraitRepairRequiresANewAcknowledgementAndPreservesImage()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        _root = new GameObject("Portrait repair test");
        _manager = new GameObject("Portrait repair host").AddComponent<NetworkManager>();
        var transport = _manager.gameObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17879);
        _manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _manager.AddNetworkPrefab(prefab);
        var grid = Add("GridManager", "Grid");
        grid.GetType().GetField("gridWidth").SetValue(grid, 10);
        grid.GetType().GetField("gridHeight").SetValue(grid, 10);
        var tokens = Add("TokenManager", "Tokens");
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        yield return null;
        Assert.That(_manager.StartHost(), Is.True);
        yield return null;
        var token = Call(tokens, "CreateTokenAsHost", "Portrait repair", true);
        Assert.That(token, Is.Not.Null);
        _texture = new Texture2D(4, 4);
        _texture.SetPixels(new Color[16]); _texture.Apply();
        byte[] image = _texture.EncodeToPNG();
        var type = TypeOf("TokenImageSync");
        ulong id = Read<ulong>(token, "NetworkObjectId");
        Assert.That(type.GetMethod("BroadcastImage").Invoke(null, new object[] { id, image }), Is.EqualTo(true));
        var versions = (System.Collections.Generic.Dictionary<ulong, int>)type.GetField("PortraitVersions", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var cache = (System.Collections.Generic.Dictionary<ulong, byte[]>)type.GetField("PortraitCache", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        var sync = type.GetProperty("Instance").GetValue(null);
        var acks = (System.Collections.Generic.Dictionary<(ulong, ulong), int>)type.GetField("_downloadAcks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(sync);
        int oldVersion = versions[id];
        acks[(42UL, id)] = oldVersion;
        byte[] previousPortrait = (byte[])Call(token, "GetPortraitJpg");
        Assert.That(type.GetMethod("RestartPortraitForRepair").Invoke(null, new object[] { id }), Is.EqualTo(true));
        Assert.That(versions[id], Is.EqualTo(oldVersion + 1));
        Assert.That(cache[id], Is.SameAs(image), "Repair reuses the validated image bytes.");
        Assert.That((byte[])Call(token, "GetPortraitJpg"), Is.EqualTo(previousPortrait));
        Assert.That(acks[(42UL, id)], Is.Not.EqualTo(versions[id]), "The old object's ACK cannot satisfy the repaired portrait revision.");
        acks.Remove((42UL, id));
        using var writer = new FastBufferWriter(sizeof(ulong) + sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(id); writer.WriteValueSafe(oldVersion);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        type.GetMethod("OnDownloadAck", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(sync, new object[] { 42UL, reader });
        Assert.That(acks.ContainsKey((42UL, id)), Is.False, "A delayed old ACK must be rejected after repair.");
        Assert.That(type.GetMethod("RestartPortraitForRepair").Invoke(null, new object[] { ulong.MaxValue }), Is.EqualTo(false));
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
        VerifyStatBlockAssignment(token);
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
        Assert.That((bool)restoredMaster.GetType().GetField("hideArmorClass").GetValue(restoredMaster), Is.True);
        Assert.That(Read<int>(token, "NetworkVisibleCurrentHp"), Is.EqualTo(-1));
        Call(token, "ServerToggleCondition", "poisoned");
        Call(fog, "TogglePreview");
        Assert.That(Read<int>(token, "DisplayedCurrentHp"), Is.EqualTo(-1), "Player preview must hide private HP.");
        Assert.That(Read<int>(token, "DisplayedMaxHp"), Is.EqualTo(-1));
        Assert.That(Read<int>(token, "DisplayedArmorClass"), Is.EqualTo(-1), "Player preview must hide private AC.");
        Assert.That(Read<bool>(token, "CanEditMasterStats"), Is.False, "Preview menu must not render master editing controls.");
        Assert.That((string)Call(token, "GetDisplayedTokenInfo"), Does.Not.Contain("КД:"));
        Assert.That((string)Call(token, "GetDisplayedTokenInfo"), Does.Not.Contain("ХП:"));
        Assert.That((string)Call(token, "GetDisplayedTokenInfo"), Does.Not.Contain("Отравлен"));
        Assert.That(Read<string[]>(token, "VisibleConditionIds"), Is.Empty, "Player preview must hide private conditions.");
        Call(fog, "TogglePreview");
        Assert.That(Read<bool>(token, "CanEditMasterStats"), Is.True);
        Assert.That((string)Call(token, "GetDisplayedTokenInfo"), Does.Contain("КД: 10"));
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
        var store = TypeOf("SceneFileStore");
        string campaignBeforeInvalidLoad = JsonUtility.ToJson(store.GetMethod("CaptureCampaign").Invoke(null, new object[] { null }));
        bool dirtyBeforeInvalidLoad = (bool)store.GetMethod("HasUnsavedChanges").Invoke(null, null);
        Assert.That(store.GetProperty("HasCampaign").GetValue(null), Is.EqualTo(true));
        var broken = JsonUtility.FromJson(savedJson, TypeOf("SceneDefinition"));
        broken.GetType().GetField("mapImage").SetValue(broken, "invalid-base64");
        File.WriteAllText(_path, JsonUtility.ToJson(broken));
        Assert.Throws<TargetInvocationException>(() => TypeOf("SceneFileStore").GetMethod("Load").Invoke(null, new object[] { _path }));
        Assert.That(Read<bool>(token, "IsSpawned"), Is.True, "Invalid import must not delete existing tokens.");
        Assert.That(Read<bool>(token, "IsHidden"), Is.False, "Invalid import must preserve the current state.");
        Assert.That(store.GetProperty("HasCampaign").GetValue(null), Is.EqualTo(true), "Invalid scene must not discard the campaign.");
        Assert.That(JsonUtility.ToJson(store.GetMethod("CaptureCampaign").Invoke(null, new object[] { null })),
            Is.EqualTo(campaignBeforeInvalidLoad), "Campaign metadata and libraries must remain intact.");
        Assert.That(store.GetMethod("HasUnsavedChanges").Invoke(null, null), Is.EqualTo(dirtyBeforeInvalidLoad));
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
        var block = JsonUtility.FromJson("{\"id\":\"restore-public-block\",\"name\":\"Public name\",\"size\":\"\",\"creatureType\":\"\",\"alignment\":\"\",\"speed\":\"\",\"challengeRating\":\"\",\"description\":\"Master secret\",\"actions\":[],\"publicFieldsMask\":1}", TypeOf("StatBlockDefinition"));
        TypeOf("SceneFileStore").GetMethod("UpsertStatBlock").Invoke(null, new[] { block });
        var statToken = Call(tokenManager, "CreateTokenAsHost", "Stat block restore", false);
        var master = Call(statToken, "CaptureMasterData");
        master.GetType().GetField("statBlockId").SetValue(master, "restore-public-block");
        Call(statToken, "ServerApplyMasterData", master);
        string publicBlock = Read<string>(statToken, "PublicStatBlockJson");
        Assert.That(publicBlock, Does.Contain("Public name"), "Restoring master data must publish permitted stat block fields.");
        Assert.That(publicBlock, Does.Not.Contain("Master secret"));
    }

    [UnityTest]
    public IEnumerator StationaryPointerKeepsTokenInfoVisibleAndClearsWhenItLeaves()
    {
        yield return new EnterPlayMode();
        _root = new GameObject("Hover test");
        var networkObject = new GameObject("Hover network");
        _manager = networkObject.AddComponent<NetworkManager>();
        var transport = networkObject.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17882);
        _manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _manager.AddNetworkPrefab(prefab);
        var tokens = Add("TokenManager", "Tokens");
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        foreach (var existingCamera in Camera.allCameras) existingCamera.enabled = false;
        var cameraObject = new GameObject("Hover camera", typeof(Camera));
        cameraObject.transform.SetParent(_root.transform);
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.transform.position = new Vector3(0, 10, 0);
        camera.transform.rotation = Quaternion.Euler(90, 0, 0);
        yield return null;
        Assert.That(_manager.StartHost(), Is.True);
        yield return null;
        var token = (Component)Call(tokens, "CreateTokenAsHost", "Наведение", false);
        var previousMouse = UnityEngine.InputSystem.Mouse.current;
        var mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>();
        mouse.MakeCurrent();
        try
        {
            Vector3 point = camera.WorldToScreenPoint(token.transform.position);
            UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position, new Vector2(point.x, point.y));
            var hovered = token.GetType().GetField("_isHovered", BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < 6; i++)
            {
                yield return null;
                var currentMouse = UnityEngine.InputSystem.Mouse.current;
                var selectedToken = token.GetType().GetField("_pointerToken", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                Assert.That(hovered.GetValue(token), Is.True,
                    $"Stationary hover frame {i}: mouse={currentMouse?.deviceId}/{mouse.deviceId}, position={currentMouse?.position.ReadValue()}, expected={point}, camera={Camera.main?.name}, rect={camera.pixelRect}, selected={selectedToken}, allowed={TypeOf("GameplayInputGate").GetProperty("AllowsWorldPointerInput").GetValue(null)}, spawned={token.GetComponent<NetworkObject>().IsSpawned}");
                Assert.That(Call(token, "GetDisplayedTokenInfo"), Does.Contain("Наведение"));
            }
            UnityEngine.InputSystem.LowLevel.InputState.Change(mouse.position, new Vector2(-100, -100));
            yield return null;
            Assert.That(hovered.GetValue(token), Is.False);
        }
        finally
        {
            UnityEngine.InputSystem.InputSystem.RemoveDevice(mouse);
            previousMouse?.MakeCurrent();
        }
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
        foreach (string path in _fixturePaths) if (File.Exists(path)) File.Delete(path);
        yield return null;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    private static void VerifyStatBlockAssignment(object token)
    {
        var original = Call(token, "CaptureMasterData");
        var block = Activator.CreateInstance(TypeOf("StatBlockDefinition"));
        block.GetType().GetField("id").SetValue(block, "assign-a");
        block.GetType().GetField("name").SetValue(block, "Assignment A");
        block.GetType().GetField("hitPoints").SetValue(block, 42);
        block.GetType().GetField("armorClass").SetValue(block, 16);
        foreach (string field in new[] { "size", "creatureType", "alignment", "speed", "challengeRating", "description" })
            block.GetType().GetField(field).SetValue(block, "");
        TypeOf("SceneFileStore").GetMethod("UpsertStatBlock").Invoke(null, new[] { block });
        Assert.That(Call(token, "ServerSetStatBlock", "assign-a"), Is.EqualTo(true));
        Assert.That(Read<int>(token, "VisibleCurrentHp"), Is.EqualTo(42));
        Assert.That(Read<int>(token, "VisibleMaxHp"), Is.EqualTo(42));
        Assert.That(Read<int>(token, "ArmorClass"), Is.EqualTo(16));
        Call(token, "ServerSetHealth", 9, 42, false);
        Call(token, "ServerSetStatBlock", "assign-a");
        Assert.That(Read<int>(token, "VisibleCurrentHp"), Is.EqualTo(9), "Reassignment must not heal.");
        block.GetType().GetField("hitPoints").SetValue(block, 80);
        block.GetType().GetField("armorClass").SetValue(block, 22);
        TypeOf("SceneFileStore").GetMethod("UpsertStatBlock").Invoke(null, new[] { block });
        Assert.That(Read<int>(token, "VisibleCurrentHp"), Is.EqualTo(9), "Editing a template must not reset live HP.");
        Assert.That(Read<int>(token, "VisibleMaxHp"), Is.EqualTo(42));
        Assert.That(Read<int>(token, "ArmorClass"), Is.EqualTo(16));
        block = JsonUtility.FromJson(JsonUtility.ToJson(block), TypeOf("StatBlockDefinition"));
        block.GetType().GetField("id").SetValue(block, "assign-b");
        TypeOf("SceneFileStore").GetMethod("UpsertStatBlock").Invoke(null, new[] { block });
        Call(token, "ServerToggleCondition", "poisoned");
        var before = Call(token, "CaptureMasterData");
        Assert.That(Call(token, "ServerSetStatBlock", "assign-b"), Is.EqualTo(true));
        Assert.That(Read<int>(token, "VisibleCurrentHp"), Is.EqualTo(80));
        Assert.That(Read<int>(token, "ArmorClass"), Is.EqualTo(22));
        var assigned = Call(token, "CaptureMasterData");
        foreach (string field in new[] { "hideHp", "hideArmorClass", "hideConditions" })
            Assert.That(assigned.GetType().GetField(field).GetValue(assigned), Is.EqualTo(before.GetType().GetField(field).GetValue(before)));
        Assert.That(Read<string[]>(token, "VisibleConditionIds"), Does.Contain("poisoned"));
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        Assert.That(JsonUtility.ToJson(Call(token, "CaptureMasterData")), Is.EqualTo(JsonUtility.ToJson(before)));
        Assert.That(token.GetType().GetField("_masterCurrentHpInput", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(token), Is.EqualTo("9"));
        Assert.That(Call(token, "ServerSetStatBlock", "missing-template"), Is.EqualTo(false));
        Assert.That(JsonUtility.ToJson(Call(token, "CaptureMasterData")), Is.EqualTo(JsonUtility.ToJson(before)));
        Assert.That(Call(token, "ServerSetStatBlock", ""), Is.EqualTo(true));
        Assert.That(Read<int>(token, "VisibleCurrentHp"), Is.EqualTo(9), "Detaching the template keeps combat values.");
        TypeOf("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        Assert.That(JsonUtility.ToJson(Call(token, "CaptureMasterData")), Is.EqualTo(JsonUtility.ToJson(before)));
        Call(token, "ServerApplyMasterData", original);
        TypeOf("GameMasterUndo").GetMethod("Clear").Invoke(null, null);
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
