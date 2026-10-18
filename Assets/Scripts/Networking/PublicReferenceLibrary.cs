using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Read-only player library. Host-only entries never enter its wire snapshot.</summary>
public sealed class PublicReferenceLibrary : MonoBehaviour
{
    private const string RequestMessage = "ReferenceRequestV1", HeaderMessage = "ReferenceHeaderV1", ChunkMessage = "ReferenceChunkV1";
    private const string InvalidateMessage = "ReferenceInvalidateV1";
    private const string ErrorMessage = "ReferenceErrorV1";
    private static PublicReferenceLibrary _instance;
    private const int ChunkSize = 1000, MaxBytes = 16 * 1024 * 1024;
    public static bool IsOpen { get; private set; }
    private static bool _available;
    public static bool IsPointerOverButton
    {
        get
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (!_available || mouse == null || DisplaySettingsUI.IsOpen || SceneArrivalPicker.IsOpen) return false;
            float scale = Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 600f, Screen.height / 460f));
            return new Rect(Screen.width - 286 * scale, 12 * scale, 132 * scale, 28 * scale).Contains(mouse.position.ReadValue());
        }
    }
    private NetworkManager _network;
    private CustomMessagingManager _messaging;
    private readonly HashSet<ulong> _sending = new();
    private readonly Dictionary<ulong, float> _requests = new();
    private byte[] _published, _incoming;
    private string _publishedHash, _incomingHash;
    private int _revision, _incomingRevision = -1, _appliedRevision = -1, _received;
    private bool[] _chunks;
    private ReferenceEntry[] _entries = Array.Empty<ReferenceEntry>();
    private string _query = "", _category = "Все категории", _selectedId, _status = "";
    private Vector2 _listScroll, _bodyScroll;
    private float _nextRequest;
    private GameObject _blocker;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState() { IsOpen = false; _available = false; _instance = null; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var root = new GameObject("PublicReferenceLibrary");
        DontDestroyOnLoad(root); root.AddComponent<PublicReferenceLibrary>();
    }

    private void Awake()
    {
        _instance = this;
        _blocker = new GameObject("LibraryInputBlocker", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
        _blocker.transform.SetParent(transform, false);
        var canvas = _blocker.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32750;
        var overlay = new GameObject("Modal", typeof(RectTransform), typeof(Image)); overlay.transform.SetParent(_blocker.transform, false);
        var rect = overlay.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero; overlay.GetComponent<Image>().color = Color.clear;
        _blocker.SetActive(false);
    }

    private void Update()
    {
        var network = NetworkManager.Singleton;
        _available = network != null && network.IsConnectedClient && !network.IsServer;
        var messaging = network != null && network.IsListening ? network.CustomMessagingManager : null;
        if (_network != network || _messaging != messaging)
        {
            Detach(); _network = network; _messaging = messaging;
            if (_messaging != null)
            {
                _messaging.RegisterNamedMessageHandler(RequestMessage, OnRequest);
                _messaging.RegisterNamedMessageHandler(HeaderMessage, OnHeader);
                _messaging.RegisterNamedMessageHandler(ChunkMessage, OnChunk);
                _messaging.RegisterNamedMessageHandler(InvalidateMessage, OnInvalidated);
                _messaging.RegisterNamedMessageHandler(ErrorMessage, OnLibraryError);
            }
        }
        if (_network == null || !_network.IsConnectedClient) { if (IsOpen) SetOpen(false); return; }
        if (IsOpen && !_network.IsServer && Time.unscaledTime >= _nextRequest)
        {
            _nextRequest = Time.unscaledTime + 5;
            using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
            writer.WriteValueSafe(_appliedRevision);
            _messaging?.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer);
        }
    }

    private void Detach()
    {
        IsOpen = false; if (_blocker != null) _blocker.SetActive(false);
        if (_messaging != null)
        {
            _messaging.UnregisterNamedMessageHandler(RequestMessage);
            _messaging.UnregisterNamedMessageHandler(HeaderMessage);
            _messaging.UnregisterNamedMessageHandler(ChunkMessage);
            _messaging.UnregisterNamedMessageHandler(InvalidateMessage);
            _messaging.UnregisterNamedMessageHandler(ErrorMessage);
        }
        StopAllCoroutines(); _sending.Clear(); _requests.Clear();
        _entries = Array.Empty<ReferenceEntry>(); _incoming = _published = null; _chunks = null;
        _publishedHash = _incomingHash = null; _incomingRevision = _appliedRevision = -1;
        _selectedId = null; _status = ""; _nextRequest = 0;
    }

    private void OnRequest(ulong sender, FastBufferReader reader)
    {
        if (_network == null || !_network.IsServer || !_network.ConnectedClients.ContainsKey(sender) || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int known);
        if (_sending.Contains(sender) || _requests.TryGetValue(sender, out float previous) && Time.unscaledTime - previous < 2) return;
        _requests[sender] = Time.unscaledTime;
        // Filtering happens on the host before serialization; no campaign or master data is sent.
        var file = new ReferenceLibraryFile { entries = SceneFileStore.GetPublicReferenceEntries() };
        var bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(file));
        if (bytes.Length > MaxBytes)
        {
            using var error = new FastBufferWriter(1, Allocator.Temp);
            error.WriteValueSafe((byte)1); _messaging.SendNamedMessage(ErrorMessage, sender, error);
            return;
        }
        string hash = MapImageCache.Hash(bytes);
        if (hash != _publishedHash) { _published = bytes; _publishedHash = hash; _revision++; }
        if (known != _revision) StartCoroutine(Send(sender, _published, _publishedHash, _revision));
    }

    /// <summary>Revoke old UI and cancel queued serialization before publishing a changed library.</summary>
    public static void InvalidatePublishedLibrary()
    {
        var instance = _instance;
        if (instance == null || instance._network?.IsServer != true || instance._messaging == null) return;
        if (instance._publishedHash == null) return; // No client can hold a published snapshot yet.
        var file = new ReferenceLibraryFile { entries = SceneFileStore.GetPublicReferenceEntries() };
        string currentHash = MapImageCache.Hash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(file)));
        if (currentHash == instance._publishedHash) return; // Private notes must not interrupt player reading.
        instance.StopAllCoroutines(); instance._sending.Clear(); instance._requests.Clear();
        instance._published = null; instance._publishedHash = null; instance._revision++;
        using var writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(instance._revision);
        foreach (ulong client in instance._network.ConnectedClientsIds)
            if (client != NetworkManager.ServerClientId) instance._messaging.SendNamedMessage(InvalidateMessage, client, writer);
    }
    private void OnInvalidated(ulong sender, FastBufferReader reader)
    {
        if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int revision);
        if (revision <= _appliedRevision || revision < _incomingRevision) return;
        _entries = Array.Empty<ReferenceEntry>(); _incoming = null; _chunks = null;
        _selectedId = null; _incomingRevision = revision; _appliedRevision = -1; _nextRequest = 0;
        _status = "Справочник обновлён мастером. Получаем доступные записи…";
    }
    private void OnLibraryError(ulong sender, FastBufferReader reader)
    {
        if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(1)) return;
        reader.ReadValueSafe(out byte code); if (code != 1) return;
        _entries = Array.Empty<ReferenceEntry>(); _incoming = null; _chunks = null; _selectedId = null;
        _status = "Открытый справочник превышает 16 МБ. Мастеру нужно сократить открытую часть библиотеки.";
        _nextRequest = Time.unscaledTime + 30;
    }

    private IEnumerator Send(ulong client, byte[] bytes, string hash, int revision)
    {
        _sending.Add(client);
        try
        {
            var digest = Encoding.ASCII.GetBytes(hash);
            using (var writer = new FastBufferWriter(8 + 64, Allocator.Temp))
            {
                writer.WriteValueSafe(revision); writer.WriteValueSafe(bytes.Length); writer.WriteBytesSafe(digest, 64);
                _messaging.SendNamedMessage(HeaderMessage, client, writer);
            }
            for (int offset = 0; offset < bytes.Length; offset += ChunkSize)
            {
                if (revision != _revision) yield break;
                int size = Math.Min(ChunkSize, bytes.Length - offset);
                while (!NetworkTransferBudget.TryConsume(client, size + 12))
                {
                    if (_network == null || !_network.IsServer || !_network.ConnectedClients.ContainsKey(client)) yield break;
                    if (revision != _revision) yield break;
                    yield return null;
                }
                if (_network == null || !_network.IsServer || !_network.ConnectedClients.ContainsKey(client)) yield break;
                using (var writer = new FastBufferWriter(12 + size, Allocator.Temp))
                {
                    writer.WriteValueSafe(revision); writer.WriteValueSafe(offset); writer.WriteValueSafe(size);
                    writer.WriteBytesSafe(bytes, size, offset); _messaging.SendNamedMessage(ChunkMessage, client, writer);
                }
                if (offset / ChunkSize % 4 == 3) yield return null;
            }
        }
        finally { _sending.Remove(client); }
    }

    private void OnHeader(ulong sender, FastBufferReader reader)
    {
        if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || !reader.TryBeginRead(72)) return;
        reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int length);
        byte[] digest = new byte[64]; reader.ReadBytesSafe(ref digest, 64);
        if (length <= 0 || length > MaxBytes || revision <= _appliedRevision || revision < _incomingRevision) return;
        _incomingRevision = revision; _incomingHash = Encoding.ASCII.GetString(digest);
        _incoming = new byte[length]; _chunks = new bool[(length + ChunkSize - 1) / ChunkSize]; _received = 0;
        _status = "Получение справочника…";
    }

    private void OnChunk(ulong sender, FastBufferReader reader)
    {
        if (_network == null || _network.IsServer || sender != NetworkManager.ServerClientId || _incoming == null || !reader.TryBeginRead(12)) return;
        reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int offset); reader.ReadValueSafe(out int size);
        if (revision != _incomingRevision || offset < 0 || offset >= _incoming.Length || offset % ChunkSize != 0
            || size != Math.Min(ChunkSize, _incoming.Length - offset) || !reader.TryBeginRead(size)) return;
        int index = offset / ChunkSize;
        if (_chunks[index]) return;
        byte[] chunk = new byte[size]; reader.ReadBytesSafe(ref chunk, size); Buffer.BlockCopy(chunk, 0, _incoming, offset, size);
        _chunks[index] = true; _received++;
        if (_received != _chunks.Length) return;
        try
        {
            if (MapImageCache.Hash(_incoming) != _incomingHash) throw new FormatException();
            var file = JsonUtility.FromJson<ReferenceLibraryFile>(Encoding.UTF8.GetString(_incoming));
            SceneFileStore.ValidateReferenceLibrary(file);
            foreach (var entry in file.entries) if (entry == null || entry.masterOnly) throw new FormatException();
            _entries = file.entries; _appliedRevision = revision; _status = _entries.Length == 0 ? "Мастер пока не открыл записи." : "";
        }
        catch (Exception) { _status = "Не удалось получить справочник. Повтор через несколько секунд."; }
        finally { _incoming = null; _chunks = null; }
    }

    private void SetOpen(bool open) { IsOpen = open; GameplayInputGate.MarkToolExit(); _blocker.SetActive(open); if (open) _nextRequest = 0; }
    private void OnDestroy() { Detach(); IsOpen = false; _available = false; if (_instance == this) _instance = null; }

    private void OnGUI()
    {
        if (IsOpen && Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        { SetOpen(false); Event.current.Use(); }
        if (_network == null || !_network.IsConnectedClient || _network.IsServer || MovementReviewUI.IsOpen || DisplaySettingsUI.IsOpen || SceneArrivalPicker.IsOpen) return;
        Matrix4x4 matrix = GUI.matrix; int depth = GUI.depth;
        float scale = IsOpen ? Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 940f, Screen.height / 640f))
            : Mathf.Min(LocalUiScale.Value, Mathf.Min(Screen.width / 600f, Screen.height / 460f));
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1)); GUI.depth = -900;
        try
        {
            float width = Screen.width / scale, height = Screen.height / scale;
            if (!IsOpen)
            {
                if (GUI.Button(new Rect(width - 286, height - 40, 132, 28), "Справочник", VttUiSkin.ImGuiButton)) SetOpen(true);
                return;
            }
            GUI.Box(new Rect(0, 0, width, height), "", VttUiSkin.ImGuiPanel);
            GUILayout.BeginArea(new Rect((width - 920) / 2, (height - 620) / 2, 920, 620), VttUiSkin.ImGuiPanel);
            GUILayout.BeginHorizontal(); GUILayout.Label("Справочник кампании");
            if (GUILayout.Button("Закрыть", GUILayout.Width(100))) SetOpen(false);
            GUILayout.EndHorizontal();
            _query = GUILayout.TextField(_query, 128); GUILayout.Label(_status);
            var categories = new List<string> { "Все категории" };
            foreach (var entry in _entries) if (!categories.Contains(entry.category)) categories.Add(entry.category);
            int categoryIndex = Math.Max(0, categories.IndexOf(_category));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(32))) categoryIndex = (categoryIndex + categories.Count - 1) % categories.Count;
            GUILayout.Label(categories[categoryIndex]);
            if (GUILayout.Button("▶", GUILayout.Width(32))) categoryIndex = (categoryIndex + 1) % categories.Count;
            GUILayout.EndHorizontal(); _category = categories[categoryIndex];
            GUILayout.BeginHorizontal();
            _listScroll = GUILayout.BeginScrollView(_listScroll, GUILayout.Width(300));
            var sorted = new List<ReferenceEntry>(_entries);
            sorted.Sort((a, b) => a.pinned != b.pinned ? b.pinned.CompareTo(a.pinned) : string.Compare(a.title, b.title, StringComparison.CurrentCultureIgnoreCase));
            bool found = false;
            var entryStyle = new GUIStyle(GUI.skin.button) { wordWrap = true, richText = false, alignment = TextAnchor.MiddleLeft };
            foreach (var entry in sorted)
            {
                if (_category != "Все категории" && entry.category != _category) continue;
                string search = (entry.title ?? "") + " " + entry.category + " " + entry.body + " " + string.Join(" ", entry.tags ?? Array.Empty<string>());
                if (search.IndexOf(_query, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                found = true;
                if (GUILayout.Button(new GUIContent((entry.pinned ? "★ " : "") + entry.title, entry.title), entryStyle, GUILayout.Width(272), GUILayout.MinHeight(32))) { _selectedId = entry.id; _bodyScroll = Vector2.zero; }
            }
            if (!found) GUILayout.Label(string.IsNullOrWhiteSpace(_query) ? "В этой категории пока нет открытых записей." : "Ничего не найдено. Измените запрос или категорию.", new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.EndScrollView();
            _bodyScroll = GUILayout.BeginScrollView(_bodyScroll);
            var selected = Array.Find(_entries, entry => entry.id == _selectedId);
            var style = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            if (selected != null)
            {
                GUILayout.Label(selected.title, style); GUILayout.Label(selected.category + " · " + string.Join(", ", selected.tags ?? Array.Empty<string>()), style);
                GUILayout.Label(selected.body, style);
            }
            else GUILayout.Label("Выберите запись слева.");
            GUILayout.EndScrollView(); GUILayout.EndHorizontal(); GUILayout.EndArea();
        }
        finally { GUI.matrix = matrix; GUI.depth = depth; }
    }
}
