using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Shared initiative order. Only the host changes entries and turns.</summary>
[RequireComponent(typeof(NetworkObject))]
public class InitiativeTracker : NetworkBehaviour
{
    [Serializable]
    private sealed class InitiativeNetworkState
    {
        public int currentIndex;
        public int round = 1;
        public string activeParticipantId;
        public InitiativeNetworkEntry[] entries = Array.Empty<InitiativeNetworkEntry>();
    }

    [Serializable]
    private sealed class InitiativeNetworkEntry
    {
        public int id;
        public string name;
        public int initiative;
        public string colorHex;
        public ulong playerId;
        public string tokenId;
        public int publicHp = -1;
    }

    private readonly NetworkVariable<FixedString4096Bytes> _netData = new(
        new FixedString4096Bytes(""), NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private struct Entry
    {
        public int id;
        public string name;
        public int initiative;
        public string colorHex;
        public ulong playerId;
        public string tokenId;
        public int publicHp;
    }

    private readonly List<Entry> _entries = new();
    private const string StateMessage = "InitiativeStateV2", RequestMessage = "InitiativeRequestV2", AckMessage = "InitiativeAckV2";
    private const int MaxStateBytes = 512 * 1024;
    private int _stateRevision, _receivedRevision = -1;
    private float _nextStateRequest;
    private readonly Dictionary<ulong, int> _stateAcks = new();
    public bool AllClientsHaveCurrentState
    {
        get
        {
            if (!IsSpawned || !IsServer) return false;
            foreach (ulong client in NetworkManager.ConnectedClientsIds)
                if (client != Unity.Netcode.NetworkManager.ServerClientId
                    && (!_stateAcks.TryGetValue(client, out int revision) || revision != _stateRevision)) return false;
            return true;
        }
    }
    // Host HP cache; only the privacy-filtered HP projection enters _netData.
    private readonly Dictionary<int, int> _hpById = new();
    private int _currentIndex;
    private int _nextEntryId;
    private bool _userVisible;
    private bool _hostControlsVisible;
    private int _round = 1;
    private string _activeParticipantId;
    private string _awaitingInitiativeTokenId;
    private readonly Queue<string> _awaitingInitiativeTokenIds = new();
    private float _initiativeRollExpiresAt;
    private Canvas _canvas;
    private Font _font;
    private GameObject _panel, _addPanel, _hostButtons, _emptyMessage;
    private GameObject _viewport;
    private RectTransform _content;
    private InputField _nameInput, _initInput, _hpInput;
    private int _focusedAddField;
    private Text _roundText;
    private Text _addErrorText;
    private const int CardsPerRow = 5;
    private const int CardWidth = 164;
    private const int CardHeight = 48;
    private const int CardStepX = 170;
    private const int CardStepY = 54;

    public static InitiativeTracker Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        BuildUI();
        DiceUI.JournalResultRecorded += OnJournalResultRecorded;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    private void OnJournalResultRecorded(string dieType, int result, ulong throwerId)
    {
        if (!IsHost || _awaitingInitiativeTokenIds.Count == 0 && string.IsNullOrEmpty(_awaitingInitiativeTokenId)) return;
        if (Time.unscaledTime > _initiativeRollExpiresAt)
        {
            _awaitingInitiativeTokenId = null;
            _awaitingInitiativeTokenIds.Clear();
            DiceUI.Instance?.ShowToolNotice("Назначение инициативы истекло. Нажмите 🎲 ещё раз.");
            return;
        }
        if (dieType != "d20") return;
        if (throwerId != NetworkManager.Singleton.LocalClientId) return;

        string tokenId;
        if (_awaitingInitiativeTokenIds.Count > 0)
        {
            tokenId = _awaitingInitiativeTokenIds.Dequeue();
            _awaitingInitiativeTokenId = _awaitingInitiativeTokenIds.Count > 0 ? _awaitingInitiativeTokenIds.Peek() : null;
        }
        else
        {
            tokenId = _awaitingInitiativeTokenId;
            _awaitingInitiativeTokenId = null;
        }
        int index = _entries.FindIndex(entry => entry.tokenId == tokenId);
        if (index < 0) return;
        int id = _entries[index].id;
        int previous = _entries[index].initiative;
        GameMasterUndo.Record("инициатива", () => SetInitiativeById(id, previous.ToString()));
        SetInitiativeById(id, result.ToString());
        DiceUI.Instance?.ShowToolNotice($"Инициатива: {result}");
    }

    public void ArmInitiativeFromJournal(string tokenId)
    {
        if (!IsHost || string.IsNullOrEmpty(tokenId) || !ContainsToken(tokenId)) return;
        _awaitingInitiativeTokenIds.Clear();
        _awaitingInitiativeTokenId = tokenId;
        _initiativeRollExpiresAt = Time.unscaledTime + 120f;
        DiceUI.Instance?.ShowToolNotice("Бросьте d20: следующий результат из журнала пойдёт в инициативу.");
    }

    public int AddTokensAndArmInitiative(IEnumerable<TokenController> tokens)
    {
        if (!IsHost || tokens == null) return 0;
        var eligible = new List<TokenController>();
        foreach (var token in tokens)
            if (token != null && token.IsSpawned && !ContainsToken(token.SceneId)) eligible.Add(token);
        if (eligible.Count == 0) return 0;

        _awaitingInitiativeTokenIds.Clear();
        foreach (var token in eligible)
        {
            AddToken(token);
            _awaitingInitiativeTokenIds.Enqueue(token.SceneId);
        }
        _awaitingInitiativeTokenId = _awaitingInitiativeTokenIds.Peek();
        _initiativeRollExpiresAt = Time.unscaledTime + 300f;
        var tokenIds = new string[eligible.Count];
        for (int i = 0; i < eligible.Count; i++) tokenIds[i] = eligible[i].SceneId;
        if (NetworkDiceManager.Instance?.RollInitiativeGroup(tokenIds) != true)
        {
            _awaitingInitiativeTokenIds.Clear();
            _awaitingInitiativeTokenId = null;
            DiceUI.Instance?.ShowToolNotice($"Добавлено в инициативу: {eligible.Count}, но сетевые кости недоступны. Броски можно назначить кнопкой d20 в трекере.");
            return eligible.Count;
        }
        DiceUI.Instance?.ShowToolNotice($"Добавлено в инициативу: {eligible.Count}. Брошены групповые d20; результаты назначатся по порядку списка токенов.");
        return eligible.Count;
    }

    public void RemovePendingInitiativeAssignment(string tokenId)
    {
        if (!IsHost || string.IsNullOrEmpty(tokenId) || _awaitingInitiativeTokenIds.Count == 0) return;
        var pending = new List<string>(_awaitingInitiativeTokenIds);
        if (!pending.Remove(tokenId)) return;
        _awaitingInitiativeTokenIds.Clear();
        foreach (string pendingId in pending) _awaitingInitiativeTokenIds.Enqueue(pendingId);
        _awaitingInitiativeTokenId = _awaitingInitiativeTokenIds.Count > 0 ? _awaitingInitiativeTokenIds.Peek() : null;
    }

    public void CancelInitiativeFromJournal()
    {
        if (!IsHost || _awaitingInitiativeTokenIds.Count == 0 && string.IsNullOrEmpty(_awaitingInitiativeTokenId)) return;
        _awaitingInitiativeTokenId = null;
        _awaitingInitiativeTokenIds.Clear();
        DiceUI.Instance?.ShowToolNotice("Назначение броска инициативы отменено.");
    }

    private void ResetPendingInitiative()
    {
        _awaitingInitiativeTokenId = null;
        _awaitingInitiativeTokenIds.Clear();
        _initiativeRollExpiresAt = 0;
    }

    private void OnServerStarted()
    {
        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && !netObj.IsSpawned) netObj.Spawn();
    }

