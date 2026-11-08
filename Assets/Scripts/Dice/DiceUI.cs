using System;
using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Dice/journal controller and lifecycle for the native Atlas interface.</summary>
public partial class DiceUI : MonoBehaviour
{
    public static DiceUI Instance { get; private set; }
    public static event Action<string,int,ulong> JournalResultRecorded;
    public float buttonWidth=55,buttonHeight=32,gap=4,padding=8,spawnSpread=.8f;
    private Canvas _canvas;
    private InputField _scaleInput;
    public InputField ScaleInput => _scaleInput;
    public float ToolsPanelPixelWidth => _nativeRoot!=null&&_nativeRoot.gameObject.activeInHierarchy?AtlasLeft*_canvas.scaleFactor:0;
    public float SessionHeaderPixelHeight => _nativeHeader!=null&&_nativeRoot.gameObject.activeInHierarchy?_nativeHeader.rect.height*_canvas.scaleFactor:0;
    private GameObject _topBarGO,_bottomPanelGO,_logPanelGO,_confirmPanel;
    private Text _movementRequestsText,_confirmTitle,_confirmBody;
    private Button _movementRequestsButton,_confirmAccept;
    private GridManager _gridManager;
    private readonly List<DieResult> _lastResults=new();
    private bool _trackingRoll,_subscribedToMT;
    private int _rollingCount;
    private Action _atlasDragEnd;
    public bool IsConfirmationOpen => _confirmPanel!=null&&_confirmPanel.activeInHierarchy;
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        _gridManager = FindAnyObjectByType<GridManager>();
        if (GetComponent<EffectPaintTool>() == null)
            gameObject.AddComponent<EffectPaintTool>();
        if (GetComponent<SceneEditor>() == null) gameObject.AddComponent<SceneEditor>();
        if (GetComponent<FogManager>() == null) gameObject.AddComponent<FogManager>();
        BuildUI();
        PlayerColors.Changed += OnPlayerColorsChanged;
        LocalControls.HintsChanged += OnHintsChanged;
        if (GetComponent<DmPanelUI>() == null)
            gameObject.AddComponent<DmPanelUI>();
        if (DiceManager.Instance != null)
            DiceManager.Instance.OnAnyResult += OnDieResult;
        InvokeRepeating(nameof(TrySubscribeToMeasureTool), 0.1f, 0.5f);
    }

    void TrySubscribeToMeasureTool()
    {
        var mt=MeasurementTool.Instance;if(mt==null||_subscribedToMT)return;
        _atlasDragEnd=()=>{if(mt.IsLocalActive&&mt.CurrentMode!=MeasurementTool.Mode.Ruler&&mt.GetCellsInArea().Count>0)ShowTextureMenu();};
        mt.OnDragEnd+=_atlasDragEnd;_subscribedToMT=true;CancelInvoke(nameof(TrySubscribeToMeasureTool));
    }

    void Update()
    {
        UpdateAtlasNativeUi();
    }

    static bool IsLocalHost()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
    }

    void OnDestroy()
    {
        DisconnectSessionFeed();
        PlayerColors.Changed-=OnPlayerColorsChanged;
        LocalControls.HintsChanged -= OnHintsChanged;
        if(DiceManager.Instance!=null)DiceManager.Instance.OnAnyResult-=OnDieResult;
        if(MeasurementTool.Instance!=null&&_atlasDragEnd!=null)MeasurementTool.Instance.OnDragEnd-=_atlasDragEnd;
        if(Instance==this)Instance=null;
    }

    private void OnHintsChanged()
    {
        if (_nativePlayerContent != null) NativeBuildPlayerMenu();
    }

    private void OnPlayerColorsChanged()
    {
        _nativeRefreshAt=0;
    }

    void BuildUI()
    {
        try
        {
            BuildUIInternal();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"DiceUI build failed: {e.Message}\n{e.StackTrace}");
        }
    }

    void BuildUIInternal()
    {
        var es = FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>();
        if(es==null){var go=new GameObject("EventSystem",typeof(UnityEngine.EventSystems.EventSystem),typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));}
        var canvasGO=new GameObject("AtlasCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));
        canvasGO.transform.SetParent(transform,false); _canvas=canvasGO.GetComponent<Canvas>();
        _canvas.renderMode=RenderMode.ScreenSpaceOverlay;_canvas.sortingOrder=10;
        var scaler=canvasGO.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,900);scaler.matchWidthOrHeight=.5f;
        BuildAtlasNativeUi();
    }

    public void ShowGamePanels()
    {
        AtlasCloseContext();
        _nativeRoot.gameObject.SetActive(true); _nativeBuiltRole=false; _nativeMenuOpen=true;
        _nativeInitKey=null;  _nativeTool=0;
        _nativeHost=IsLocalHost(); _bottomPanelGO.SetActive(_nativeHost);
        _logPanelGO.SetActive(false);_confirmPanel.SetActive(false);UpdateAtlasNativeUi();
        ConnectSessionFeed();
    }

    public void HideGamePanels()
    {
        DisconnectSessionFeed();
        SelectDefaultTool(); AtlasCloseContext();if(_nativeRoot!=null)_nativeRoot.gameObject.SetActive(false);
        if(_confirmPanel!=null)_confirmPanel.SetActive(false);
    }

    internal void ShowLeaveConfirm()
    {
        bool host = IsLocalHost();
        bool unsaved = host && SceneFileStore.HasUnsavedChanges();
        string detail = host ? "Подключённые игроки будут отключены." : "Вы сможете снова войти по коду сессии.";
        if (unsaved) detail += "\nЕсть несохранённые изменения или черновики. Отмените выход и сохраните нужные данные.";
        ConfirmAction(host ? "Завершить сессию?" : "Выйти из сессии?", detail, DoLeave);
        if (_confirmAccept != null) _confirmAccept.GetComponentInChildren<Text>().text = host ? "Завершить сессию" : "Выйти";
    }

    void DoLeave()
    {
        if (GameNetworkManager.Instance != null)
            _ = GameNetworkManager.Instance.ShutdownAndReset();
        else if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.Shutdown();
    }

    public void ConfirmAction(string title, string detail, System.Action action)
    {
        if (_confirmPanel == null || action == null) return;
        _confirmTitle.text = title;
        _confirmBody.text = detail;
        _confirmAccept.GetComponentInChildren<Text>().text = "Подтвердить";
        _confirmAccept.onClick.RemoveAllListeners();
        _confirmAccept.onClick.AddListener(() => {
            _confirmPanel.SetActive(false);
            action();
        });
        _confirmPanel.transform.SetAsLastSibling();
        _confirmPanel.SetActive(true);
        var modalCanvas=_confirmPanel.GetComponent<Canvas>();
        if(modalCanvas!=null){modalCanvas.overrideSorting=true;modalCanvas.sortingOrder=100;}
    }

    void SelectDefaultTool()
    {
        _nativeTool=0;SceneEditor.Instance?.Deactivate();FogManager.Instance?.StopManual();
        MeasurementTool.Instance?.Deactivate();EffectPaintTool.Instance?.Deactivate();
    }

    void ActivateMeasurement(int mode)
    {
        MeasurementTool.Instance?.Activate(mode);
    }

    void ShowTextureMenu()
    {
        _nativeTool=3;_nativeMenuOpen=true;NativeRebuildContext();NativeLayout();
    }

    void SpawnDie(DieType type)
    {
        // Центр карты (или 0,0,0 если карты нет)
        Vector3 mapCenter = MapController.Instance != null
            ? MapController.Instance.transform.position
            : Vector3.zero;

        // Сетевой режим — кубики через NetworkDiceManager
        if (GameNetworkManager.Instance != null && GameNetworkManager.Instance.IsConnected)
        {
            Vector3 pos = mapCenter + new Vector3(
                UnityEngine.Random.Range(-spawnSpread, spawnSpread),
                0,
                UnityEngine.Random.Range(-spawnSpread, spawnSpread)
            );
            NetworkDiceManager.Instance?.RequestSpawnDie(type, pos);
            return;
        }

        // Локальный режим — старый DiceManager
        if (DiceManager.Instance == null) return;
        float x = mapCenter.x + UnityEngine.Random.Range(-spawnSpread, spawnSpread);
        float z = mapCenter.z + UnityEngine.Random.Range(-spawnSpread, spawnSpread);
        DiceManager.Instance.SpawnDieAt(type, x, z);
    }

    void RollAll()
    {
        if (DiceManager.Instance == null) return;
        int active = DiceManager.Instance.ActiveDice.Count;
        if (active == 0)
        {
            ShowToolNotice("Нет кубиков.", TableNoticeKind.Warning);
            return;
        }
        _lastResults.Clear();
        _trackingRoll = true;
        _rollingCount = active;
        DiceManager.Instance.RollAll();
        UpdateResultText();
    }

    void ClearAll()
    {
        var nm = NetworkManager.Singleton;
        bool isHost = nm != null && nm.IsHost;
        ulong localClientId = nm != null ? nm.LocalClientId : 0;
        bool hostHasOwnDice = isHost && NetworkPermissions.HostHasSpawnedDice(localClientId);

        // Удаляем сетевые кубики (права: IsSpawner; хост без своих — все)
        var netDice = FindObjectsByType<NetworkDice>(FindObjectsInactive.Exclude);
        foreach (var nd in netDice)
        {
            if (!NetworkPermissions.ShouldDeleteDiceInClearAll(nd, localClientId, isHost, hostHasOwnDice))
                continue;

            if (!nd.TryGetComponent<NetworkObject>(out var no) || !no.IsSpawned)
                continue;

            if (nm != null && nm.IsServer)
                no.Despawn();
            else
                SendDespawnRequest(no.NetworkObjectId);
        }

        // Удаляем локальные кубики
        if (DiceManager.Instance != null)
            DiceManager.Instance.ClearAll();

        _lastResults.Clear();
        _trackingRoll = false;
        _rollingCount = 0;
        // Логи НЕ чистим!
        UpdateResultText();
    }

    void ConfirmDiceClear()
    {
        var nm = NetworkManager.Singleton;
        bool hostClearsAll = nm != null && nm.IsHost &&
            !NetworkPermissions.HostHasSpawnedDice(nm.LocalClientId);
        ConfirmAction(hostClearsAll ? "Убрать все кубики?" : "Убрать мои кубики?",
            hostClearsAll
                ? "На столе нет ваших кубиков. Как DM вы удалите кубики всех игроков. Отменить удаление нельзя."
                : "Ваши кубики будут удалены со стола. Журнал бросков сохранится. Отменить удаление нельзя.",
            ClearAll);
    }

    private static void SendDespawnRequest(ulong netId)
    {
        var writer = new FastBufferWriter(sizeof(ulong), Unity.Collections.Allocator.Temp);
        writer.WriteValueSafe(netId);
        NetworkManager.Singleton.CustomMessagingManager.SendNamedMessage(
            "DespawnDice", NetworkManager.ServerClientId, writer);
        writer.Dispose();
    }

    public void StartManualRoll(int diceCount)
    {
        _lastResults.Clear();
        _trackingRoll = true;
        _rollingCount = diceCount;
    }

    public void ShowResult(string dieTypeName, int result, ulong throwerId, string ownerNickname = null)
    {
        DieType type = ParseDieType(dieTypeName);

        string who = !string.IsNullOrEmpty(ownerNickname) ? ownerNickname
            : PlayerColors.GetNickname(throwerId) ?? $"P{throwerId}";
        string logLine = $"{System.DateTime.Now:HH:mm:ss}  [{who}] {DieTypeName(type)}={result}";
        AddFeedRow(DateTime.UtcNow.Ticks, logLine, true);
        JournalResultRecorded?.Invoke(DieTypeName(type), result, throwerId);
        ShowRollNotice(who, $"{DieTypeName(type)} = {result}", throwerId);
        Debug.Log($"[Dice] Remote: {who} rolled {dieTypeName}: {result}");
    }

    static DieType ParseDieType(string name)
    {
        return name switch
        {
            "d4" => DieType.d4,
            "d6" => DieType.d6,
            "d8" => DieType.d8,
            "d10" => DieType.d10,
            "d100" => DieType.d100,
            "d12" => DieType.d12,
            "d20" => DieType.d20,
            _ => DieType.d20,
        };
    }

    void OpenTexturePicker()
    {
        MapController.Instance?.LoadImage();
    }

    void OnDieResult(Dice die)
    {
        if (!_trackingRoll) return;
        _lastResults.Add(new DieResult { type = die.DieType, value = die.Result });
        _rollingCount--;
        UpdateResultText();
    }

    void UpdateResultText()
    {
        if (!_trackingRoll || _rollingCount > 0 || _lastResults.Count == 0) return;
        _trackingRoll = false;
        int total = 0;
        foreach (var result in _lastResults) total += result.value;
        string logLine = $"{System.DateTime.Now:HH:mm:ss}  [{_lastResults.Count}] {string.Join(" + ", _lastResults.ConvertAll(r => $"{DieTypeName(r.type)}={r.value}"))} = {total}";
        AddFeedRow(DateTime.UtcNow.Ticks, logLine, true);
        foreach (var result in _lastResults)
            JournalResultRecorded?.Invoke(DieTypeName(result.type), result.value,
                NetworkManager.Singleton != null ? NetworkManager.Singleton.LocalClientId : 0);
        string roll = _lastResults.Count == 1 ? $"{DieTypeName(_lastResults[0].type)} = {total}"
            : $"{_lastResults.Count} куб. · сумма {total}";
        ShowToolNotice("Бросок · " + roll, TableNoticeKind.Roll);
    }

    static string DieTypeName(DieType t) => t switch
    {
        DieType.d4 => "d4",
        DieType.d6 => "d6",
        DieType.d8 => "d8",
        DieType.d10 => "d10",
        DieType.d12 => "d12",
        DieType.d20 => "d20",
        DieType.d100 => "d100",
        _ => "d?",
    };
    private struct DieResult { public DieType type; public int value; }
}
