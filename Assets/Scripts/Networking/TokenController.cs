using System;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Сетевой токен. Права: IsSpawner — свои; IsHost — любые (картинка, удаление, копирование).
/// Перемещение проходит через сервер; создатель и хост управляют меню.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class TokenController : NetworkDraggable
{
    [Header("Portrait")]
    public GameObject portraitQuad; // Quad для картинки

    [Header("Appearance")]
    public Color defaultColor = new Color(0.4f, 0.45f, 0.55f);

    private Material _bodyMaterial;
    private Material _portraitMaterial;
    private Texture2D _portraitTexture;
    private Vector2Int _currentCell = new(-1, -1);
    private TokenAppearance _appearance;
    private Bounds _labelBounds;
    private GridManager _labelGrid;
    private GUIStyle _labelStyle;
    private GUIStyle _statsLabelStyle;
    private bool _hasInitialServerState;
    private ulong _initialCreator;
    private bool _initialHero;
    private bool _initialHidden;
    private string _initialName;
    private string _nameBase;
    private int _initialVision;
    private ulong _initialController = ulong.MaxValue;
    private bool _initialShared;
    public string SavedOwnerNickname { get; private set; }
    private Vector3 _dragStartCommitted;
    private bool? _lastFogVisible;
    private readonly NetworkVariable<Vector3> _netCommitted = new(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<ulong> _netController = new(ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _netSharedMove = new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public Vector3 CommittedPosition => _netCommitted.Value;
    public ulong ControllerClientId => _netController.Value;
    public bool EveryoneCanMove => _netSharedMove.Value;
    public string SceneId { get; private set; } = Guid.NewGuid().ToString("N");
    private readonly NetworkVariable<FixedString128Bytes> _netName = new(new FixedString128Bytes(""),
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public const int MaxVisionFeet = 1000;
    private readonly NetworkVariable<bool> _netHero = new(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _netHidden = new(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _netVisionFeet = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _netVisibleCurrentHp = new(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _netVisibleMaxHp = new(-1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> _netArmorClass = new(10,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<FixedString4096Bytes> _netVisibleConditions = new(
        new FixedString4096Bytes(""), NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private MasterTokenData _masterData = new() { armorClass = 10, hideHp = true, hideConditions = true };

    public bool IsHero => _netHero.Value;
    public string TokenName => _netName.Value.IsEmpty ? (IsHero ? "Герой" : "Токен") : _netName.Value.ToString();
    public string NameBase => string.IsNullOrEmpty(_nameBase) ? TokenName : _nameBase;
    public bool IsHidden => _netHidden.Value;
    public int VisionFeet => _netVisionFeet.Value;
    public int VisibleCurrentHp => IsServer ? _masterData.currentHp : _netVisibleCurrentHp.Value;
    public int VisibleMaxHp => IsServer ? _masterData.maxHp : _netVisibleMaxHp.Value;
    public int ArmorClass => IsServer ? _masterData.armorClass : _netArmorClass.Value;
    public string[] VisibleConditionIds => IsServer ? (string[])_masterData.conditionIds.Clone()
        : string.IsNullOrEmpty(_netVisibleConditions.Value.ToString())
            ? Array.Empty<string>() : _netVisibleConditions.Value.ToString().Split('|');
    public bool IsVisibleToLocalPlayer => FogManager.Instance != null && FogManager.Instance.ShowingPlayerView
        ? FogManager.Instance.CanSeeToken(NetworkManager.LocalClientId, this, IsHost && FogManager.Instance.Preview) : !IsHidden || IsHost;
    protected override bool CanStartLocalDrag => IsVisibleToLocalPlayer && CanControlClient(NetworkManager.LocalClientId)
        && !(IsHost && FogManager.Instance?.Preview == true);
    protected override bool CanClientStartDrag(ulong client) =>
        CanControlClient(client) && (FogManager.Instance == null || FogManager.Instance.CanSeeToken(client, this));
    public bool CanControlClient(ulong client) => NetworkPermissions.IsHostClient(client)
        || !IsHidden && (ControllerClientId == client || EveryoneCanMove);

    /// <summary>Called by the host before NetworkShow can send an initial snapshot.</summary>
    public void InitializeServerState(ulong creator, bool hero, bool hidden, string tokenName, string nameBase,
        int vision = 0, string sceneId = null, ulong? controller = null, bool shared = false, string savedOwner = null)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || IsSpawned) return;
        // Apply in OnNetworkSpawn, after NGO initializes variable serializers/permissions.
        // TokenManager spawns without observers and queues NetworkShow only after this callback.
        _hasInitialServerState = true;
        _initialCreator = creator;
        _initialHero = hero;
        _initialHidden = hidden;
        _initialName = tokenName;
        _nameBase = nameBase;
        _initialVision = vision;
        _initialController = controller ?? creator; _initialShared = shared; SavedOwnerNickname = savedOwner;
        if (!string.IsNullOrEmpty(sceneId)) SceneId = sceneId;
    }

    private NetworkVariable<Vector3> _netColor = new(Vector3.one,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<ulong> _netSpawnerClientId = new(
        ulong.MaxValue, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public ulong SpawnerClientId => _netSpawnerClientId.Value;

    public bool IsSpawner =>
        NetworkManager.Singleton != null
        && ControllerClientId != ulong.MaxValue
        && NetworkManager.Singleton.LocalClientId == ControllerClientId;

    public static TokenController Instance { get; private set; }

    private void Awake()
    {
        var mesh = GetComponent<MeshFilter>()?.sharedMesh;
        _labelBounds = mesh != null ? mesh.bounds : new Bounds(Vector3.zero, Vector3.one);
        _bodyMaterial = GetComponent<MeshRenderer>()?.material;

        if (portraitQuad != null)
        {
            var mr = portraitQuad.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                _portraitMaterial = mr.material;
                _portraitMaterial.color = Color.white;
            }
        }
        _appearance = new TokenAppearance(transform);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Применить цвет при спавне (для late-join)
        _netColor.OnValueChanged += OnColorChanged;
        _netHidden.OnValueChanged += OnHiddenChanged;
        _netVisionFeet.OnValueChanged += OnVisionChanged;
        ApplyColor(_netColor.Value);

        if (IsServer)
        {
            bool initialized = _hasInitialServerState;
            if (_hasInitialServerState)
            {
                _netSpawnerClientId.Value = _initialCreator;
                _netHero.Value = _initialHero;
                _netHidden.Value = _initialHidden;
                _netVisionFeet.Value = _initialVision;
                _netName.Value = new FixedString128Bytes(_initialName);
                _netController.Value = _initialController; _netSharedMove.Value = _initialShared;
                _hasInitialServerState = false;
            }
            if (_netSpawnerClientId.Value == ulong.MaxValue)
                _netSpawnerClientId.Value = OwnerClientId;
            if (!initialized && _netController.Value == ulong.MaxValue && string.IsNullOrEmpty(SavedOwnerNickname))
                _netController.Value = _netSpawnerClientId.Value;
            _netCommitted.Value = transform.position;
            PublishMasterData();
            ServerRefreshPlayerColor();
        }

        TokenImageSync.EnsureInstance();
        TokenImageSync.TryApplyPending(this);
        ApplyAppearance();
        StartCoroutine(RegisterCellAfterSpawn());
        FogManager.Instance?.MarkDirty();
    }

    private System.Collections.IEnumerator RegisterCellAfterSpawn()
    {
        // NetworkTransform's initial position is available after the spawn frame.
        yield return null;
        if (!IsSpawned || IsHidden || _currentCell.x >= 0) yield break;
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) yield break;
        Vector2Int cell = gm.GetGridPosition(transform.position);
        if (gm.OccupyCell(cell)) _currentCell = cell;
    }

    /// <summary>Rebuild local snap occupancy after the map changes its grid bounds.</summary>
    public static void RebuildCellOccupancy()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;
        gm.ClearOccupiedCells();
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (token == null || !token.IsSpawned) continue;
            token._currentCell = new Vector2Int(-1, -1);
            if (token.IsHidden) continue;
            Vector2Int cell = gm.GetGridPosition(token.transform.position);
            if (gm.OccupyCell(cell)) token._currentCell = cell;
        }
    }

    /// <summary>Server: update spawner after reconnect ownership transfer.</summary>
    public void ServerUpdateSpawnerClientId(ulong clientId)
    {
        if (!IsServer) return;
        ulong previous = _netSpawnerClientId.Value;
        _netSpawnerClientId.Value = clientId;
        if (_netController.Value == previous) _netController.Value = clientId;
    }

    /// <summary>Server: sync baked token color from player registry to all clients.</summary>
    public void ServerRefreshPlayerColor()
    {
        if (!IsServer) return;

        Color c = PlayerRegistry.GetServerPlayerColor(
            _netSpawnerClientId.Value != ulong.MaxValue ? _netSpawnerClientId.Value : OwnerClientId);
        var rgb = new Vector3(c.r, c.g, c.b);
        _netColor.Value = rgb;
        ApplyColor(rgb);
        RefreshColorClientRpc(rgb);
    }

    [Rpc(SendTo.Everyone)]
    private void RefreshColorClientRpc(Vector3 rgb)
    {
        ApplyColor(rgb);
    }

    private void ApplyColor(Vector3 rgb)
    {
        if (_portraitMaterial != null && _portraitTexture == null)
            _portraitMaterial.color = new Color(rgb.x, rgb.y, rgb.z, 1f);
        ApplyAppearance();
    }

    private void OnColorChanged(Vector3 previous, Vector3 current) => ApplyColor(current);
    private void OnVisionChanged(int previous, int current)
    {
        _visionInput = current.ToString();
        _visionError = null;
        FogManager.Instance?.MarkDirty();
    }
    private void OnHiddenChanged(bool previous, bool current)
    {
        var grid = FindAnyObjectByType<GridManager>();
        if (grid != null)
        {
            grid.ReleaseCell(_currentCell);
            _currentCell = new Vector2Int(-1, -1);
            // Revealing must not push either token away from an intentionally shared cell.
            if (!current)
            {
                Vector2Int cell = grid.GetGridPosition(transform.position);
                if (grid.OccupyCell(cell)) _currentCell = cell;
            }
        }
        ApplyAppearance();
        FogManager.Instance?.MarkDirty();
    }

    private void ApplyAppearance()
    {
        if (!IsVisibleToLocalPlayer)
        {
            CloseMenu();
            GetComponent<NetworkDragPreview>()?.Stop();
        }
        _lastFogVisible = IsVisibleToLocalPlayer;
        _appearance?.ApplyWithFog(IsHidden, IsHost, IsVisibleToLocalPlayer);
    }

    public void RefreshFogAppearance()
    {
        if (_lastFogVisible != IsVisibleToLocalPlayer) ApplyAppearance();
    }

    public static TokenController FindSceneToken(string id)
    {
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
            if (token.IsSpawned && token.SceneId == id) return token;
        return null;
    }
    public void AssignController(ulong client)
    {
        if (!IsServer || !IsHost || client != ulong.MaxValue && !NetworkManager.ConnectedClients.ContainsKey(client)) return;
        string id = SceneId; ulong previous = ControllerClientId; string previousName = SavedOwnerNickname;
        GameMasterUndo.Record("назначение токена", () => { var token = FindSceneToken(id); if (token != null) {
            ulong target = previous == ulong.MaxValue || NetworkManager.ConnectedClients.ContainsKey(previous) ? previous : ulong.MaxValue;
            token.AssignController(target); token.SavedOwnerNickname = previousName;
        } });
        AbortServerDrag(); _netController.Value = client;
        SavedOwnerNickname = client != ulong.MaxValue && client != NetworkManager.ServerClientId ? PlayerColors.GetNickname(client) : null;
        FogManager.Instance?.MarkDirty();
    }
    public void SetEveryoneCanMove(bool shared)
    {
        if (!IsServer || !IsHost) return;
        string id = SceneId; bool previous = EveryoneCanMove;
        GameMasterUndo.Record("права перемещения", () => FindSceneToken(id)?.SetEveryoneCanMove(previous));
        AbortServerDrag(); _netSharedMove.Value = shared;
    }
    public void SetHero(bool hero)
    {
        if (!IsServer || !IsHost || IsHero == hero) return;
        string id = SceneId; bool previous = IsHero;
        GameMasterUndo.Record("статус героя", () => FindSceneToken(id)?.SetHero(previous));
        _netHero.Value = hero; FogManager.Instance?.MarkDirty();
    }
    public void ServerRestoreAssignment(ulong client)
    {
        if (IsServer) { _netController.Value = client; _netSpawnerClientId.Value = client; }
    }

    public MasterTokenData CaptureMasterData()
    {
        if (!IsServer) return null;
        return new MasterTokenData
        {
            tokenId = SceneId,
            currentHp = _masterData.currentHp,
            maxHp = _masterData.maxHp,
            armorClass = _masterData.armorClass,
            hideHp = _masterData.hideHp,
            hideConditions = _masterData.hideConditions,
            conditionIds = (string[])_masterData.conditionIds.Clone(),
            statBlockId = _masterData.statBlockId
        };
    }

    public void ServerApplyMasterData(MasterTokenData data)
    {
        if (!IsServer) return;
        _masterData = data == null
            ? new MasterTokenData { tokenId = SceneId, armorClass = 10, hideHp = true, hideConditions = true }
            : new MasterTokenData
            {
                tokenId = SceneId,
                currentHp = Mathf.Clamp(data.currentHp, 0, 999999),
                maxHp = Mathf.Clamp(data.maxHp, 0, 999999),
                armorClass = Mathf.Clamp(data.armorClass, 0, 999),
                hideHp = data.hideHp,
                hideConditions = data.hideConditions,
                conditionIds = data.conditionIds == null ? Array.Empty<string>() : (string[])data.conditionIds.Clone(),
                statBlockId = data.statBlockId
            };
        _masterData.currentHp = Mathf.Min(_masterData.currentHp, _masterData.maxHp);
        PublishMasterData();
    }

    public bool ServerSetHealth(int current, int maximum)
    {
        if (!IsServer || current < 0 || current > 999999 || maximum < 0 || maximum > 999999
            || current > maximum) return false;
        _masterData.currentHp = current;
        _masterData.maxHp = maximum;
        PublishMasterData();
        InitiativeTracker.Instance?.RefreshToken(SceneId);
        return true;
    }

    public bool ServerSetArmorClass(int armorClass)
    {
        if (!IsServer || armorClass < 0 || armorClass > 999) return false;
        _masterData.armorClass = armorClass;
        PublishMasterData();
        return true;
    }

    public bool ServerSetStatBlock(string statBlockId)
    {
        if (!IsServer || statBlockId == null || statBlockId.Length > 64
            || statBlockId.Contains("|") || statBlockId.Contains(",")) return false;
        _masterData.statBlockId = string.IsNullOrWhiteSpace(statBlockId) ? null : statBlockId;
        return true;
    }

    public void ServerSetMasterVisibility(bool hideHp, bool hideConditions)
    {
        if (!IsServer) return;
        _masterData.hideHp = hideHp;
        _masterData.hideConditions = hideConditions;
        PublishMasterData();
    }

    public bool ServerToggleCondition(string conditionId)
    {
        if (!IsServer || string.IsNullOrWhiteSpace(conditionId) || conditionId.Length > 64
            || conditionId.Contains("|")) return false;
        var conditions = new System.Collections.Generic.List<string>(_masterData.conditionIds ?? Array.Empty<string>());
        int existing = conditions.IndexOf(conditionId);
        if (existing >= 0) conditions.RemoveAt(existing);
        else
        {
            if (conditions.Count >= 64) return false;
            conditions.Add(conditionId);
        }
        string encoded = string.Join("|", conditions);
        if (Encoding.UTF8.GetByteCount(encoded) > FixedString4096Bytes.UTF8MaxLengthInBytes) return false;
        _masterData.conditionIds = conditions.ToArray();
        PublishMasterData();
        return true;
    }

    private void PublishMasterData()
    {
        if (!IsServer || _masterData == null) return;
        _netVisibleCurrentHp.Value = _masterData.hideHp ? -1 : _masterData.currentHp;
        _netVisibleMaxHp.Value = _masterData.hideHp ? -1 : _masterData.maxHp;
        _netArmorClass.Value = _masterData.armorClass;
        string conditions = _masterData.hideConditions || _masterData.conditionIds == null
            ? string.Empty : string.Join("|", _masterData.conditionIds);
        _netVisibleConditions.Value = new FixedString4096Bytes(conditions);
    }

    public void RequestSetHidden(bool hidden)
    {
        if (!IsSpawned || !IsHost) return;
        SetHiddenServerRpc(hidden);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetHiddenServerRpc(bool hidden, RpcParams rpcParams = default)
    {
        if (!NetworkPermissions.IsHostClient(rpcParams.Receive.SenderClientId)) return;
        if (_netHidden.Value == hidden) return;
        string id = SceneId; bool previous = IsHidden; var memory = FogManager.Instance?.Capture(true);
        GameMasterUndo.Record("скрытие токена", () => { FindSceneToken(id)?.RequestSetHidden(previous); if (memory != null) FogManager.Instance?.Restore(memory, false); });
        // Remove a player's lease before hiding; subsequent stale moves/finishes cannot move this token.
        if (hidden && DragLease.IsHeld && !NetworkPermissions.IsHostClient(DragLease.Controller))
            AbortServerDrag();
        _netHidden.Value = hidden;
    }

    public void RequestSetVision(int feet)
    {
        if (!IsSpawned || !IsHost || feet < 0 || feet > MaxVisionFeet) return;
        SetVisionServerRpc(feet);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetVisionServerRpc(int feet, RpcParams rpcParams = default)
    {
        if (!NetworkPermissions.IsHostClient(rpcParams.Receive.SenderClientId)
            || feet < 0 || feet > MaxVisionFeet) return;
        if (_netVisionFeet.Value == feet) return;
        string id = SceneId; int previous = VisionFeet; var memory = FogManager.Instance?.Capture(true);
        GameMasterUndo.Record("зрение токена", () => { FindSceneToken(id)?.RequestSetVision(previous); if (memory != null) FogManager.Instance?.Restore(memory, false); });
        _netVisionFeet.Value = feet;
    }

    protected override void OnServerDragStarted() { _dragStartCommitted = CommittedPosition; }

    protected override void OnServerDragFinished(Vector3 velocity, Vector3 spin, bool cancel)
    {
        if (cancel) { RestorePosition(_dragStartCommitted); return; }
        SnapAuthoritative();
        if (!cancel && (_dragStartCommitted - transform.position).sqrMagnitude > 0.000001f && NetworkPermissions.IsHostClient(FinishingDragClient))
        {
            string id = SceneId; Vector3 previous = _dragStartCommitted; var memory = FogManager.Instance?.Capture(true);
            GameMasterUndo.Record("перемещение токена", () => { FindSceneToken(id)?.RestorePosition(previous); if (memory != null) FogManager.Instance?.Restore(memory, false); });
        }
        _netCommitted.Value = transform.position; FogManager.Instance?.MarkDirty();
    }

    protected override void OnServerDragAborted()
    {
        RestorePosition(CommittedPosition);
    }

    public void RestorePosition(Vector3 position)
    {
        if (!IsServer) return;
        AbortServerDrag(); transform.position = position; _netCommitted.Value = position;
        GetComponent<NetworkDragTransform>().Teleport(position, transform.rotation, transform.localScale);
        SnapResultClientRpc(position); FogManager.Instance?.MarkDirty();
    }

    private void Update()
    {
        UpdateDragState();
        if (_showMenu && Keyboard.current?.escapeKey.wasPressedThisFrame == true) CloseMenu();
        if (_showMenu && (SceneEditor.IsEditing || FogManager.IsManualEditing || !IsVisibleToLocalPlayer || !GameplayInputGate.AllowsImGuiOverlays)) CloseMenu();
    }

    /// <summary>Загружает картинку на токен (создатель или хост).</summary>
    public void LoadImage(byte[] jpgData)
    {
        if (!IsVisibleToLocalPlayer || (!IsSpawner && !IsHost) || jpgData == null || jpgData.Length == 0) return;
        if (jpgData.Length > TokenImageSync.MaxPortraitBytes)
        {
            DiceUI.Instance?.ShowToolNotice("Изображение токена слишком большое (максимум 2 МБ).");
            return;
        }

        byte[] previousImage = !IsServer ? GetPortraitJpg() : null;
        if (!ApplyImageLocal(jpgData)) return;
        if (!IsSpawned) return;

        TokenImageSync.EnsureInstance();
        ulong netId = NetworkObjectId;

        if (IsServer)
        {
            TokenImageSync.BroadcastImage(netId, jpgData);
        }
        else
            TokenImageSync.UploadToServer(netId, jpgData, previousImage);
    }

    public void RestoreImageLocal(byte[] previousImage)
    {
        if (previousImage != null && previousImage.Length > 0)
        {
            ApplyImageLocal(previousImage);
            return;
        }
        if (_portraitTexture != null) Destroy(_portraitTexture);
        _portraitTexture = null;
        if (_portraitMaterial == null) return;
        _portraitMaterial.mainTexture = null;
        var color = _netColor.Value;
        _portraitMaterial.color = new Color(color.x, color.y, color.z, 1f);
        ApplyAppearance();
    }

    /// <summary>Клиент получает картинку от создателя.</summary>
    public bool ApplyImageLocal(byte[] pngData)
    {
        if (pngData == null || pngData.Length == 0) return false;

        Texture2D tex = new Texture2D(2, 2);
        if (!tex.LoadImage(pngData))
        {
            Destroy(tex);
            Debug.LogError("[Token] Failed to load image");
            return false;
        }

        if (_portraitTexture != null)
            Destroy(_portraitTexture);
        _portraitTexture = tex;

        if (_portraitMaterial != null)
        {
            _portraitMaterial.mainTexture = tex;
            _portraitMaterial.color = Color.white;
            Debug.Log($"[Token] Image applied: {tex.width}x{tex.height}");
        }
        ApplyAppearance();
        return true;
    }

    // ═══ Снап к сетке ═══

    /// <summary>Снап при создании токена на сервере.</summary>
    public void SnapToGrid()
    {
        if (IsServer) { SnapAuthoritative(); _netCommitted.Value = transform.position; FogManager.Instance?.MarkDirty(); }
    }

    private void SnapAuthoritative()
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm == null) return;

        Vector2Int desired = gm.GetGridPosition(transform.position);
        if (desired.x < 0) return;

        Vector2Int previous = _currentCell;
        gm.ReleaseCell(_currentCell);
        _currentCell = new Vector2Int(-1, -1);
        Vector2Int target = IsHidden ? desired : gm.FindNearestFreeCell(desired, maxRadius: 3);
        if (!IsHidden && !gm.TryOccupyCell(target))
        {
            // A full neighbourhood must not place two tokens in the same cell.
            if (!gm.OccupyCell(previous)) return;
            target = previous;
        }
        if (!IsHidden) _currentCell = target;

        Vector3 snapped = gm.GetCellCenter(target.x, target.y, transform.position.y);
        transform.position = snapped;
        if (IsSpawned) SnapResultClientRpc(snapped);
    }

    [Rpc(SendTo.Everyone)]
    private void SnapResultClientRpc(Vector3 snappedPos)
    {
        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null)
        {
            gm.ReleaseCell(_currentCell);
            _currentCell = new Vector2Int(-1, -1);
            Vector2Int cell = gm.GetGridPosition(snappedPos);
            if (!IsHidden && gm.OccupyCell(cell))
            {
                _currentCell = cell;
            }
        }
        // Transform delivery (including interpolation reset) belongs to NetworkDragTransform.
    }

    // ═══ Удаление ═══

    /// <summary>Запросить удаление токена (владелец или хост).</summary>
    public void RequestDespawn()
    {
        if (!IsSpawned) return;
        if (!IsVisibleToLocalPlayer) return;
        if (!IsSpawner && !IsHost) return;
        RequestDespawnServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestDespawnServerRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (NetworkPermissions.CanUploadTokenPortrait(sender, this))
        {
            if (NetworkPermissions.IsHostClient(sender)) RecordDeletion();
            GetComponent<NetworkObject>().Despawn();
        }
    }

    public void RecordDeletion()
    {
        var grid = FindAnyObjectByType<GridManager>(); if (grid == null) return;
        var data = SceneFileStore.CaptureToken(this, grid);
        var masterData = CaptureMasterData();
        GameMasterUndo.Record("удаление токена", () => {
            var token = TokenManager.Instance?.RestoreSceneToken(data, grid, masterData);
            if (token != null && !string.IsNullOrEmpty(data.portrait)) token.LoadImage(Convert.FromBase64String(data.portrait));
        });
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) InitiativeTracker.Instance?.RemoveToken(SceneId);
        _netColor.OnValueChanged -= OnColorChanged;
        _netHidden.OnValueChanged -= OnHiddenChanged;
        _netVisionFeet.OnValueChanged -= OnVisionChanged;
        CloseMenu();
        base.OnNetworkDespawn();
        FogManager.Instance?.MarkDirty();
        TokenImageSync.RemovePortrait(NetworkObjectId);

        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null && _currentCell.x >= 0)
            gm.ReleaseCell(_currentCell);
        _currentCell = new(-1, -1);
    }

    public override void OnDestroy()
    {
        CloseMenu();
        _appearance?.Dispose();
        if (_bodyMaterial != null) Destroy(_bodyMaterial);
        if (_portraitMaterial != null) Destroy(_portraitMaterial);
        if (_portraitTexture != null) Destroy(_portraitTexture);
        var gm = FindAnyObjectByType<GridManager>();
        if (gm != null && _currentCell.x >= 0)
            gm.ReleaseCell(_currentCell);
        base.OnDestroy();
    }

    // ═══ Копирование ═══

    public void RequestCopy()
    {
        if (!IsSpawned) return;
        if (!IsVisibleToLocalPlayer) return;
        if (!IsSpawner && !IsHost) return;

        if (IsServer)
        {
            if (NetworkPermissions.CanCopyToken(NetworkManager.Singleton.LocalClientId, this))
                TokenManager.Instance?.CopyToken(this, NetworkManager.Singleton.LocalClientId);
        }
        else
        {
            RequestCopyServerRpc();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestCopyServerRpc(RpcParams rpcParams = default)
    {
        ulong sender = rpcParams.Receive.SenderClientId;
        if (!NetworkPermissions.CanCopyToken(sender, this)) return;
        TokenManager.Instance?.CopyToken(this, sender);
    }

    // ═══ Контекстное меню (OnGUI) ═══

    private static TokenController _activeMenuToken;
    private bool _showMenu;
    private Rect _menuRect;
    private string _visionInput = "0";
    private string _visionError;
    private bool _visionEditing;
    private static int _menuClosedFrame = -1;
    private string _masterCurrentHpInput = "0", _masterMaxHpInput = "0", _masterArmorClassInput = "10";
    private string _masterStatBlockInput = "";
    private bool _conditionsPopup;
    private Vector2 _conditionsScroll;
    private Rect _conditionsRect;

    public static bool IsMenuTextFocused => _activeMenuToken != null &&
        _activeMenuToken._showMenu && _activeMenuToken._visionEditing;
    public static bool IsPointerOverMenu
    {
        get
        {
            if (_menuClosedFrame == Time.frameCount) return true;
            if (_activeMenuToken == null || !_activeMenuToken._showMenu || Mouse.current == null) return false;
            Vector2 pointer = Mouse.current.position.ReadValue();
            pointer.y = Screen.height - pointer.y;
            return _activeMenuToken._menuRect.Contains(pointer)
                || _activeMenuToken._conditionsPopup && _activeMenuToken._conditionsRect.Contains(pointer);
        }
    }

    private void CloseMenu()
    {
        if (_showMenu) _menuClosedFrame = Time.frameCount;
        _showMenu = false;
        _visionEditing = false;
        _conditionsPopup = false;
        if (_activeMenuToken == this) _activeMenuToken = null;
    }

    public byte[] GetPortraitJpg()
    {
        if (_portraitTexture == null) return null;
        return _portraitTexture.EncodeToJPG(50);
    }

    private void OnMouseOver()
    {
        if (SceneEditor.IsEditing || FogManager.IsManualEditing) return;
        if (!IsSpawned || !IsVisibleToLocalPlayer) return;
        if (!IsSpawner && !IsHost) return;
        if (MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive) return;
        if (EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive) return;

        if (!GameplayInputGate.AllowsWorldPointerInput) return;

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame &&
            GameplayInputGate.LastToolExitFrame != Time.frameCount)
        {
            // Закрыть предыдущее меню
            if (_activeMenuToken != null && _activeMenuToken != this)
                _activeMenuToken.CloseMenu();
            _activeMenuToken = this;

            _showMenu = true;
            _visionInput = VisionFeet.ToString();
            _visionError = null;
            _visionEditing = false;
            _masterCurrentHpInput = _masterData.currentHp.ToString();
            _masterMaxHpInput = _masterData.maxHp.ToString();
            _masterArmorClassInput = _masterData.armorClass.ToString();
            _masterStatBlockInput = _masterData.statBlockId ?? "";
            _conditionsPopup = false;
            Vector2 mousePos = mouse.position.ReadValue();
            float height = IsHost ? 570 : 196;
            _menuRect = new Rect(
                Mathf.Clamp(mousePos.x, 4, Mathf.Max(4, Screen.width - 264)),
                Mathf.Clamp(Screen.height - mousePos.y, 4, Mathf.Max(4, Screen.height - height - 4)),
                260, height);
        }
    }

    private void LoadImageDialog()
    {
        PickTokenImageFile(path =>
        {
            if (string.IsNullOrEmpty(path)) return;

            byte[] jpg = PreparePortraitJpg(File.ReadAllBytes(path));
            if (jpg != null && jpg.Length > 0)
                LoadImage(jpg);
        });
    }

    private static byte[] PreparePortraitJpg(byte[] fileData, int maxEdge = 512, int quality = 75)
    {
        if (fileData == null || fileData.Length == 0) return null;

        var tex = new Texture2D(2, 2);
        if (!tex.LoadImage(fileData))
        {
            Destroy(tex);
            return null;
        }

        int w = tex.width;
        int h = tex.height;
        if (Mathf.Max(w, h) > maxEdge)
        {
            float scale = maxEdge / (float)Mathf.Max(w, h);
            w = Mathf.Max(1, Mathf.RoundToInt(w * scale));
            h = Mathf.Max(1, Mathf.RoundToInt(h * scale));

            var rt = RenderTexture.GetTemporary(w, h);
            Graphics.Blit(tex, rt);
            var resized = new Texture2D(w, h, TextureFormat.RGB24, false);
            RenderTexture.active = rt;
            resized.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            resized.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            Destroy(tex);
            tex = resized;
        }

        byte[] jpg = tex.EncodeToJPG(quality);
        Destroy(tex);
        return jpg;
    }

    private static void PickTokenImageFile(Action<string> onPicked)
    {
#if UNITY_EDITOR
        string p = EditorUtility.OpenFilePanel("Выберите изображение", "", "png,jpg,jpeg,bmp,tga");
        onPicked(string.IsNullOrEmpty(p) ? null : p);
#else
        SimpleFileBrowser.FileBrowser.ShowLoadDialog(
            (paths) => onPicked(paths.Length > 0 ? paths[0] : null),
            () => onPicked(null),
            SimpleFileBrowser.FileBrowser.PickMode.Files,
            false, null, null, "Выберите изображение", "Select");
#endif
    }

    private void OnGUI()
    {
        if (!GameplayInputGate.AllowsImGuiOverlays) return;
        if (!IsSpawned || !IsVisibleToLocalPlayer) return;
        DrawTokenLabel();
        if (MeasurementTool.Instance != null && MeasurementTool.Instance.IsLocalActive ||
            EffectPaintTool.Instance != null && EffectPaintTool.Instance.IsActive)
        {
            CloseMenu();
            return;
        }
        if (!_showMenu) return;

        bool canLoad = IsSpawner || IsHost;
        bool canCopy = IsSpawner || IsHost;
        bool canDelete = IsSpawner || IsHost;
        if (!canLoad && !canCopy && !canDelete)
        {
            CloseMenu();
            return;
        }

        GUI.Box(_menuRect, "", VttUiSkin.ImGuiPanel);
        var titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        titleStyle.normal.textColor = VttUiSkin.Muted;
        GUI.Label(new Rect(_menuRect.x + 12, _menuRect.y + 8, 236, 25),
            TokenName, titleStyle);

        float y = _menuRect.y + 38f;
        if (canLoad)
        {
            if (GUI.Button(new Rect(_menuRect.x + 10, y, 240, 30),
                "Загрузить изображение", VttUiSkin.ImGuiButton))
            {
                LoadImageDialog();
                CloseMenu();
                return;
            }

            y += 36f;
        }

        if (canCopy && GUI.Button(new Rect(_menuRect.x + 10, y, 240, 30),
            "Копировать", VttUiSkin.ImGuiButton))
        {
            RequestCopy();
            CloseMenu();
            return;
        }

        if (canCopy)
            y += 36f;

        if (IsHost)
        {
            if (GUI.Button(new Rect(_menuRect.x + 10, y, 240, 30),
                InitiativeTracker.Instance != null && InitiativeTracker.Instance.ContainsToken(SceneId)
                    ? "Уже в инициативе" : "Добавить в инициативу", VttUiSkin.ImGuiButton))
            {
                InitiativeTracker.Instance?.AddToken(this);
                CloseMenu();
                return;
            }
            y += 36;
            if (GUI.Button(new Rect(_menuRect.x + 10, y, 240, 30),
                IsHidden ? "Показать игрокам" : "Скрыть от игроков", VttUiSkin.ImGuiButton))
                RequestSetHidden(!IsHidden);
            y += 36;
            GUI.Label(new Rect(_menuRect.x + 12, y, 230, 22), "Зрение токена · футы", titleStyle);
            y += 24;
            if (GUI.Button(new Rect(_menuRect.x + 10, y, 48, 30), "−5", VttUiSkin.ImGuiButton))
                StepVision(-5);
            bool enter = _visionEditing && Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter);
            GUI.SetNextControlName("TokenVisionFeet");
            _visionInput = GUI.TextField(new Rect(_menuRect.x + 64, y, 72, 30), _visionInput, 4);
            _visionEditing = GUI.GetNameOfFocusedControl() == "TokenVisionFeet";
            if (GUI.Button(new Rect(_menuRect.x + 142, y, 48, 30), "+5", VttUiSkin.ImGuiButton))
                StepVision(5);
            if (GUI.Button(new Rect(_menuRect.x + 196, y, 54, 30), "OK", VttUiSkin.ImGuiButton) || enter)
            {
                ApplyVisionInput();
                if (enter) Event.current.Use();
            }
            y += 34;
            GUI.Label(new Rect(_menuRect.x + 12, y, 238, 22),
                _visionError ?? $"0 — без зрения · до {MaxVisionFeet} футов", titleStyle);
            y += 26;
            GUI.Label(new Rect(_menuRect.x + 12, y, 238, 20), "HP · максимум · КД", titleStyle);
            y += 20;
            _masterCurrentHpInput = GUI.TextField(new Rect(_menuRect.x + 10, y, 66, 28), _masterCurrentHpInput);
            _masterMaxHpInput = GUI.TextField(new Rect(_menuRect.x + 82, y, 66, 28), _masterMaxHpInput);
            _masterArmorClassInput = GUI.TextField(new Rect(_menuRect.x + 154, y, 42, 28), _masterArmorClassInput);
            if (GUI.Button(new Rect(_menuRect.x + 200, y, 50, 28), "OK", VttUiSkin.ImGuiButton))
                ApplyMasterInputs();
            y += 32;
            bool hideHp = GUI.Toggle(new Rect(_menuRect.x + 10, y, 240, 22), _masterData.hideHp,
                "Скрыть HP от игроков");
            if (hideHp != _masterData.hideHp) ServerSetMasterVisibility(hideHp, _masterData.hideConditions);
            y += 22;
            bool hideConditions = GUI.Toggle(new Rect(_menuRect.x + 10, y, 240, 22), _masterData.hideConditions,
                "Скрыть состояния от игроков");
            if (hideConditions != _masterData.hideConditions) ServerSetMasterVisibility(_masterData.hideHp, hideConditions);
            y += 24;
            if (GUI.Button(new Rect(_menuRect.x + 10, y, 240, 28), "Состояния…", VttUiSkin.ImGuiButton))
                _conditionsPopup = !_conditionsPopup;
            y += 32;
            GUI.Label(new Rect(_menuRect.x + 12, y, 238, 20), "ID статблока", titleStyle);
            y += 20;
            _masterStatBlockInput = GUI.TextField(new Rect(_menuRect.x + 10, y, 180, 28), _masterStatBlockInput, 64);
            if (GUI.Button(new Rect(_menuRect.x + 196, y, 54, 28), "OK", VttUiSkin.ImGuiButton))
                ApplyStatBlockInput();
        }
        else
        {
            GUI.Label(new Rect(_menuRect.x + 12, y, 238, 22), $"Зрение: {VisionFeet} футов · задаёт GM", titleStyle);
            y += 26;
        }

        if (canDelete && GUI.Button(new Rect(_menuRect.x + 10, y, 240, 30),
            "Удалить токен", VttUiSkin.ImGuiDangerButton))
        {
            CloseMenu();
            DiceUI.Instance?.ConfirmAction($"Удалить «{TokenName}»?",
                "Токен исчезнет у всех участников сессии. Отменить удаление нельзя.",
                RequestDespawn);
        }

        if (_conditionsPopup) DrawConditionPopup();

        if (Event.current.type == EventType.MouseDown && !_menuRect.Contains(Event.current.mousePosition)
            && (!_conditionsPopup || !_conditionsRect.Contains(Event.current.mousePosition)))
            CloseMenu();
    }

    private void ApplyMasterInputs()
    {
        if (!int.TryParse(_masterCurrentHpInput, out int current)
            || !int.TryParse(_masterMaxHpInput, out int maximum)
            || !int.TryParse(_masterArmorClassInput, out int armorClass)
            || current < 0 || maximum < 0 || current > maximum
            || current > 999999 || maximum > 999999 || armorClass < 0 || armorClass > 999)
        {
            DiceUI.Instance?.ShowToolNotice("Проверьте HP, максимум HP и КД.");
            return;
        }
        ServerSetHealth(current, maximum);
        ServerSetArmorClass(armorClass);
        GUI.FocusControl(null);
    }

    private void ApplyStatBlockInput()
    {
        string id = _masterStatBlockInput.Trim();
        if (id.Length > 64 || id.Contains("|") || id.Contains(","))
        {
            DiceUI.Instance?.ShowToolNotice("Некорректный ID статблока.");
            return;
        }
        if (!string.IsNullOrEmpty(id))
        {
            DiceUI.Instance?.ShowToolNotice("В этой сессии пока нет загруженного каталога статблоков; сначала добавьте этот ID в пакет кампании.");
            return;
        }
        if (_masterData.statBlockId == id) return;
        string previous = _masterData.statBlockId;
        string tokenId = SceneId;
        GameMasterUndo.Record("статблок токена", () => FindSceneToken(tokenId)?.ServerSetStatBlock(previous));
        ServerSetStatBlock(id);
        GUI.FocusControl(null);
    }

    private void DrawConditionPopup()
    {
        int width = 260;
        float height = Mathf.Min(350, Screen.height - 16);
        float x = _menuRect.xMax + 8;
        if (x + width > Screen.width) x = Mathf.Max(4, _menuRect.x - width - 8);
        float y = Mathf.Clamp(_menuRect.y + 180, 4, Mathf.Max(4, Screen.height - height - 4));
        _conditionsRect = new Rect(x, y, width, height);
        GUI.Box(_conditionsRect, "Состояния", VttUiSkin.ImGuiPanel);
        var viewport = new Rect(_conditionsRect.x + 8, _conditionsRect.y + 28,
            _conditionsRect.width - 16, _conditionsRect.height - 36);
        var content = new Rect(0, 0, viewport.width - 18, TokenConditionCatalog.Ids.Length * 28);
        _conditionsScroll = GUI.BeginScrollView(viewport, _conditionsScroll, content);
        for (int i = 0; i < TokenConditionCatalog.Ids.Length; i++)
        {
            string id = TokenConditionCatalog.Ids[i];
            bool selected = Array.IndexOf(_masterData.conditionIds, id) >= 0;
            if (GUI.Button(new Rect(0, i * 28, content.width, 26),
                (selected ? "✓  " : "　 ") + TokenConditionCatalog.DisplayName(id), VttUiSkin.ImGuiButton))
                ServerToggleCondition(id);
        }
        GUI.EndScrollView();
    }

    private void StepVision(int step)
    {
        int value = int.TryParse(_visionInput, out int draft) ? draft : VisionFeet;
        value = Mathf.Clamp(value + step, 0, MaxVisionFeet);
        _visionInput = value.ToString();
        _visionError = null;
        RequestSetVision(value);
        GUI.FocusControl(null);
        _visionEditing = false;
    }

    private void ApplyVisionInput()
    {
        if (!int.TryParse(_visionInput, out int feet) || feet < 0 || feet > MaxVisionFeet)
        {
            _visionError = $"Введите число от 0 до {MaxVisionFeet}";
            return;
        }
        RequestSetVision(feet);
        _visionError = null;
        GUI.FocusControl(null);
        _visionEditing = false;
    }

    private void DrawTokenLabel()
    {
        var camera = Camera.main;
        if (camera == null) return;
        if (!TokenLabelLayout.Project(camera, DisplayTransform, _labelBounds, Screen.height,
            out Rect label)) return;
        if (_labelGrid == null) _labelGrid = FindAnyObjectByType<GridManager>();
        if (_labelGrid != null)
        {
            Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
            for (int i = 0; i < 4; i++)
            {
                Vector3 point = camera.WorldToScreenPoint(_labelGrid.GetTokenFootprintCorner(DisplayPosition, i));
                if (point.z <= 0) return;
                Vector2 screen = new(point.x, Screen.height - point.y);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
            label = TokenLabelLayout.PlaceInsideCell(label, Rect.MinMaxRect(min.x, min.y, max.x, max.y));
        }
        if (!label.Overlaps(new Rect(0, 0, Screen.width, Screen.height))) return;
        _labelStyle ??= new GUIStyle(VttUiSkin.ImGuiPanel)
        {
            wordWrap = false,
            richText = false,
            clipping = TextClipping.Clip,
            padding = new RectOffset(4, 4, 0, 0),
            fontSize = TokenLabelLayout.DesignFontSize
        };
        // Scale the whole plate, including font, padding and rounded borders, uniformly.
        Matrix4x4 previousMatrix = GUI.matrix;
        float scale = label.width / TokenLabelLayout.DesignWidth;
        GUI.matrix = previousMatrix * Matrix4x4.TRS(new Vector3(label.x, label.y, 0),
            Quaternion.identity, new Vector3(scale, scale, 1));
        Color previousColor = GUI.color;
        if (IsHidden) GUI.color = new Color(previousColor.r, previousColor.g, previousColor.b, previousColor.a * 0.35f);
        GUI.Label(new Rect(0, 0, TokenLabelLayout.DesignWidth, TokenLabelLayout.DesignHeight), "", _labelStyle);
        _statsLabelStyle ??= new GUIStyle(GUI.skin.label)
        {
            fontSize = 10,
            alignment = TextAnchor.MiddleCenter,
            clipping = TextClipping.Clip,
            padding = new RectOffset(2, 2, 0, 0)
        };
        _statsLabelStyle.normal.textColor = VttUiSkin.Text;
        GUI.Label(new Rect(3, 1, 154, 19), IsHero ? TokenName + " · герой" : TokenName, _statsLabelStyle);
        int currentHp = VisibleCurrentHp;
        int maxHp = VisibleMaxHp;
        string conditions = string.Join(" · ", Array.ConvertAll(VisibleConditionIds,
            TokenConditionCatalog.DisplayName));
        if (currentHp >= 0 && maxHp > 0)
        {
            Rect bar = new Rect(5, 21, 150, 12);
            GUI.color = new Color(0.06f, 0.07f, 0.09f, 0.94f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            float ratio = Mathf.Clamp01((float)currentHp / maxHp);
            GUI.color = Color.Lerp(new Color(0.8f, 0.18f, 0.18f), new Color(0.2f, 0.72f, 0.34f), ratio);
            if (ratio > 0) GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), Texture2D.whiteTexture);
            GUI.color = previousColor;
            GUI.Label(new Rect(4, 20, 152, 14), $"{currentHp}/{maxHp} HP · КД {ArmorClass}", _statsLabelStyle);
        }
        else
            GUI.Label(new Rect(3, 20, 154, 15), $"КД {ArmorClass}", _statsLabelStyle);
        if (!string.IsNullOrEmpty(conditions))
            GUI.Label(new Rect(3, 34, 154, 16), conditions, _statsLabelStyle);
        GUI.color = previousColor;
        GUI.matrix = previousMatrix;
    }
}