    public override void OnNetworkSpawn()
    {
        _stateRevision = 0; _receivedRevision = -1;
        _stateAcks.Clear();
        NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(StateMessage, ReceiveState);
        NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(RequestMessage, ReceiveStateRequest);
        NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(AckMessage, ReceiveStateAck);
        _netData.OnValueChanged += OnDataChanged;
        if (!IsServer) ParseData(_netData.Value.ToString());
        if (!IsServer) RequestCurrentState();
        RebuildRows();
        UpdateHostControls();
    }

    public override void OnNetworkDespawn()
    {
        ResetPendingInitiative();
        _stateAcks.Clear();
        if (NetworkManager.CustomMessagingManager != null)
        {
            NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StateMessage);
            NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(RequestMessage);
            NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(AckMessage);
        }
        _netData.OnValueChanged -= OnDataChanged;
        _hpById.Clear();
    }

    private void OnDataChanged(FixedString4096Bytes oldValue, FixedString4096Bytes newValue)
    {
        if (!IsServer) ParseData(newValue.ToString());
        RebuildRows();
    }

    private void Update()
    {
        if (IsSpawned && !IsServer && _receivedRevision < 0 && Time.unscaledTime >= _nextStateRequest)
            RequestCurrentState();
        bool connected = GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected;
        bool shouldShow = connected && _userVisible;
        if (_panel != null && _panel.activeSelf != shouldShow)
            _panel.SetActive(shouldShow);
        if (!connected && _addPanel != null) _addPanel.SetActive(false);

        if (connected) UpdateHostControls();
        if (_addPanel != null && _addPanel.activeSelf
            && Keyboard.current?.tabKey.wasPressedThisFrame == true)
        {
            int direction = Keyboard.current.shiftKey.isPressed ? -1 : 1;
            _focusedAddField = (_focusedAddField + direction + 3) % 3;
            StartCoroutine(FocusAfterFrame(AddField(_focusedAddField)));
        }
    }

    private void LateUpdate()
    {
        if (_addPanel == null || !_addPanel.activeSelf
            || Keyboard.current?.tabKey.wasPressedThisFrame == true) return;
        var selected = EventSystem.current?.currentSelectedGameObject;
        if (selected == _nameInput.gameObject) _focusedAddField = 0;
        else if (selected == _initInput.gameObject) _focusedAddField = 1;
        else if (selected == _hpInput.gameObject) _focusedAddField = 2;
    }

    private InputField AddField(int index) => index == 0 ? _nameInput
        : index == 1 ? _initInput : _hpInput;

    private IEnumerator FocusAfterFrame(InputField field)
    {
        yield return new WaitForEndOfFrame();
        if (_addPanel == null || !_addPanel.activeSelf || field == null) yield break;
        EventSystem.current?.SetSelectedGameObject(field.gameObject);
        field.ActivateInputField();
    }

    public void SetVisible(bool visible)
    {
        _userVisible = visible;
        if (_panel != null) _panel.SetActive(visible && GameNetworkManager.Instance != null
            && GameNetworkManager.Instance.IsConnected);
        if (!visible && _addPanel != null) _addPanel.SetActive(false);
    }

    public void ToggleVisible() => SetVisible(!_userVisible);

    private void UpdateHostControls()
    {
        bool host = IsHost;
        if (_hostButtons != null) _hostButtons.SetActive(host);
        if (!host && _addPanel != null) _addPanel.SetActive(false);
        if (host == _hostControlsVisible) return;
        _hostControlsVisible = host;
        RebuildRows();
    }

    private void ParseData(string data)
    {
        _entries.Clear();
        _currentIndex = 0;
        _round = 1;
        _activeParticipantId = null;
        _nextEntryId = 0;
        if (string.IsNullOrEmpty(data)) return;
        if (data.StartsWith("{", StringComparison.Ordinal))
        {
            var state = JsonUtility.FromJson<InitiativeNetworkState>(data);
            if (state == null || state.entries == null) return;
            _currentIndex = Mathf.Max(0, state.currentIndex);
            _round = state.round;
            _activeParticipantId = state.activeParticipantId;
            int entryLimit = Mathf.Min(state.entries.Length, SceneValidation.MaxTokens + 64);
            for (int index = 0; index < entryLimit; index++)
            {
                var item = state.entries[index];
                if (item == null) continue;
                _entries.Add(new Entry
                {
                    id = item.id,
                    name = ClampText(item.name, 28, "Участник"),
                    initiative = Mathf.Clamp(item.initiative, -999, 999),
                    colorHex = item.colorHex ?? "#FFFFFF",
                    playerId = item.playerId,
                    tokenId = item.tokenId,
                    publicHp = item.publicHp
                });
                _nextEntryId = Mathf.Max(_nextEntryId, item.id);
            }
            if (!string.IsNullOrEmpty(_activeParticipantId))
            {
                int active = _entries.FindIndex(entry => entry.id.ToString() == _activeParticipantId);
                if (active >= 0) _currentIndex = active;
            }
            if (_currentIndex >= _entries.Count) _currentIndex = 0;
            _activeParticipantId = ActiveId < 0 ? null : ActiveId.ToString();
            if (_round < 1 || _round > 100000) _round = 1;
            return;
        }

        // Legacy state: index|name,score,color,playerId[,entryId[,tokenId]]
        var parts = data.Split('|');
        if (int.TryParse(parts[0], out int current)) _currentIndex = current;
        for (int i = 1; i < parts.Length; i++)
        {
            var fields = parts[i].Split(',');
            if (fields.Length < 3) continue;
            int id = fields.Length >= 5 && int.TryParse(fields[4], out int parsedId)
                ? parsedId : ++_nextEntryId;
            _nextEntryId = Mathf.Max(_nextEntryId, id);
            _entries.Add(new Entry {
                id = id,
                name = fields[0],
                initiative = int.TryParse(fields[1], out int score) ? score : 0,
                colorHex = fields[2],
                playerId = fields.Length >= 4 && ulong.TryParse(fields[3], out ulong playerId)
                    ? playerId : ulong.MaxValue,
                tokenId = fields.Length >= 6 ? fields[5] : null
            });
        }
        if (_currentIndex >= _entries.Count) _currentIndex = 0;
        _activeParticipantId = ActiveId < 0 ? null : ActiveId.ToString();
        _round = 1;
    }

    private string SerializeData()
    {
        var state = new InitiativeNetworkState
        {
            currentIndex = _currentIndex,
            round = _round,
            activeParticipantId = ActiveId < 0 ? null : ActiveId.ToString(),
            entries = new InitiativeNetworkEntry[_entries.Count]
        };
        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            state.entries[i] = new InitiativeNetworkEntry
            {
                id = entry.id,
                name = entry.name,
                initiative = entry.initiative,
                colorHex = entry.colorHex,
                playerId = entry.playerId,
                tokenId = entry.tokenId,
                publicHp = GetPublicHp(entry)
            };
        }
        return JsonUtility.ToJson(state);
    }

    private void Sync()
    {
        if (!IsHost) return;
        string data = SerializeData();
        if (System.Text.Encoding.UTF8.GetByteCount(data)
            > MaxStateBytes)
        {
            Debug.LogError("[Initiative] State exceeds network string capacity");
            DiceUI.Instance?.ShowToolNotice("Список инициативы слишком большой.");
            return;
        }
        _stateRevision++;
        foreach (ulong client in NetworkManager.ConnectedClientsIds)
            if (client != Unity.Netcode.NetworkManager.ServerClientId) SendState(client, data);
        RebuildRows();
    }

    private void RequestCurrentState()
    {
        _nextStateRequest = Time.unscaledTime + 2f;
        using var writer = new FastBufferWriter(1, Unity.Collections.Allocator.Temp);
        NetworkManager.CustomMessagingManager.SendNamedMessage(RequestMessage, Unity.Netcode.NetworkManager.ServerClientId, writer);
    }

    private void ReceiveStateRequest(ulong client, FastBufferReader reader)
    {
        if (IsServer && NetworkManager.ConnectedClients.ContainsKey(client))
        {
            _stateAcks.Remove(client);
            SendState(client, SerializeData());
        }
    }

    private void ReceiveStateAck(ulong client, FastBufferReader reader)
    {
        if (!IsServer || !NetworkManager.ConnectedClients.ContainsKey(client) || !reader.TryBeginRead(sizeof(int))) return;
        reader.ReadValueSafe(out int revision);
        if (revision == _stateRevision) _stateAcks[client] = revision;
    }

    private void SendState(ulong client, string data)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(data);
        if (bytes.Length > MaxStateBytes) return;
        using var writer = new FastBufferWriter(bytes.Length + 8, Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(_stateRevision); writer.WriteValueSafe(bytes.Length); writer.WriteBytesSafe(bytes);
        NetworkManager.CustomMessagingManager.SendNamedMessage(StateMessage, client, writer, NetworkDelivery.ReliableFragmentedSequenced);
    }

    private void ReceiveState(ulong sender, FastBufferReader reader)
    {
        if (IsServer || sender != Unity.Netcode.NetworkManager.ServerClientId || !reader.TryBeginRead(8)) return;
        reader.ReadValueSafe(out int revision); reader.ReadValueSafe(out int length);
        if (revision < _receivedRevision || length <= 0 || length > MaxStateBytes || !reader.TryBeginRead(length)) return;
        if (revision > _receivedRevision)
        {
            var bytes = new byte[length]; reader.ReadBytesSafe(ref bytes, length);
            ParseData(System.Text.Encoding.UTF8.GetString(bytes));
            _receivedRevision = revision;
            RebuildRows();
        }
        using var ack = new FastBufferWriter(sizeof(int), Unity.Collections.Allocator.Temp);
        ack.WriteValueSafe(revision);
        NetworkManager.CustomMessagingManager.SendNamedMessage(AckMessage, Unity.Netcode.NetworkManager.ServerClientId, ack);
    }

    private int ActiveId => _entries.Count > 0 && _currentIndex < _entries.Count
        ? _entries[_currentIndex].id : -1;

    private void SortKeepingTurn(int activeId)
    {
        _entries.Sort((a, b) => {
            int byScore = b.initiative.CompareTo(a.initiative);
            return byScore != 0 ? byScore : a.id.CompareTo(b.id);
        });
        _currentIndex = Mathf.Max(0, _entries.FindIndex(e => e.id == activeId));
        _activeParticipantId = ActiveId < 0 ? null : ActiveId.ToString();
    }

    public void AddEntry(string name, int initiative, string colorHex = "#FFFFFF", int hp = 0)
    {
        AddEntryInternal(name, initiative, colorHex, ulong.MaxValue, hp);
    }

    private void AddEntryInternal(string name, int initiative, string colorHex, ulong playerId,
        int hp = 0, string tokenId = null)
    {
        if (!IsHost || _entries.Count >= SceneValidation.MaxTokens + 64 || string.IsNullOrWhiteSpace(name)) return;
        initiative = Mathf.Clamp(initiative, -999, 999);
        string cleanName = name.Trim();
        if (cleanName.Length > 28) cleanName = cleanName.Substring(0, 28);
        var candidate = new Entry { id = _nextEntryId + 1, name = cleanName,
            initiative = initiative, colorHex = colorHex, playerId = playerId, tokenId = tokenId };
        _entries.Add(candidate);
        if (System.Text.Encoding.UTF8.GetByteCount(SerializeData()) > MaxStateBytes)
        {
            _entries.RemoveAt(_entries.Count - 1);
            DiceUI.Instance?.ShowToolNotice("Список инициативы слишком большой.");
            return;
        }
        int activeId = ActiveId;
        int newId = ++_nextEntryId;
        _entries[_entries.Count - 1] = candidate;
        _hpById[newId] = Mathf.Clamp(hp, 0, 99999);
        SortKeepingTurn(activeId);
        Sync();
    }

    public bool ContainsPlayer(ulong playerId) =>
        _entries.Exists(e => e.playerId == playerId);

    public void AddPlayer(ulong playerId)
    {
        if (!IsHost || ContainsPlayer(playerId)) return;
        string name = PlayerColors.GetNickname(playerId) ?? $"Игрок {playerId}";
        string color = "#" + ColorUtility.ToHtmlStringRGB(PlayerColors.GetColor(playerId));
        AddEntryInternal(name, 0, color, playerId);
    }

    private int GetPublicHp(Entry entry)
    {
        if (!string.IsNullOrEmpty(entry.tokenId))
        {
            var token = TokenController.FindSceneToken(entry.tokenId);
            return token != null ? token.NetworkVisibleCurrentHp : -1;
        }
        return _hpById.TryGetValue(entry.id, out int hp) ? hp : 0;
    }

    private static string ClampText(string value, int maxLength, string fallback)
    {
        if (string.IsNullOrEmpty(value)) return fallback;
        return value.Length <= maxLength ? value : value.Substring(0, maxLength);
    }

    public bool ContainsToken(string sceneId) =>
        !string.IsNullOrEmpty(sceneId) && _entries.Exists(entry => entry.tokenId == sceneId);

    public void RefreshToken(string sceneId)
    {
        if (IsHost && ContainsToken(sceneId)) Sync();
    }

    public void AddToken(TokenController token)
    {
        if (!IsHost || token == null || !token.IsSpawned || ContainsToken(token.SceneId)) return;
        string color = "#" + ColorUtility.ToHtmlStringRGB(PlayerColors.GetColor(token.SpawnerClientId));
        AddEntryInternal(token.TokenName, 0, color, ulong.MaxValue, token.VisibleCurrentHp, token.SceneId);
    }

    public void RemoveToken(string sceneId)
    {
        if (!IsHost || string.IsNullOrEmpty(sceneId)) return;
        int index = _entries.FindIndex(entry => entry.tokenId == sceneId);
        if (index >= 0) RemoveEntry(index);
    }

    public void RemovePlayer(ulong playerId)
    {
        if (!IsHost) return;
        int index = _entries.FindIndex(e => e.playerId == playerId);
        RemoveEntry(index);
    }

    public void RemoveEntry(int index)
    {
        if (!IsHost || index < 0 || index >= _entries.Count) return;
        int activeId = ActiveId;
        _hpById.Remove(_entries[index].id);
        _entries.RemoveAt(index);
        _currentIndex = Mathf.Max(0, _entries.FindIndex(e => e.id == activeId));
        _activeParticipantId = ActiveId < 0 ? null : ActiveId.ToString();
        Sync();
    }

    private void RemoveEntryById(int id) => RemoveEntry(_entries.FindIndex(e => e.id == id));

    private void SetInitiativeById(int id, string value)
    {
        if (!IsHost || !int.TryParse(value, out int score)) { RebuildRows(); return; }
        score = Mathf.Clamp(score, -999, 999);
        int index = _entries.FindIndex(e => e.id == id);
        if (index < 0) return;
        if (_entries[index].initiative == score) return;
        int activeId = ActiveId;
        var entry = _entries[index];
        entry.initiative = score;
        _entries[index] = entry;
        SortKeepingTurn(activeId);
        Sync();
    }

    private void SetHpById(int id, string value)
    {
        if (!IsHost || !_entries.Exists(entry => entry.id == id)) return;
        if (int.TryParse(value, out int hp))
        {
            var entry = _entries.Find(item => item.id == id);
            if (!string.IsNullOrEmpty(entry.tokenId))
            {
                var token = TokenController.FindSceneToken(entry.tokenId);
                if (token != null)
                {
                    int maximum = token.VisibleMaxHp;
                    if (maximum == 0 && hp > 0) maximum = Mathf.Clamp(hp, 0, 999999);
                    token.ServerSetHealth(Mathf.Clamp(hp, 0, maximum), maximum);
                }
            }
            else
            {
                int clamped = Mathf.Clamp(hp, 0, 99999);
                int previous = _hpById.TryGetValue(id, out int oldHp) ? oldHp : 0;
                if (previous != clamped)
                {
                    GameMasterUndo.Record("ХП участника инициативы", () => SetStandaloneHpById(id, previous));
                    _hpById[id] = clamped;
                }
            }
        }
        RebuildRows();
    }

    private void SetStandaloneHpById(int id, int value)
    {
        if (!IsHost || !_entries.Exists(entry => entry.id == id)) return;
        _hpById[id] = Mathf.Clamp(value, 0, 99999);
        RebuildRows();
    }

    public void NextTurn()
    {
        if (!IsHost || _entries.Count == 0) return;
        _currentIndex = (_currentIndex + 1) % _entries.Count;
        if (_currentIndex == 0) _round = Mathf.Min(100000, _round + 1);
        _activeParticipantId = ActiveId.ToString();
        Sync();
    }

    public void ClearAll()
    {
        if (!IsHost) return;
        ResetPendingInitiative();
        _entries.Clear();
        _hpById.Clear();
        _currentIndex = 0;
        _round = 1;
        _activeParticipantId = null;
        Sync();
    }

    public SceneBattleState CaptureBattleState()
    {
        var participants = new BattleParticipant[_entries.Count];
        for (int index = 0; index < _entries.Count; index++)
        {
            var entry = _entries[index];
            participants[index] = new BattleParticipant
            {
                id = entry.id.ToString(),
                name = entry.name,
                colorHex = entry.colorHex,
                tokenId = entry.tokenId,
                playerId = entry.playerId,
                initiative = entry.initiative,
                hasHitPoints = string.IsNullOrEmpty(entry.tokenId),
                hitPoints = string.IsNullOrEmpty(entry.tokenId)
                    ? _hpById.GetValueOrDefault(entry.id) : 0
            };
        }
        return new SceneBattleState
        {
            round = _round,
            activeParticipantId = ActiveId < 0 ? null : ActiveId.ToString(),
            participants = participants
        };
    }

    public void RestoreBattleState(SceneBattleState battle)
    {
        if (!IsHost || battle == null || battle.participants == null) return;
        ResetPendingInitiative();
        _entries.Clear();
        _hpById.Clear();
        _round = Mathf.Clamp(battle.round, 1, 100000);
        _nextEntryId = 0;
        var restoredIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var participant in battle.participants)
        {
            int id = ++_nextEntryId;
            restoredIds[participant.id] = id;
            TokenController token = string.IsNullOrEmpty(participant.tokenId)
                ? null : TokenController.FindSceneToken(participant.tokenId);
            string name = token != null ? token.TokenName
                : !string.IsNullOrWhiteSpace(participant.name) ? participant.name
                : participant.playerId != ulong.MaxValue
                    ? PlayerColors.GetNickname(participant.playerId) ?? "Игрок"
                    : "Участник";
            string color = !string.IsNullOrWhiteSpace(participant.colorHex) ? participant.colorHex
                : "#" + ColorUtility.ToHtmlStringRGB(
                    PlayerColors.GetColor(token != null ? token.SpawnerClientId : participant.playerId));
            _entries.Add(new Entry
            {
                id = id,
                name = name,
                initiative = participant.initiative,
                colorHex = color,
                playerId = participant.playerId,
                tokenId = participant.tokenId
            });
            if (token != null) _hpById[id] = token.VisibleCurrentHp;
            else if (participant.hasHitPoints) _hpById[id] = Mathf.Clamp(participant.hitPoints, 0, 99999);
        }
        _entries.Sort((a, b) => {
            int byScore = b.initiative.CompareTo(a.initiative);
            return byScore != 0 ? byScore : a.id.CompareTo(b.id);
        });
        _activeParticipantId = !string.IsNullOrEmpty(battle.activeParticipantId)
            && restoredIds.TryGetValue(battle.activeParticipantId, out int activeId)
                ? activeId.ToString() : null;
        _currentIndex = _entries.FindIndex(entry => entry.id.ToString() == _activeParticipantId);
        if (_currentIndex < 0) _currentIndex = 0;
        Sync();
    }

    public void RequestClearAll()
    {
        if (!IsHost || _entries.Count == 0) return;
        DiceUI.Instance?.ConfirmAction("Очистить инициативу?",
            "Все участники будут удалены из трекера. Отменить удаление нельзя.", ClearAll);
    }

    private void BuildUI()
    {
        var root = new GameObject("InitiativeCanvas", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(transform, false);
        _canvas = root.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 20;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        _panel = Box(root.transform, "InitiativePanel", VttUiSkin.Panel, 10);
        Place(_panel, new Vector2(0.5f, 1), new Vector2(0.5f, 1),
            new Vector2(880, 102), new Vector2(0, -72));
        _panel.SetActive(false);
        Label(_panel.transform, "ИНИЦИАТИВА", 17, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(154, 32), new Vector2(18, -10), true);
        _roundText = Label(_panel.transform, "ОЧЕРЁДНОСТЬ ХОДОВ", 12, VttUiSkin.Muted,
            TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(290, 24), new Vector2(180, -14));

        _hostButtons = new GameObject("HostActions", typeof(RectTransform));
        _hostButtons.transform.SetParent(_panel.transform, false);
        Place(_hostButtons, new Vector2(1, 1), new Vector2(1, 1), new Vector2(350, 36), new Vector2(-48, -10));
        Button(_hostButtons.transform, "+ Добавить", 112, 32, 0, 0, ShowAddPanel, VttUiSkin.Button);
        Button(_hostButtons.transform, "Следующий ход  →", 146, 32, 118, 0, NextTurn,
            new Color(0.10f, 0.28f, 0.47f));
        Button(_hostButtons.transform, "Очистить", 78, 32, 270, 0, RequestClearAll,
            new Color(0.28f, 0.11f, 0.14f));
        Button(_hostButtons.transform, "Отмена броска", 104, 30, -2, -38,
            CancelInitiativeFromJournal, new Color(0.28f, 0.20f, 0.12f));

        var viewport = Box(_panel.transform, "Viewport", new Color(0, 0, 0, 0.01f), 0, false);
        _viewport = viewport;
        var viewportRt = viewport.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = new Vector2(12, 10);
        viewportRt.offsetMax = new Vector2(-12, -48);
        viewport.GetComponent<Image>().raycastTarget = false;
        viewport.AddComponent<Mask>().showMaskGraphic = false;

        var contentGo = new GameObject("Rows", typeof(RectTransform));
        contentGo.transform.SetParent(viewport.transform, false);
        _content = contentGo.GetComponent<RectTransform>();
        _content.anchorMin = new Vector2(0, 1);
        _content.anchorMax = new Vector2(0, 1);
        _content.pivot = new Vector2(0, 1);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(0, 70);

        var scroll = viewport.AddComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = _content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 28;
        viewportRt.offsetMax = new Vector2(-12, -82);

        _emptyMessage = new GameObject("EmptyMessage", typeof(RectTransform));
        _emptyMessage.transform.SetParent(viewport.transform, false);
        Place(_emptyMessage, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(980, 60), Vector2.zero);
        Label(_emptyMessage.transform, "Пока нет участников. DM может добавить игрока или персонажа.",
            15, VttUiSkin.Muted, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(980, 54), Vector2.zero);

        BuildAddPanel(root.transform);
        RebuildRows();
        UpdateHostControls();
    }

    private void RebuildRows()
    {
        if (_content == null) return;
        foreach (Transform child in _content) Destroy(child.gameObject);
        if (_emptyMessage != null) _emptyMessage.SetActive(_entries.Count == 0);
        if (_viewport != null) _viewport.SetActive(_entries.Count > 0);
        int rows = Mathf.CeilToInt(_entries.Count / (float)CardsPerRow);
        _content.sizeDelta = new Vector2(850, rows * CardStepY);
        if (_panel != null)
            _panel.GetComponent<RectTransform>().sizeDelta =
                new Vector2(880, _entries.Count == 0 ? 52 : Mathf.Min(82 + rows * CardStepY + 12, 82 + 4 * CardStepY + 12));
        if (_roundText != null)
            _roundText.text = _entries.Count == 0 ? $"Раунд {_round} · нет участников" :
                $"Раунд {_round} · {_entries.Count} участников · ход {_currentIndex + 1}";

        for (int i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            bool active = i == _currentIndex;
            var row = Box(_content, "Entry " + entry.id,
                active ? new Color(0.13f, 0.30f, 0.49f, 0.99f) : VttUiSkin.Raised, 8, active);
            Place(row, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CardWidth, CardHeight),
                new Vector2(i % CardsPerRow * CardStepX, -(i / CardsPerRow) * CardStepY));
            Label(row.transform, active ? "▶" : (i + 1).ToString(), 14,
                active ? VttUiSkin.Blue : VttUiSkin.Muted, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, 24),
                new Vector2(4, -4), active);
            int maxNameLength = IsHost ? 11 : 14;
            string liveName = string.IsNullOrEmpty(entry.tokenId)
                ? entry.name : TokenController.FindSceneToken(entry.tokenId)?.TokenName ?? entry.name;
            string displayName = liveName.Length > maxNameLength
                ? liveName.Substring(0, maxNameLength - 1) + "…" : liveName;
            Label(row.transform, displayName, 13, VttUiSkin.Text, TextAnchor.MiddleLeft,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(IsHost ? 100 : 122, 25),
                new Vector2(32, -3), active);
            if (IsHost)
            {
                var linkedToken = string.IsNullOrEmpty(entry.tokenId)
                    ? null : TokenController.FindSceneToken(entry.tokenId);
                var input = Input(row.transform, entry.initiative.ToString(), 44, 22, 24, -25);
                input.contentType = InputField.ContentType.IntegerNumber;
                int id = entry.id;
                input.onEndEdit.AddListener(value => SetInitiativeById(id, value));
                if (!string.IsNullOrEmpty(entry.tokenId))
                {
                    string tokenId = entry.tokenId;
                    Button(row.transform, "d20", 28, 20, 75, -25,
                        () => ArmInitiativeFromJournal(tokenId), new Color(0.12f, 0.26f, 0.38f));
                }
                Label(row.transform, "HP", 10, new Color(1f, 0.36f, 0.38f),
                    TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(18, 22), new Vector2(82, -25), true);
                int hp = linkedToken != null ? linkedToken.VisibleCurrentHp : _hpById.GetValueOrDefault(id);
                var hpInput = Input(row.transform, hp.ToString(),
                    58, 22, 100, -25);
                hpInput.contentType = InputField.ContentType.IntegerNumber;
                hpInput.textComponent.color = new Color(1f, 0.36f, 0.38f);
                hpInput.onEndEdit.AddListener(value => SetHpById(id, value));
                Button(row.transform, "×", 22, 20, 137, -3,
                    () => RemoveEntryById(id), new Color(0.28f, 0.11f, 0.14f));
            }
            else
            {
                Label(row.transform, entry.initiative.ToString(), 14, VttUiSkin.Text,
                    TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
                    new Vector2(90, 22), new Vector2(32, -25), true);
                if (entry.publicHp >= 0)
                    Label(row.transform, "HP " + entry.publicHp, 11, VttUiSkin.Muted,
                        TextAnchor.MiddleRight, new Vector2(1, 1), new Vector2(1, 1),
                        new Vector2(52, 22), new Vector2(-5, -25));
            }
        }
    }

    private void BuildAddPanel(Transform parent)
    {
        _addPanel = Box(parent, "AddParticipant", VttUiSkin.Panel, 12);
        Place(_addPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(414, 216), Vector2.zero);
        _addPanel.SetActive(false);
        Label(_addPanel.transform, "Добавить участника", 18, VttUiSkin.Text,
            TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(380, 28), new Vector2(18, -14), true);
        Label(_addPanel.transform, "Имя", 13, VttUiSkin.Muted, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(120, 22), new Vector2(18, -54));
        _nameInput = Input(_addPanel.transform, "", 246, 32, 150, -51, "Например, гоблин");
        Label(_addPanel.transform, "Инициатива", 13, VttUiSkin.Muted, TextAnchor.MiddleLeft,
            new Vector2(0, 1), new Vector2(0, 1), new Vector2(125, 22), new Vector2(18, -96));
        _initInput = Input(_addPanel.transform, "0", 74, 32, 150, -93);
        _initInput.contentType = InputField.ContentType.IntegerNumber;
        Label(_addPanel.transform, "HP", 13, new Color(1f, 0.36f, 0.38f),
            TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(125, 22), new Vector2(18, -138));
        _hpInput = Input(_addPanel.transform, "0", 100, 32, 150, -135);
        _hpInput.contentType = InputField.ContentType.IntegerNumber;
        _hpInput.textComponent.color = new Color(1f, 0.36f, 0.38f);
        _addErrorText = Label(_addPanel.transform, "", 12,
            VttUiSkin.Muted, TextAnchor.MiddleLeft, new Vector2(0, 1), new Vector2(0, 1),
            new Vector2(365, 20), new Vector2(18, -166));
        Button(_addPanel.transform, "Отмена", 92, 34, 18, -178,
            () => _addPanel.SetActive(false), VttUiSkin.Button);
        Button(_addPanel.transform, "Добавить", 112, 34, 284, -178,
            SubmitAdd, new Color(0.10f, 0.30f, 0.48f));
    }

    public void ShowAddPanel()
    {
        if (!IsHost || _addPanel == null) return;
        _addErrorText.text = "";
        SetVisible(true);
        _addPanel.SetActive(true);
        _focusedAddField = 0;
        StartCoroutine(FocusAfterFrame(_nameInput));
    }

    private void SubmitAdd()
    {
        if (!IsHost) return;
        if (string.IsNullOrWhiteSpace(_nameInput.text))
        {
            _addErrorText.text = "Введите имя участника.";
            StartCoroutine(FocusAfterFrame(_nameInput));
            return;
        }
        if (!int.TryParse(_initInput.text, out int score))
        {
            _addErrorText.text = "Укажите инициативу числом.";
            StartCoroutine(FocusAfterFrame(_initInput));
            return;
        }
        if (!int.TryParse(_hpInput.text, out int hp) || hp < 0)
        {
            _addErrorText.text = "Укажите HP неотрицательным числом.";
            StartCoroutine(FocusAfterFrame(_hpInput));
            return;
        }
        AddEntry(_nameInput.text, Mathf.Clamp(score, -999, 999), "#FFFFFF",
            Mathf.Clamp(hp, 0, 99999));
        _nameInput.text = "";
        _initInput.text = "0";
        _hpInput.text = "0";
        _addPanel.SetActive(false);
    }

    private GameObject Box(Transform parent, string name, Color color, int radius, bool outline = true)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        VttUiSkin.Surface(go.GetComponent<Image>(), color, radius, outline);
        return go;
    }

    private static void Place(GameObject go, Vector2 anchor, Vector2 pivot, Vector2 size, Vector2 pos)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.sizeDelta = size;
        rt.anchoredPosition = pos;
    }

    private Text Label(Transform parent, string value, int size, Color color, TextAnchor align,
        Vector2 anchor, Vector2 pivot, Vector2 dimensions, Vector2 position, bool bold = false)
    {
        var go = new GameObject("Label", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Place(go, anchor, pivot, dimensions, position);
        var text = go.GetComponent<Text>();
        text.font = _font;
        text.text = value;
        text.fontSize = size;
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.color = color;
        text.alignment = align;
        text.raycastTarget = false;
        return text;
    }

    private GameObject Button(Transform parent, string title, float width, float height,
        float x, float y, Action click, Color color)
    {
        var go = new GameObject("Button " + title, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        Place(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(width, height), new Vector2(x, y));
        VttUiSkin.ButtonStyle(go.GetComponent<Image>(), color, 7);
        go.GetComponent<Button>().onClick.AddListener(() => click());
        if (!string.IsNullOrEmpty(title))
            Label(go.transform, title, 13, VttUiSkin.Text, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(width - 4, height - 4), Vector2.zero, true);
        return go;
    }

    private InputField Input(Transform parent, string value, float width, float height,
        float x, float y, string hint = "")
    {
        var go = new GameObject("Input", typeof(RectTransform), typeof(Image), typeof(InputField));
        go.transform.SetParent(parent, false);
        Place(go, new Vector2(0, 1), new Vector2(0, 1), new Vector2(width, height), new Vector2(x, y));
        VttUiSkin.Surface(go.GetComponent<Image>(), VttUiSkin.Raised, 6);
        var text = Label(go.transform, value, 14, VttUiSkin.Text, TextAnchor.MiddleLeft,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 16, height - 4), Vector2.zero);
        text.raycastTarget = false;
        var placeholder = Label(go.transform, hint, 13, VttUiSkin.Muted,
            TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(width - 16, height - 4), Vector2.zero);
        var field = go.GetComponent<InputField>();
        field.textComponent = text;
        field.placeholder = placeholder;
        field.text = value;
        return field;
    }

    private new void OnDestroy()
    {
        DiceUI.JournalResultRecorded -= OnJournalResultRecorded;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        if (Instance == this) Instance = null;
    }
}
