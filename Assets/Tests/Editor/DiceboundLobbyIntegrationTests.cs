using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.TestTools;
using System.Threading.Tasks;
using System.IO;
using System.IO.Compression;

public class DiceboundLobbyIntegrationTests
{
    private NetworkManager _host, _client;
    private int _funReceived, _funType;
    private ulong _funTarget;
    private Vector3 _funPoint, _funRgb;
    private string _funText;
    private void SendFunRequest(int type, Vector3 point, int epoch = -1, ulong target = ulong.MaxValue, string text = "")
    {
        using var writer = new FastBufferWriter(512, Allocator.Temp);
        writer.WriteValueSafe((byte)type); writer.WriteValueSafe(epoch);
        writer.WriteValueSafe(target); writer.WriteValueSafe(point);
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);
        writer.WriteValueSafe((ushort)bytes.Length); writer.WriteBytesSafe(bytes);
        _client.CustomMessagingManager.SendNamedMessage("FunRequestV2", NetworkManager.ServerClientId, writer);
    }
    private Component _registry;
    private string _reply;
    private int _heartbeatReceived;
    private string _initiativeReceived;
    private object _originalCampaign;
    private GameObject _scene;
    private Texture2D _mapTexture;
    private int _transferTotal, _transferReceived, _fogChanges;
    private byte[] _incomingImage;
    private bool? _fogEnabled;
    private bool? _curtainReceived, _syncPendingReceived;
    private static Type RuntimeType(string name) => Type.GetType(name + ", Assembly-CSharp", true);
    private NetworkManager Manager(string name)
    {
        var go = new GameObject(name);
        var manager = go.AddComponent<NetworkManager>();
        var transport = go.AddComponent<UnityTransport>();
        transport.SetConnectionData("127.0.0.1", 17881);
        manager.NetworkConfig = new NetworkConfig { EnableSceneManagement = false, NetworkTransport = transport };
        return manager;
    }

    [TestCase(29.9f, false)]
    [TestCase(30f, true)]
    [TestCase(90f, true)]
    public void ServerHeartbeatExpiresAfterThirtySeconds(float elapsed, bool expired)
    {
        var method = RuntimeType("GameNetworkManager").GetMethod("HeartbeatExpired", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.That(method.Invoke(null, new object[] { 100f + elapsed, 100f }), Is.EqualTo(expired));
    }

    [UnityTest]
    public IEnumerator ServerRepliesToConnectedClientHeartbeat()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        _host = Manager("Heartbeat host");
        _host.gameObject.AddComponent(RuntimeType("GameNetworkManager"));
        Assert.That(_host.StartHost(), Is.True);
        _client = Manager("Heartbeat client");
        Assert.That(_client.StartClient(), Is.True);
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!_client.IsConnectedClient && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True);
        Assert.That(_client.CustomMessagingManager, Is.Not.Null);
        _heartbeatReceived = -1;
        _client.CustomMessagingManager.RegisterNamedMessageHandler("SessionHeartbeatReplyV1", (sender, reader) =>
        {
            Assert.That(sender, Is.EqualTo(NetworkManager.ServerClientId));
            reader.ReadValueSafe(out _heartbeatReceived);
        });
        using (var writer = new FastBufferWriter(sizeof(int), Allocator.Temp))
        {
            writer.WriteValueSafe(42);
            _client.CustomMessagingManager.SendNamedMessage("SessionHeartbeatRequestV1", NetworkManager.ServerClientId, writer);
        }
        deadline = Time.realtimeSinceStartup + 5f;
        while (_heartbeatReceived < 0 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_heartbeatReceived, Is.EqualTo(42));
    }

    private IEnumerator RegisterRemote(string nickname)
    {
        _reply = null;
        _client = Manager("Lobby regression client");
        Assert.That(_client.StartClient(), Is.True);
        for (int i = 0; i < 100 && !_client.IsConnectedClient; i++) yield return new WaitForSecondsRealtime(0.05f);
        Assert.That(_client.IsConnectedClient, Is.True, "The local transport must establish an actual client connection.");
        _client.CustomMessagingManager.RegisterNamedMessageHandler("PlayerSyncAll", (sender, reader) => {
            reader.ReadValueSafe(out int count);
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out ulong id); reader.ReadValueSafe(out FixedString64Bytes name);
                reader.ReadValueSafe(out float r); reader.ReadValueSafe(out float g); reader.ReadValueSafe(out float b);
                if (id == _client.LocalClientId) _reply = name.ToString();
            }
        });
        using (var writer = new FastBufferWriter(64, Allocator.Temp))
        {
            writer.WriteValueSafe(new FixedString64Bytes(nickname));
            _client.CustomMessagingManager.SendNamedMessage("PlayerRegister", NetworkManager.ServerClientId, writer);
        }
        for (int i = 0; i < 100 && _reply == null; i++) yield return new WaitForSecondsRealtime(0.05f);
        Assert.That(_reply, Is.EqualTo(nickname), "The host must answer registration through the client's real message channel.");
        _client.Shutdown();
        for (int i = 0; i < 100 && _client.ShutdownInProgress; i++) yield return null;
        UnityEngine.Object.Destroy(_client.gameObject); _client = null;
        yield return null;
    }

    [UnityTest]
    public IEnumerator UntouchedGeometryHasPositiveRevisionAndRecoversRepeatedSnapshot()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        _host = Manager("Fresh geometry host");
        _scene = new GameObject("Fresh geometry scene");
        var grid = _scene.AddComponent(RuntimeType("GridManager"));
        var editorType = RuntimeType("SceneEditor");
        var serverGo = new GameObject("Server geometry"); serverGo.transform.SetParent(_scene.transform);
        var server = serverGo.AddComponent(editorType); ((Behaviour)server).enabled = false;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        editorType.GetField("_grid", flags).SetValue(server, grid);
        Assert.That(_host.StartHost(), Is.True);
        editorType.GetMethod("Register", flags).Invoke(server, new object[] { _host });
        _client = Manager("Fresh geometry client");
        Assert.That(_client.StartClient(), Is.True);
        var clientGo = new GameObject("Client geometry"); clientGo.transform.SetParent(_scene.transform);
        var client = clientGo.AddComponent(editorType); ((Behaviour)client).enabled = false;
        editorType.GetField("_grid", flags).SetValue(client, grid);
        editorType.GetMethod("Register", flags).Invoke(client, new object[] { _client });
        float deadline = Time.realtimeSinceStartup + 10;
        while (!_client.IsConnectedClient && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True);
        deadline = Time.realtimeSinceStartup + 5;
        while ((int)editorType.GetField("_receivedRevision", flags).GetValue(client) < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(editorType.GetField("_receivedRevision", flags).GetValue(client), Is.EqualTo(1),
            "Untouched geometry must already have a positive revision without Publish or drawing walls.");
        editorType.GetMethod("LateUpdate", flags).Invoke(client, null);
        deadline = Time.realtimeSinceStartup + 5;
        while (!(bool)editorType.GetMethod("HasClientCurrentRevision").Invoke(server, new object[] { _client.LocalClientId }) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(editorType.GetMethod("HasClientCurrentRevision").Invoke(server, new object[] { _client.LocalClientId }), Is.True);
        ((System.Collections.IDictionary)editorType.GetField("_clientAcks", flags).GetValue(server)).Clear();
        editorType.GetMethod("ResendStateToClient").Invoke(server, new object[] { _client.LocalClientId });
        deadline = Time.realtimeSinceStartup + 5;
        while ((int)editorType.GetField("_pendingRevisionAck", flags).GetValue(client) < 1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(editorType.GetField("_pendingRevisionAck", flags).GetValue(client), Is.EqualTo(1), "Retrying the same snapshot must produce another acknowledgement.");
        editorType.GetMethod("LateUpdate", flags).Invoke(client, null);
        deadline = Time.realtimeSinceStartup + 5;
        while (!(bool)editorType.GetMethod("HasClientCurrentRevision").Invoke(server, new object[] { _client.LocalClientId }) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(editorType.GetMethod("HasClientCurrentRevision").Invoke(server, new object[] { _client.LocalClientId }), Is.True);
    }

    [UnityTest]
    public IEnumerator FunEventsUseServerColorAndRejectInvalidTargetsAndSpam()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        _host = Manager("Fun host");
        _registry = _host.gameObject.AddComponent(RuntimeType("PlayerRegistry"));
        var effects = _host.gameObject.AddComponent(RuntimeType("FunEffects"));
        _scene = new GameObject("Fun map");
        _scene.AddComponent(RuntimeType("GridManager"));
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _host.AddNetworkPrefab(prefab);
        var tokens = _scene.AddComponent(RuntimeType("TokenManager"));
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        _client = Manager("Fun client");
        _client.AddNetworkPrefab(prefab);
        Assert.That(_client.StartClient(), Is.True);
        float connectionDeadline = Time.realtimeSinceStartup + 10;
        while (!_client.IsConnectedClient && Time.realtimeSinceStartup < connectionDeadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True);
        _registry.GetType().GetMethod("RegisterPlayerOnServer", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(_registry, new object[] { _client.LocalClientId, "Fun player" });
        yield return null;
        _funReceived = 0; _funType = -1;
        _funPoint = _funRgb = default;
        Assert.That(_client, Is.Not.Null, "Fun client must still exist after registration.");
        Assert.That(_client.CustomMessagingManager, Is.Not.Null, "Fun client message channel must remain active.");
        _client.CustomMessagingManager.RegisterNamedMessageHandler("FunShowV2", (sender, reader) =>
        {
            Assert.That(sender, Is.EqualTo(NetworkManager.ServerClientId));
            reader.ReadValueSafe(out byte type); reader.ReadValueSafe(out int epoch);
            reader.ReadValueSafe(out _funTarget); reader.ReadValueSafe(out _funPoint); reader.ReadValueSafe(out _funRgb);
            reader.ReadValueSafe(out ushort textLength);
            byte[] textBytes = new byte[textLength]; reader.ReadBytesSafe(ref textBytes, textLength);
            _funText = System.Text.Encoding.UTF8.GetString(textBytes);
            Assert.That(epoch, Is.EqualTo(-1));
            _funReceived++; _funType = type;
        });
        Assert.That(_registry, Is.Not.Null, "Fun registry must remain active.");
        var colorMethod = RuntimeType("PlayerRegistry").GetMethod("GetServerPlayerColor", BindingFlags.Public | BindingFlags.Static);
        Assert.That(colorMethod, Is.Not.Null);
        var expected = (Color)colorMethod.Invoke(null, new object[] { _client.LocalClientId });
        for (int type = 0; type < 3; type++)
        {
            var point = new Vector3(1, 0, 1);
            SendFunRequest(type, point, text: type == 2 ? "  свой текст\nштампа  " : "");
            float deadline = Time.realtimeSinceStartup + 5;
            while (_funReceived < type + 1 && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_funReceived, Is.EqualTo(type + 1));
            Assert.That(_funType, Is.EqualTo(type));
            Assert.That(_funTarget, Is.EqualTo(ulong.MaxValue));
            Assert.That(_funPoint, Is.EqualTo(point));
            Assert.That(_funRgb, Is.EqualTo(new Vector3(expected.r, expected.g, expected.b)));
            Assert.That(_funText, Is.EqualTo(type == 2 ? "свой текст штампа" : ""));
            SendFunRequest(type, point, text: type == 2 ? "повтор" : "");
            yield return new WaitForSecondsRealtime(.75f);
            Assert.That(_funReceived, Is.EqualTo(type + 1), "A repeated immediate request must be rate-limited.");
        }
        SendFunRequest(99, new Vector3(1, 0, 1));
        SendFunRequest(0, new Vector3(float.NaN, 0, 1));
        SendFunRequest(0, new Vector3(10000, 0, 10000));
        SendFunRequest(0, new Vector3(1, 0, 1), 999);
        SendFunRequest(2, new Vector3(1, 0, 1), text: "   ");
        SendFunRequest(2, new Vector3(1, 0, 1), text: new string('Я', 81));
        SendFunRequest(3, new Vector3(1, 0, 1), text: "removed preset");
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(_funReceived, Is.EqualTo(3));
        var reactions = (IList)effects.GetType().GetField("_reactions", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(effects);
        Assert.That(reactions.Count, Is.EqualTo(3));
        var token = (Component)tokens.GetType().GetMethod("CreateTokenAsHost").Invoke(tokens, new object[] { "Fun target", false });
        ulong tokenId = token.GetComponent<NetworkObject>().NetworkObjectId;
        SendFunRequest(0, token.transform.position + Vector3.up * .2f, target: tokenId);
        float tokenDeadline = Time.realtimeSinceStartup + 5;
        while (_funReceived < 4 && Time.realtimeSinceStartup < tokenDeadline) yield return null;
        Assert.That(_funReceived, Is.EqualTo(4));
        Assert.That(_funTarget, Is.EqualTo(tokenId));
        Assert.That(_funPoint, Is.EqualTo(token.transform.InverseTransformPoint(token.transform.position + Vector3.up * .2f)),
            "Token hits must transmit a local anchor so traces follow token movement.");
        yield return new WaitForSecondsRealtime(.75f);
        SendFunRequest(0, token.transform.position + Vector3.right * 100, target: tokenId);
        var hidden = (Component)tokens.GetType().GetMethod("CreateTokenAsHost").Invoke(tokens, new object[] { "Hidden target", true });
        SendFunRequest(0, hidden.transform.position, target: hidden.GetComponent<NetworkObject>().NetworkObjectId);
        SendFunRequest(0, token.transform.position, target: 100000UL);
        yield return new WaitForSecondsRealtime(.3f);
        Assert.That(_funReceived, Is.EqualTo(4), "Hidden, nonexistent and distant token targets must be rejected.");
        yield return new WaitForSecondsRealtime(6.8f);
        Assert.That(reactions.Count, Is.Zero, "Transient effects must remove themselves without persistent scene objects.");
    }

    [UnityTest]
    public IEnumerator RemoteRegistrationWorksAfterHostSessionRestart()
    {
        yield return new EnterPlayMode();
        _host = Manager("Lobby regression host");
        _registry = _host.gameObject.AddComponent(RuntimeType("PlayerRegistry"));
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        yield return RegisterRemote("Первый вход");
        var previousChannel = _host.CustomMessagingManager;
        _host.Shutdown();
        for (int i = 0; i < 100 && _host.ShutdownInProgress; i++) yield return null;
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        Assert.That(_host.CustomMessagingManager, Is.Not.SameAs(previousChannel));
        yield return RegisterRemote("Повторный вход");
    }

    [UnityTest]
    public IEnumerator DestroyingDuplicateRegistryDoesNotRemoveSyncHandlers()
    {
        yield return new EnterPlayMode();
        _host = Manager("Lobby duplicate test host");
        _registry = _host.gameObject.AddComponent(RuntimeType("PlayerRegistry"));
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        yield return null;
        var duplicate = new GameObject("Scene-load duplicate registry");
        duplicate.AddComponent(RuntimeType("PlayerRegistry"));
        yield return null;
        UnityEngine.Object.Destroy(duplicate);
        using (var writer = new FastBufferWriter(96, Allocator.Temp))
        {
            writer.WriteValueSafe(42UL); writer.WriteValueSafe(new FixedString64Bytes("Ответ после загрузки сцены"));
            writer.WriteValueSafe(0.3f); writer.WriteValueSafe(0.6f); writer.WriteValueSafe(0.9f);
            _host.CustomMessagingManager.SendNamedMessage("PlayerSync", NetworkManager.ServerClientId, writer);
        }
        var nickname = RuntimeType("PlayerColors").GetMethod("GetNickname").Invoke(null, new object[] { 42UL });
        Assert.That(nickname, Is.EqualTo("Ответ после загрузки сцены"));
    }

    [UnityTest]
    public IEnumerator MapTransferDoesNotBlockFogTogglesOrDisconnectClient()
    { return MapAndFog(false); }

    [UnityTest]
    public IEnumerator LoadingStatusIsSeparateFromMasterCurtainAndCompletionPreservesManualCurtain()
    {
        yield return new EnterPlayMode();
        _host = Manager("Client curtain host");
        _scene = new GameObject("Client curtain test");
        var type = RuntimeType("HostSceneCurtain");
        type.GetMethod("EnsureInstance").Invoke(null, null);
        var curtain = (Component)type.GetProperty("Instance").GetValue(null);
        curtain.transform.SetParent(_scene.transform);
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        _client = Manager("Loading client");
        Assert.That(_client.StartClient(), Is.True);
        for (int i = 0; i < 200 && !_client.IsConnectedClient; i++) yield return new WaitForSecondsRealtime(0.05f);
        Assert.That(_client.IsConnectedClient, Is.True);
        _curtainReceived = _syncPendingReceived = null;
        _client.CustomMessagingManager.RegisterNamedMessageHandler("HostSceneCurtainState", (sender, reader) =>
        {
            Assert.That(sender, Is.EqualTo(NetworkManager.ServerClientId));
            reader.ReadValueSafe(out bool down); reader.ReadValueSafe(out bool pending);
            _curtainReceived = down; _syncPendingReceived = pending;
        });
        type.GetMethod("SendCurtainStateToClient").Invoke(curtain, new object[] { _client.LocalClientId });
        for (int i = 0; i < 100 && _syncPendingReceived != true; i++) yield return new WaitForSecondsRealtime(0.02f);
        Assert.That(_curtainReceived, Is.False, "Automatic loading must not close the master's curtain.");
        Assert.That(_syncPendingReceived, Is.True);
        type.GetMethod("ToggleCurtainOnHost").Invoke(curtain, null);
        type.GetMethod("ToggleCurtainOnHost").Invoke(curtain, null);
        yield return new WaitForSecondsRealtime(0.2f);
        Assert.That(_curtainReceived, Is.False, "The loading panel survives independently of the master's curtain.");
        Assert.That(_syncPendingReceived, Is.True);
        type.GetMethod("ToggleCurtainOnHost").Invoke(curtain, null);
        type.GetMethod("CompleteClientSync").Invoke(curtain, new object[] { _client.LocalClientId });
        for (int i = 0; i < 100 && _syncPendingReceived != false; i++) yield return new WaitForSecondsRealtime(0.02f);
        Assert.That(_syncPendingReceived, Is.False);
        Assert.That(_curtainReceived, Is.True, "Finishing loading must preserve the master's manually closed curtain.");
        type.GetMethod("ToggleCurtainOnHost").Invoke(curtain, null);
        for (int i = 0; i < 100 && _curtainReceived != false; i++) yield return new WaitForSecondsRealtime(0.02f);
        Assert.That(_curtainReceived, Is.False);
    }

    [UnityTest]
    public IEnumerator ReconnectedPlayerCanRenameOwnAndGmAssignedTokensWithoutGrowingSuffixes()
    {
        UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Single);
        yield return new EnterPlayMode();
        _host = Manager("Rename reconnect host");
        _registry = _host.gameObject.AddComponent(RuntimeType("PlayerRegistry"));
        _host.OnClientDisconnectCallback += id => _registry.GetType().GetMethod("OnClientDisconnected").Invoke(_registry, new object[] { id });
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        _host.AddNetworkPrefab(prefab);
        _scene = new GameObject("Rename reconnect tokens");
        var tokens = _scene.AddComponent(RuntimeType("TokenManager"));
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        _client = Manager("Rename first connection");
        _client.AddNetworkPrefab(prefab);
        Assert.That(_client.StartClient(), Is.True);
        float deadline = Time.realtimeSinceStartup + 10;
        while (!_client.IsConnectedClient && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True);
        ulong oldId = _client.LocalClientId;
        var register = _registry.GetType().GetMethod("RegisterPlayerOnServer", BindingFlags.NonPublic | BindingFlags.Instance);
        register.Invoke(_registry, new object[] { oldId, "Test" });
        var type = RuntimeType("TokenController");
        var own = (Component)tokens.GetType().GetMethod("SpawnTokenForClient", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(tokens, new object[] { Vector3.zero, oldId, false, false, null, null, null });
        var assigned = (Component)tokens.GetType().GetMethod("CreateTokenAsHost").Invoke(tokens, new object[] { "Assigned", false });
        type.GetMethod("AssignController").Invoke(assigned, new object[] { oldId });
        // Exercise reassignment itself, without TokenManager's periodic nickname fallback.
        ((Behaviour)tokens).enabled = false;
        _client.Shutdown();
        deadline = Time.realtimeSinceStartup + 10;
        while ((_client.ShutdownInProgress || _host.ConnectedClients.ContainsKey(oldId)) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_host.ConnectedClients.ContainsKey(oldId), Is.False);
        UnityEngine.Object.Destroy(_client.gameObject);
        yield return null;
        _client = Manager("Rename second connection");
        _client.AddNetworkPrefab(prefab);
        Assert.That(_client.StartClient(), Is.True);
        deadline = Time.realtimeSinceStartup + 10;
        while (!_client.IsConnectedClient && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True);
        ulong newId = _client.LocalClientId;
        Assert.That(newId, Is.Not.EqualTo(oldId));
        register.Invoke(_registry, new object[] { newId, "Test" });
        Assert.That(type.GetProperty("ControllerClientId").GetValue(own), Is.EqualTo(newId));
        Assert.That(type.GetProperty("ControllerClientId").GetValue(assigned), Is.EqualTo(newId));
        Assert.That(type.GetProperty("SpawnerClientId").GetValue(assigned), Is.EqualTo(NetworkManager.ServerClientId),
            "Restoring an assigned controller must preserve the GM as creator.");
        foreach (var token in new[] { own, assigned })
        {
            var obj = token.GetComponent<NetworkObject>();
            if (!obj.IsNetworkVisibleTo(newId)) obj.NetworkShow(newId);
            deadline = Time.realtimeSinceStartup + 10;
            while (!_client.SpawnManager.SpawnedObjects.ContainsKey(obj.NetworkObjectId) && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(_client.SpawnManager.SpawnedObjects.ContainsKey(obj.NetworkObjectId), Is.True);
            var remote = _client.SpawnManager.SpawnedObjects[obj.NetworkObjectId].GetComponent(type);
            string desired = token == own ? "Character" : "Assigned character";
            for (int attempt = 0; attempt < 4; attempt++)
            {
                type.GetMethod("RequestRename").Invoke(remote, new object[] { desired });
                deadline = Time.realtimeSinceStartup + 5;
                while ((string)type.GetProperty("TokenName").GetValue(remote) != desired && Time.realtimeSinceStartup < deadline) yield return null;
                Assert.That(type.GetProperty("TokenName").GetValue(remote), Is.EqualTo(desired));
                yield return new WaitForSecondsRealtime(0.1f);
                Assert.That(type.GetProperty("TokenName").GetValue(token), Is.EqualTo(desired));
            }
        }
    }

    [UnityTest]
    public IEnumerator HighResolutionMapLoadsAndReachesClient()
    {
        if (SystemInfo.maxTextureSize < 10080) Assert.Ignore("The graphics backend cannot create a 10080-pixel source texture.");
        return MapAndFog(false, true);
    }

    [UnityTest]
    public IEnumerator RelayMapAndFogRemainConnected()
    {
        if (Environment.GetEnvironmentVariable("DICEBOUND_RELAY_TEST") != "1") Assert.Ignore("Explicit online Relay verification.");
        return MapAndFog(true);
    }

    private IEnumerator Await(Task task)
    {
        float deadline = Time.realtimeSinceStartup + 45;
        while (!task.IsCompleted && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(task.IsCompleted, Is.True, "Relay HTTP operation timed out.");
        if (task.IsFaulted) throw task.Exception;
    }

    [UnityTest]
    public IEnumerator TokenPrivacyAndLargeInitiativeReachConnectedClient()
    {
        yield return new EnterPlayMode();
        _host = Manager("Token state host");
        _scene = new GameObject("Token state scene");
        _scene.AddComponent(RuntimeType("GridManager"));
        var tokens = _scene.AddComponent(RuntimeType("TokenManager"));
        var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Token.prefab");
        tokens.GetType().GetField("tokenPrefab").SetValue(tokens, prefab.GetComponent<NetworkObject>());
        _host.AddNetworkPrefab(prefab);
        yield return null;
        Assert.That(_host.StartHost(), Is.True);
        _client = Manager("Token state client");
        _client.AddNetworkPrefab(prefab);
        Assert.That(_client.StartClient(), Is.True);
        float deadline = Time.realtimeSinceStartup + 10;
        while (!_client.IsConnectedClient && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True);

        // Both managers share one Unity scene. Create the server-only tracker after
        // client startup so NGO's initial scene-object cleanup cannot destroy it.
        var trackerObject = new GameObject("State tracker");
        trackerObject.transform.SetParent(_scene.transform);
        var tracker = trackerObject.AddComponent(RuntimeType("InitiativeTracker"));
        Assert.That(tracker, Is.Not.Null);
        Assert.That(tracker.GetComponent<NetworkObject>(), Is.Not.Null, "Tracker must have its required NetworkObject.");
        tracker.GetComponent<NetworkObject>().SpawnWithObservers = false;
        tracker.GetComponent<NetworkObject>().Spawn();
        _initiativeReceived = null;
        Assert.That(_client.CustomMessagingManager, Is.Not.Null, "Connected client must have a messaging channel.");
        _client.CustomMessagingManager.RegisterNamedMessageHandler("InitiativeStateV2", (sender, reader) =>
        {
            Assert.That(sender, Is.EqualTo(NetworkManager.ServerClientId));
            reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int count);
            var bytes = new byte[count]; reader.ReadBytesSafe(ref bytes, count);
            _initiativeReceived = System.Text.Encoding.UTF8.GetString(bytes);
            using var ack = new FastBufferWriter(4, Allocator.Temp);
            ack.WriteValueSafe(revision);
            _client.CustomMessagingManager.SendNamedMessage("InitiativeAckV2", NetworkManager.ServerClientId, ack);
        });
        var token = (Component)tokens.GetType().GetMethod("CreateTokenAsHost").Invoke(tokens, new object[] { "Private token", false });
        Assert.That(token, Is.Not.Null, "Host token creation must succeed.");
        var tokenType = token.GetType();
        tokenType.GetMethod("ServerSetHealth").Invoke(token, new object[] { 137, 251, false });
        tokenType.GetMethod("ServerSetArmorClass").Invoke(token, new object[] { 19, false });
        tokenType.GetMethod("ServerToggleCondition").Invoke(token, new object[] { "poisoned" });
        var campaignField = RuntimeType("SceneFileStore").GetField("_campaign", BindingFlags.NonPublic | BindingFlags.Static);
        _originalCampaign = campaignField.GetValue(null);
        var campaign = Activator.CreateInstance(RuntimeType("CampaignDefinition"));
        var block = Activator.CreateInstance(RuntimeType("StatBlockDefinition"));
        block.GetType().GetField("id").SetValue(block, "privacy-template");
        block.GetType().GetField("name").SetValue(block, "Privacy template");
        block.GetType().GetField("armorClass").SetValue(block, 23);
        block.GetType().GetField("hitPoints").SetValue(block, 997);
        block.GetType().GetField("publicFieldsMask").SetValue(block, 1 | 16 | 32);
        var blocks = Array.CreateInstance(block.GetType(), 1); blocks.SetValue(block, 0);
        campaign.GetType().GetField("statBlocks").SetValue(campaign, blocks);
        campaignField.SetValue(null, campaign);
        tokenType.GetMethod("ServerSetStatBlock").Invoke(token, new object[] { "privacy-template" });
        // Privacy scenario uses distinct live values so a hidden template cannot leak them.
        tokenType.GetMethod("ServerSetHealth").Invoke(token, new object[] { 137, 251, false });
        tokenType.GetMethod("ServerSetArmorClass").Invoke(token, new object[] { 19, false });
        ulong objectId = token.GetComponent<NetworkObject>().NetworkObjectId;
        deadline = Time.realtimeSinceStartup + 10;
        while (!_client.SpawnManager.SpawnedObjects.ContainsKey(objectId) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.SpawnManager.SpawnedObjects.ContainsKey(objectId), Is.True, "Token must spawn on the actual connected client.");
        var remote = _client.SpawnManager.SpawnedObjects[objectId].GetComponent(tokenType);
        Assert.That(tokenType.GetProperty("SceneId").GetValue(remote), Is.EqualTo(tokenType.GetProperty("SceneId").GetValue(token)),
            "Client tokens must retain the server's stable scene ID for initiative links.");
        Assert.That((bool)tokenType.GetProperty("IsServer").GetValue(remote), Is.False);
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(remote), Is.EqualTo(-1));
        Assert.That(tokenType.GetProperty("VisibleConditionIds").GetValue(remote), Is.Empty);
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(remote), Is.EqualTo(-1));
        string hiddenInfo = (string)tokenType.GetMethod("GetDisplayedTokenInfo").Invoke(remote, null);
        Assert.That(hiddenInfo, Does.Contain("Private token"));
        Assert.That(hiddenInfo, Does.Not.Contain("КД:"));
        Assert.That(hiddenInfo, Does.Not.Contain("ХП:"));
        Assert.That(hiddenInfo, Does.Not.Contain("Отравлен"));
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(token), Is.EqualTo(19));
        deadline = Time.realtimeSinceStartup + 10;
        while (string.IsNullOrEmpty((string)tokenType.GetProperty("PublicStatBlockJson").GetValue(remote)) && Time.realtimeSinceStartup < deadline) yield return null;
        AssertPublicStatPrivacy(remote, false, false);
        tokenType.GetMethod("ServerSetArmorClassVisibility").Invoke(token, new object[] { false, true });
        deadline = Time.realtimeSinceStartup + 10;
        while ((int)tokenType.GetProperty("ArmorClass").GetValue(remote) != 19 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(remote), Is.EqualTo(19));
        Assert.That((string)tokenType.GetMethod("GetDisplayedTokenInfo").Invoke(remote, null), Does.Contain("КД: 19"));
        deadline = Time.realtimeSinceStartup + 10;
        while ((PublicStatMask(remote) & 16) == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        AssertPublicStatPrivacy(remote, true, false);
        RuntimeType("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        deadline = Time.realtimeSinceStartup + 10;
        while ((int)tokenType.GetProperty("ArmorClass").GetValue(remote) != -1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(remote), Is.EqualTo(-1));
        deadline = Time.realtimeSinceStartup + 10;
        while ((PublicStatMask(remote) & 16) != 0 && Time.realtimeSinceStartup < deadline) yield return null;
        AssertPublicStatPrivacy(remote, false, false);

        tracker.GetType().GetMethod("AddToken").Invoke(tracker, new object[] { token });
        for (int i = 0; i < 40; i++)
            tracker.GetType().GetMethod("AddEntry").Invoke(tracker, new object[] { "Participant " + i + new string('Ж', 80), i, "#FFFFFF", 0 });
        deadline = Time.realtimeSinceStartup + 15;
        while (!(bool)tracker.GetType().GetProperty("AllClientsHaveCurrentState").GetValue(tracker) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That((bool)tracker.GetType().GetProperty("AllClientsHaveCurrentState").GetValue(tracker), Is.True, "Client must acknowledge the current fragmented snapshot.");
        var stateType = tracker.GetType().GetNestedType("InitiativeNetworkState", BindingFlags.NonPublic);
        var state = JsonUtility.FromJson(_initiativeReceived, stateType);
        var entries = (Array)stateType.GetField("entries").GetValue(state);
        Assert.That(entries.Length, Is.EqualTo(41));
        Assert.That(System.Text.Encoding.UTF8.GetByteCount(_initiativeReceived), Is.GreaterThan(4096));
        object linked = null;
        foreach (var entry in entries)
            if ((string)entry.GetType().GetField("tokenId").GetValue(entry) == (string)tokenType.GetProperty("SceneId").GetValue(token)) linked = entry;
        Assert.That(linked, Is.Not.Null);
        Assert.That(linked.GetType().GetField("publicHp").GetValue(linked), Is.EqualTo(-1));

        tokenType.GetMethod("ServerSetMasterVisibility").Invoke(token, new object[] { false, false });
        deadline = Time.realtimeSinceStartup + 10;
        while ((int)tokenType.GetProperty("VisibleCurrentHp").GetValue(remote) != 137 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(remote), Is.EqualTo(137));
        Assert.That(tokenType.GetProperty("VisibleMaxHp").GetValue(remote), Is.EqualTo(251));
        Assert.That((string[])tokenType.GetProperty("VisibleConditionIds").GetValue(remote), Does.Contain("poisoned"));
        Assert.That((string)tokenType.GetMethod("GetDisplayedTokenInfo").Invoke(remote, null), Does.Contain("ХП: 137/251"));
        Assert.That((string)tokenType.GetMethod("GetDisplayedTokenInfo").Invoke(remote, null), Does.Not.Contain("КД:"));
        deadline = Time.realtimeSinceStartup + 10;
        while ((PublicStatMask(remote) & 32) == 0 && Time.realtimeSinceStartup < deadline) yield return null;
        AssertPublicStatPrivacy(remote, false, true);
        // Naming and rename authorization must hold on an actual connected client.
        RuntimeType("PlayerColors").GetMethod("SetPlayer").Invoke(null,
            new object[] { _client.LocalClientId, "Алексей", Color.blue });
        var playerToken = (Component)tokens.GetType().GetMethod("SpawnTokenForClient", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(tokens, new object[] { Vector3.zero, _client.LocalClientId, false, false, null, null, null });
        Assert.That(tokenType.GetProperty("TokenName").GetValue(playerToken), Is.EqualTo("Алексей"));
        ulong playerObjectId = playerToken.GetComponent<NetworkObject>().NetworkObjectId;
        deadline = Time.realtimeSinceStartup + 10;
        while (!_client.SpawnManager.SpawnedObjects.ContainsKey(playerObjectId) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.SpawnManager.SpawnedObjects.ContainsKey(playerObjectId), Is.True);
        var playerRemote = _client.SpawnManager.SpawnedObjects[playerObjectId].GetComponent(tokenType);
        Assert.That(tokenType.GetMethod("ServerRename").Invoke(playerToken, new object[] { 999UL, "Чужое имя" }), Is.False);
        tokenType.GetMethod("RequestRename").Invoke(playerRemote, new object[] { "  Мой персонаж  " });
        deadline = Time.realtimeSinceStartup + 10;
        while ((string)tokenType.GetProperty("TokenName").GetValue(playerRemote) != "Мой персонаж" && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("TokenName").GetValue(playerToken), Is.EqualTo("Мой персонаж"));
        Assert.That(tokenType.GetProperty("TokenName").GetValue(playerRemote), Is.EqualTo("Мой персонаж"));
        tokenType.GetMethod("RequestRename").Invoke(playerRemote, new object[] { "Private token" });
        deadline = Time.realtimeSinceStartup + 10;
        while ((string)tokenType.GetProperty("TokenName").GetValue(playerRemote) != "Private token 2" && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("TokenName").GetValue(playerRemote), Is.EqualTo("Private token 2"));
        tokenType.GetMethod("RequestRename").Invoke(playerToken, new object[] { "Имя от GM" });
        deadline = Time.realtimeSinceStartup + 10;
        while ((string)tokenType.GetProperty("TokenName").GetValue(playerRemote) != "Имя от GM" && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("TokenName").GetValue(playerRemote), Is.EqualTo("Имя от GM"));
        RuntimeType("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        Assert.That(tokenType.GetProperty("TokenName").GetValue(playerToken), Is.EqualTo("Private token 2"));
        var savedPlayer = RuntimeType("SceneFileStore").GetMethod("CaptureToken").Invoke(null,
            new object[] { playerToken, _scene.GetComponent(RuntimeType("GridManager")) });
        Assert.That(savedPlayer.GetType().GetField("name").GetValue(savedPlayer), Is.EqualTo("Private token 2"));
        Assert.That(savedPlayer.GetType().GetField("ownerNickname").GetValue(savedPlayer), Is.EqualTo("Алексей"));
        string stableId = (string)tokenType.GetProperty("SceneId").GetValue(token);
        deadline = Time.realtimeSinceStartup + 10;
        while (ReceivedInitiativeHp(tracker, stableId) != 137 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(ReceivedInitiativeHp(tracker, stableId), Is.EqualTo(137), "Opening token HP must update the client's initiative projection.");
        tokenType.GetMethod("ServerSetMasterVisibility").Invoke(token, new object[] { true, true });
        deadline = Time.realtimeSinceStartup + 10;
        while ((int)tokenType.GetProperty("VisibleCurrentHp").GetValue(remote) != -1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(remote), Is.EqualTo(-1));
        Assert.That((string[])tokenType.GetProperty("VisibleConditionIds").GetValue(remote), Is.Empty);
        deadline = Time.realtimeSinceStartup + 10;
        while ((PublicStatMask(remote) & 32) != 0 && Time.realtimeSinceStartup < deadline) yield return null;
        AssertPublicStatPrivacy(remote, false, false);
        deadline = Time.realtimeSinceStartup + 10;
        while (ReceivedInitiativeHp(tracker, stableId) != -1 && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(ReceivedInitiativeHp(tracker, stableId), Is.EqualTo(-1));
        _initiativeReceived = null;
        using (var request = new FastBufferWriter(1, Allocator.Temp))
            _client.CustomMessagingManager.SendNamedMessage("InitiativeRequestV2", NetworkManager.ServerClientId, request);
        deadline = Time.realtimeSinceStartup + 10;
        while ((_initiativeReceived == null || !(bool)tracker.GetType().GetProperty("AllClientsHaveCurrentState").GetValue(tracker)) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_initiativeReceived, Is.Not.Null, "A newly ready client must be able to request the complete current snapshot.");
        state = JsonUtility.FromJson(_initiativeReceived, stateType);
        Assert.That(((Array)stateType.GetField("entries").GetValue(state)).Length, Is.EqualTo(41));
        Assert.That(ReceivedInitiativeHp(tracker, stableId), Is.EqualTo(-1));
        tokenType.GetMethod("ServerSetMasterVisibility").Invoke(token, new object[] { false, false });
        tokenType.GetMethod("ServerSetArmorClassVisibility").Invoke(token, new object[] { false, false });
        tokenType.GetMethod("ServerSetStatBlock").Invoke(token, new object[] { "" });
        Assert.That(tokenType.GetMethod("ServerSetStatBlock").Invoke(token, new object[] { "privacy-template" }), Is.EqualTo(true));
        deadline = Time.realtimeSinceStartup + 10;
        while (((int)tokenType.GetProperty("VisibleCurrentHp").GetValue(remote) != 997
            || (int)tokenType.GetProperty("ArmorClass").GetValue(remote) != 23) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(remote), Is.EqualTo(997), "Assigning a template must replicate its initial HP.");
        Assert.That(tokenType.GetProperty("VisibleMaxHp").GetValue(remote), Is.EqualTo(997));
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(remote), Is.EqualTo(23));
        tokenType.GetMethod("ServerSetHealth").Invoke(token, new object[] { 400, 997, false });
        tokenType.GetMethod("ServerSetStatBlock").Invoke(token, new object[] { "privacy-template" });
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(token), Is.EqualTo(400), "Repeated assignment must preserve wounds.");
        block.GetType().GetField("hitPoints").SetValue(block, 777);
        block.GetType().GetField("armorClass").SetValue(block, 22);
        tokenType.GetMethod("RefreshPublicStatBlock").Invoke(token, null);
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(token), Is.EqualTo(400));
        Assert.That(tokenType.GetProperty("VisibleMaxHp").GetValue(token), Is.EqualTo(997));
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(token), Is.EqualTo(23));
        RuntimeType("GameMasterUndo").GetMethod("Undo").Invoke(null, null);
        deadline = Time.realtimeSinceStartup + 10;
        while (((int)tokenType.GetProperty("VisibleCurrentHp").GetValue(remote) != 137
            || (int)tokenType.GetProperty("ArmorClass").GetValue(remote) != 19) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(tokenType.GetProperty("VisibleCurrentHp").GetValue(remote), Is.EqualTo(137), "Undo must restore original combat values on the connected client.");
        Assert.That(tokenType.GetProperty("VisibleMaxHp").GetValue(remote), Is.EqualTo(251));
        Assert.That(tokenType.GetProperty("ArmorClass").GetValue(remote), Is.EqualTo(19));
        deadline = Time.realtimeSinceStartup + 10;
        while (!(bool)tracker.GetType().GetProperty("AllClientsHaveCurrentState").GetValue(tracker) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That((bool)tracker.GetType().GetProperty("AllClientsHaveCurrentState").GetValue(tracker), Is.True);
        Assert.That(_client.IsConnectedClient, Is.True);
    }

    private static int PublicStatMask(Component token)
    {
        string json = (string)token.GetType().GetProperty("PublicStatBlockJson").GetValue(token);
        var view = JsonUtility.FromJson(json, RuntimeType("PublicStatBlockView"));
        return (int)view.GetType().GetField("visibleFields").GetValue(view);
    }

    private static void AssertPublicStatPrivacy(Component token, bool ac, bool hp)
    {
        string json = (string)token.GetType().GetProperty("PublicStatBlockJson").GetValue(token);
        var view = JsonUtility.FromJson(json, RuntimeType("PublicStatBlockView"));
        Assert.That((PublicStatMask(token) & 16) != 0, Is.EqualTo(ac));
        Assert.That((PublicStatMask(token) & 32) != 0, Is.EqualTo(hp));
        Assert.That(view.GetType().GetField("armorClass").GetValue(view), Is.EqualTo(ac ? 23 : 0));
        Assert.That(view.GetType().GetField("hitPoints").GetValue(view), Is.EqualTo(hp ? 997 : 0));
    }

    private int ReceivedInitiativeHp(Component tracker, string tokenId)
    {
        if (_initiativeReceived == null) return int.MinValue;
        var type = tracker.GetType().GetNestedType("InitiativeNetworkState", BindingFlags.NonPublic);
        var state = JsonUtility.FromJson(_initiativeReceived, type);
        foreach (var entry in (Array)type.GetField("entries").GetValue(state))
            if ((string)entry.GetType().GetField("tokenId").GetValue(entry) == tokenId)
                return (int)entry.GetType().GetField("publicHp").GetValue(entry);
        return int.MinValue;
    }

    private IEnumerator MapAndFog(bool relay, bool highResolution = false)
    {
        yield return new EnterPlayMode();
        _host = Manager("Map and fog host");
        _registry = _host.gameObject.AddComponent(RuntimeType("PlayerRegistry"));
        _scene = new GameObject("Map and fog regression scene");
        var grid = _scene.AddComponent(RuntimeType("GridManager"));
        _scene.AddComponent(RuntimeType("SceneEditor"));
        var fog = _scene.AddComponent(RuntimeType("FogManager"));
        var mapObject = new GameObject("Map"); mapObject.transform.SetParent(_scene.transform);
        var netMap = mapObject.AddComponent<NetworkObject>(); netMap.SpawnWithObservers = false;
        var map = mapObject.AddComponent(RuntimeType("MapController"));
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane); plane.transform.SetParent(mapObject.transform);
        map.GetType().GetField("mapPlane").SetValue(map, plane);
        var sync = mapObject.AddComponent(RuntimeType("MapSync"));
        sync.GetType().GetField("mapController").SetValue(sync, map);
        yield return null;
        object joined = null;
        if (relay)
        {
            var relayManager = _host.gameObject.AddComponent(RuntimeType("RelayManager"));
            var allocation = (Task<string>)relayManager.GetType().GetMethod("CreateRelayAllocation").Invoke(relayManager, new object[] { 2 });
            yield return Await(allocation);
            var relayType = Type.GetType("Unity.Services.Relay.RelayService, Unity.Services.Relay", true);
            var service = relayType.GetProperty("Instance").GetValue(null);
            var join = (Task)service.GetType().GetMethod("JoinAllocationAsync", new[] { typeof(string) }).Invoke(service, new object[] { allocation.Result });
            yield return Await(join);
            joined = join.GetType().GetProperty("Result").GetValue(join);
        }
        Assert.That(_host.StartHost(), Is.True);
        _client = Manager("Map and fog client");
        if (joined != null)
        {
            object Read(object obj, string property) => obj.GetType().GetProperty(property).GetValue(obj);
            var server = Read(joined, "RelayServer");
            _client.GetComponent<UnityTransport>().SetClientRelayData((string)Read(server, "IpV4"), (ushort)(int)Read(server, "Port"),
                (byte[])Read(joined, "AllocationIdBytes"), (byte[])Read(joined, "Key"), (byte[])Read(joined, "ConnectionData"), (byte[])Read(joined, "HostConnectionData"));
        }
        Assert.That(_client.StartClient(), Is.True);
        for (int i = 0; i < 200 && !_client.IsConnectedClient; i++) yield return new WaitForSecondsRealtime(0.05f);
        Assert.That(_client.IsConnectedClient, Is.True);
        _transferTotal = _transferReceived = _fogChanges = 0; _incomingImage = null; _fogEnabled = null;
        _client.CustomMessagingManager.RegisterNamedMessageHandler("MapMeta", OnTransferMeta);
        _client.CustomMessagingManager.RegisterNamedMessageHandler("MapChunk", OnTransferChunk);
        _client.CustomMessagingManager.RegisterNamedMessageHandler("FogStateV2", OnTransferFog);
        _mapTexture = new Texture2D(512, 512, TextureFormat.RGB24, false);
        if (!highResolution)
        {
            var pixels = new Color32[512 * 512]; var random = new System.Random(42);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), 255);
            _mapTexture.SetPixels32(pixels);
        }
        _mapTexture.Apply();
        var original = highResolution ? SolidPng(10080, 6720) : _mapTexture.EncodeToPNG();
        map.GetType().GetMethod("ApplyImage").Invoke(map, new object[] { original });
        var transmitted = (byte[])map.GetType().GetMethod("GetCurrentPngData").Invoke(map, null);
        Assert.That(transmitted, Is.Not.Null);
        fog.GetType().GetMethod("ToggleEnabled").Invoke(fog, null);
        float deadline = Time.realtimeSinceStartup + 40;
        while ((_transferReceived < Mathf.Min(32, _transferTotal) || _transferTotal == 0 || _fogEnabled != false) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_transferReceived, Is.GreaterThanOrEqualTo(Mathf.Min(32, _transferTotal))); Assert.That(_fogEnabled, Is.False);
        fog.GetType().GetMethod("ToggleEnabled").Invoke(fog, null);
        while ((_transferReceived != _transferTotal || _fogEnabled != true) && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(_client.IsConnectedClient, Is.True); Assert.That(_transferReceived, Is.EqualTo(_transferTotal));
        Assert.That(_incomingImage, Is.EqualTo(transmitted)); Assert.That(_fogEnabled, Is.True); Assert.That(_fogChanges, Is.GreaterThanOrEqualTo(2));
        Assert.That(map.GetType().GetMethod("GetCurrentPngData").Invoke(map, null), Is.SameAs(map.GetType().GetMethod("GetCurrentPngData").Invoke(map, null)), "Saving must reuse image bytes.");
    }

    private void OnTransferMeta(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out _transferTotal); reader.ReadValueSafe(out int bytes); reader.ReadValueSafe(out uint checksum);
        _incomingImage = new byte[bytes]; _transferReceived = 0;
    }
    private static byte[] SolidPng(int width, int height)
    {
        using var result = new MemoryStream();
        void UInt(uint value) { for (int shift = 24; shift >= 0; shift -= 8) result.WriteByte((byte)(value >> shift)); }
        void Chunk(string name, byte[] data)
        {
            UInt((uint)data.Length); uint crc = uint.MaxValue;
            foreach (byte value in System.Text.Encoding.ASCII.GetBytes(name)) { result.WriteByte(value); Crc(value); }
            foreach (byte value in data) { result.WriteByte(value); Crc(value); }
            UInt(~crc);
            void Crc(byte value) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1; }
        }
        result.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
        var header = new byte[13];
        for (int i = 0; i < 4; i++) { header[i] = (byte)(width >> (24 - 8 * i)); header[i + 4] = (byte)(height >> (24 - 8 * i)); }
        header[8] = 8; header[9] = 2; Chunk("IHDR", header);
        using var compressed = new MemoryStream(); compressed.WriteByte(0x78); compressed.WriteByte(0x01);
        var row = new byte[width * 3 + 1];
        using (var deflate = new DeflateStream(compressed, System.IO.Compression.CompressionLevel.Fastest, true))
            for (int y = 0; y < height; y++) deflate.Write(row, 0, row.Length);
        uint adler = ((uint)((long)row.Length * height % 65521) << 16) | 1;
        for (int shift = 24; shift >= 0; shift -= 8) compressed.WriteByte((byte)(adler >> shift));
        Chunk("IDAT", compressed.ToArray()); Chunk("IEND", Array.Empty<byte>());
        return result.ToArray();
    }
    private void OnTransferChunk(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int version); reader.ReadValueSafe(out int index); reader.ReadValueSafe(out int size);
        var data = new byte[size]; reader.ReadBytesSafe(ref data, size); Buffer.BlockCopy(data, 0, _incomingImage, index * 1000, size); _transferReceived++;
        if (_transferReceived % 32 == 0 || _transferReceived == _transferTotal)
        {
            using var writer = new FastBufferWriter(8, Allocator.Temp); writer.WriteValueSafe(version); writer.WriteValueSafe(_transferReceived);
            _client.CustomMessagingManager.SendNamedMessage("MapProgress", 0, writer);
        }
        if (_transferReceived == _transferTotal)
        {
            using var writer = new FastBufferWriter(4, Allocator.Temp); writer.WriteValueSafe(version);
            _client.CustomMessagingManager.SendNamedMessage("MapAck", 0, writer);
        }
    }
    private void OnTransferFog(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int width); reader.ReadValueSafe(out int height); reader.ReadValueSafe(out bool state);
        reader.ReadValueSafe(out int bytes); var payload = new byte[bytes]; reader.ReadBytesSafe(ref payload, bytes);
        var arguments = new object[] { payload, width * height * 16, null, null };
        RuntimeType("FogStateCodec").GetMethod("Decode").Invoke(null, arguments);
        Assert.That(((bool[])arguments[2]).Length, Is.EqualTo(width * height * 16));
        _fogEnabled = state; _fogChanges++;
    }

    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        RuntimeType("SceneFileStore").GetField("_campaign", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _originalCampaign);
        _originalCampaign = null;
        if (_client != null) _client.Shutdown();
        if (_host != null) _host.Shutdown();
        yield return null;
        if (_client != null) UnityEngine.Object.Destroy(_client.gameObject);
        if (_host != null) UnityEngine.Object.Destroy(_host.gameObject);
        if (_scene != null) UnityEngine.Object.Destroy(_scene);
        if (_mapTexture != null) UnityEngine.Object.Destroy(_mapTexture);
        yield return null;
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
