using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class HostSceneCurtain
{
    private const float LoadingCardWidth = 700f;
    private const float LoadingCardHeight = 550f;

    private string _joinStatus;
    private float _joinStartedAt, _nextLoadingRefresh;
    private RectTransform _loadingScreen, _loadingCard, _overallFill, _mapFill;
    private Text _loadingSummary, _loadingStatus;
    private Text[] _loadingStages;
    private Button _loadingRetry, _loadingExit;

    public static bool IsLoadingScreenVisible => Instance != null
        && (Instance._joinStatus != null || IsLocalSynchronizing);

    internal void SetJoinStatus(string status)
    {
        if (_joinStatus == null && status != null) _joinStartedAt = Time.unscaledTime;
        _joinStatus = status;
        ApplyCurtainState();
    }

    private void SetLoadingScreenVisible(bool visible)
    {
        if (!visible)
        {
            if (_loadingScreen != null) _loadingScreen.gameObject.SetActive(false);
            return;
        }
        if (_loadingScreen == null) BuildLoadingScreen();
        if (!_loadingScreen.gameObject.activeSelf)
            _loadingScreen.gameObject.SetActive(true);
        _nextLoadingRefresh = 0f;
        UpdateLoadingScreen();
    }

    private void BuildLoadingScreen()
    {
        _loadingScreen = AtlasUi.Panel(_clientCanvas.transform, "Session Loading Screen", AtlasUi.Forest, square: true);
        AtlasUi.Fill(_loadingScreen);
        _loadingCard = AtlasUi.Panel(_loadingScreen, "Loading Card", AtlasUi.Paper);
        _loadingCard.anchorMin = _loadingCard.anchorMax = new Vector2(.5f, .5f);
        _loadingCard.sizeDelta = new Vector2(LoadingCardWidth, LoadingCardHeight);

        var header = AtlasUi.Panel(_loadingCard, "Loading Header", new Color(.12f, .28f, .22f));
        AtlasUi.Place(header, 0, 0, LoadingCardWidth, 70);
        AtlasUi.BrandImage(header, "Atlas/ReferenceIcons/crest", 26, 15, 36, 40);
        var brand = AtlasUi.Text(header, "Dicebound", 30, AtlasUi.Light, title: true);
        AtlasUi.Place(brand.rectTransform, 75, 12, 300, 46);

        LoadingText("Загрузка сессии", 32, AtlasUi.Ink, 28, 84, 644, 44, title: true);
        _loadingSummary = LoadingText("", 17, AtlasUi.Muted, 28, 130, 644, 30);
        _overallFill = CreateLoadingBar(165, 9);

        _loadingStages = new Text[SyncStageNames.Length];
        for (int i = 0; i < _loadingStages.Length; i++)
        {
            float y = i == 0 ? 190 : 238 + (i - 1) * 36;
            LoadingText(SyncStageNames[i], 18, AtlasUi.Ink, 28, y, 247, 32);
            _loadingStages[i] = LoadingText("", 17, AtlasUi.Muted, 280, y, 392, 32);
            _loadingStages[i].alignment = TextAnchor.MiddleRight;
        }
        _mapFill = CreateLoadingBar(223, 5);

        _loadingStatus = LoadingText("", 17, AtlasUi.Ink, 28, 422, 644, 50);
        _loadingRetry = AtlasUi.Button(_loadingCard, "Повторить запрос", RetryLocalSync, primary: true, height: 42);
        AtlasUi.Place((RectTransform)_loadingRetry.transform, 28, 482, 206, 42);
        _loadingExit = AtlasUi.Button(_loadingCard, "Выйти в лобби",
            () => { _ = GameNetworkManager.Instance?.ShutdownAndReset(); }, height: 42);
        AtlasUi.Place((RectTransform)_loadingExit.transform, 466, 482, 206, 42);
    }

    private Text LoadingText(string value, int size, Color color, float x, float y, float width, float height,
        bool title = false)
    {
        var label = AtlasUi.Text(_loadingCard, value, size, color, title);
        AtlasUi.Place(label.rectTransform, x, y, width, height);
        return label;
    }

    private RectTransform CreateLoadingBar(float y, float height)
    {
        var track = AtlasUi.Panel(_loadingCard, "Progress Track", AtlasUi.Raised, square: true);
        AtlasUi.Place(track, 28, y, 644, height);
        var fill = AtlasUi.Panel(track, "Progress Fill", AtlasUi.Copper, square: true);
        AtlasUi.Fill(fill);
        return fill;
    }

    private void UpdateLoadingScreen()
    {
        if (_loadingScreen == null) return;
        float scale = Mathf.Min(1f, (_loadingScreen.rect.width - 24f) / LoadingCardWidth,
            (_loadingScreen.rect.height - 24f) / LoadingCardHeight);
        _loadingCard.localScale = Vector3.one * Mathf.Max(.01f, scale);

        float now = Time.unscaledTime;
        if (now < _nextLoadingRefresh) return;
        _nextLoadingRefresh = now + .15f;
        bool syncing = IsLocalSynchronizing;
        var map = syncing ? MapSync.Instance : null;
        var world = syncing ? LateJoinSync.Instance : null;
        int complete = 0;
        for (int i = 0; i < _loadingStages.Length; i++)
        {
            bool done = syncing && (_syncStages & (1 << i)) != 0;
            if (done) complete++;
            _loadingStages[i].color = done ? AtlasUi.Ink : AtlasUi.Muted;
            _loadingStages[i].text = i switch
            {
                0 => MapLoadingStatus(map, done),
                1 when world != null && world.LocalWorldObjectTotal >= 0 =>
                    $"{world.LocalWorldObjectCount} / {world.LocalWorldObjectTotal}" + (done ? " · Готово" : ""),
                1 when syncing && !done => "Ожидаем список",
                4 when syncing => $"{_portraitsReceived} / {_portraitsTotal}" + (done ? " · Готово" : ""),
                _ => done ? "Готово" : "Ожидаем"
            };
        }
        int elapsed = Mathf.FloorToInt(now - (syncing ? _syncStartedAt : _joinStartedAt));
        _loadingSummary.text = $"Этапы {complete} из {SyncStageNames.Length} · {elapsed} с";
        _overallFill.anchorMax = new Vector2((float)complete / SyncStageNames.Length, 1f);
        float mapProgress = syncing && (_syncStages & 1) != 0 ? 1f : map?.LocalReceiveProgress ?? 0f;
        _mapFill.anchorMax = new Vector2(Mathf.Clamp01(mapProgress), 1f);

        bool stalled = syncing && (now - _lastProgressAt > 20f
            || map != null && (map.LocalReceiveFailed || map.LocalReceiveCancelled));
        _loadingStatus.text = !syncing ? _joinStatus
            : stalled ? "Новых данных пока нет. Можно повторить запрос."
            : _curtainDown ? "Мастер готовит сцену. Продолжаем загрузку."
            : "Синхронизируем сцену. Стол откроется после загрузки всех этапов.";
        bool canLeave = syncing && _joinStatus == null && NetworkManager.Singleton?.IsConnectedClient == true;
        _loadingRetry.gameObject.SetActive(canLeave && stalled);
        _loadingExit.gameObject.SetActive(canLeave);
        var eventSystem = EventSystem.current;
        if (canLeave && eventSystem != null
            && (eventSystem.currentSelectedGameObject == null || !eventSystem.currentSelectedGameObject.activeInHierarchy))
            eventSystem.SetSelectedGameObject(_loadingExit.gameObject);
    }

    private static string MapLoadingStatus(MapSync map, bool done)
    {
        if (done) return "Готово";
        if (map == null) return "Ожидаем данные";
        if (map.IsReceivingMap)
            return $"{Mathf.RoundToInt(map.LocalReceiveProgress * 100)}% · {map.LocalReceivedBytes / 1024} / {map.LocalTotalBytes / 1024} КиБ"
                + (map.LocalRetryCount > 0 ? $" · повтор {map.LocalRetryCount}/3" : "");
        if (map.LocalReceiveFailed) return "Не удалось получить данные";
        return map.LocalReceiveCancelled ? "Загрузка отменена" : "Ожидаем данные";
    }

    private void RetryLocalSync()
    {
        LateJoinSync.RequestSynchronizationRetry();
        _lastProgressAt = Time.unscaledTime;
        _nextLoadingRefresh = 0f;
        UpdateLoadingScreen();
    }
}
