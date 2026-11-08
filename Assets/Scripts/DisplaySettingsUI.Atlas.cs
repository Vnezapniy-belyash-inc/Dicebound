using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public partial class DisplaySettingsUI
{
    private RectTransform _atlasSettingsPanel, _atlasSettingsBody;
    private Canvas _atlasSettingsCanvas;
    private string _atlasSettingsKey;
    private Text _atlasCountdown;

    private void UpdateAtlasSettings()
    {
        if (!IsOpen) return;
        var network = NetworkManager.Singleton;
        bool inSession = network != null && network.IsListening;
        bool host = network != null && network.IsHost;
        string key = $"{_section}:{_pending}:{_resolutionIndex}:{_modeIndex}:{SceneFileStore.MovementPolicy}:{LocalControls.TokenHover}:{inSession}:{host}";
        if (_atlasSettingsBody == null || key != _atlasSettingsKey)
        {
            _atlasSettingsKey = key;
            DrawAtlasSettings(inSession, host);
            LayoutRebuilder.ForceRebuildLayoutImmediate(_atlasSettingsBody);
        }
        float scale = Mathf.Max(.01f, _atlasSettingsCanvas.scaleFactor);
        float height = Mathf.Clamp(LayoutUtility.GetPreferredHeight(_atlasSettingsBody) + 12, 250, 470);
        _atlasSettingsPanel.sizeDelta = new Vector2(Mathf.Min(600, Screen.width / scale - 24),
            Mathf.Min(height, Screen.height / scale - 24));
        if (_atlasCountdown != null && _pending)
            _atlasCountdown.text = "Подтвердите экран · " + Mathf.CeilToInt(_confirmationDeadline - Time.realtimeSinceStartup) + " с";
    }

    private void DrawAtlasSettings(bool inSession, bool host)
    {
        if (_atlasSettingsBody == null)
        {
            _atlasSettingsCanvas = _inputBlocker.GetComponent<Canvas>();
            var scaler = _inputBlocker.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.matchWidthOrHeight = .5f;
            _inputBlocker.transform.GetChild(0).GetComponent<Image>().color = new Color(.03f, .10f, .07f, .82f);
            _atlasSettingsBody = AtlasUi.Scroll(_inputBlocker.transform, "AtlasSettings", out var scroll, 22);
            _atlasSettingsPanel = (RectTransform)scroll.transform;
            _atlasSettingsPanel.anchorMin = _atlasSettingsPanel.anchorMax = _atlasSettingsPanel.pivot = new Vector2(.5f, .5f);
        }

        AtlasUi.Clear(_atlasSettingsBody);
        _atlasCountdown = null;
        AtlasUi.Heading(_atlasSettingsBody, "Настройки", () => { if (_pending) Revert(); SetOpen(false); });
        var tabs = CompactSettingsRow("Tabs", 36);
        var currentTab = SettingsTab(tabs, "Экран", 0, 94);
        var controls = SettingsTab(tabs, "Управление", 1, 126);
        if (_section == 1) currentTab = controls;
        if (host)
        {
            var campaign = SettingsTab(tabs, "Кампания", 2, 110);
            if (_section == 2) currentTab = campaign;
        }

        if (_pending)
        {
            _atlasCountdown = AtlasUi.Text(_atlasSettingsBody, "Подтвердите экран", 23, title: true, height: 50);
            var confirmation = CompactSettingsRow("DisplayConfirmation");
            SettingsButton(confirmation, "Сохранить", Confirm, 120, true);
            SettingsButton(confirmation, "Вернуть прежние", Revert, 158);
        }
        else
        {
            if (_section == 0) DrawScreenSettings();
            else if (_section == 1) DrawControlSettings();
            else DrawCampaignSettings();
            DrawSettingsFooter(inSession, host);
        }

        var events = EventSystem.current;
        var selected = events != null ? events.currentSelectedGameObject : null;
        if (selected == null || !selected.activeInHierarchy || !selected.transform.IsChildOf(_atlasSettingsPanel))
            currentTab.Select();
    }

    private void DrawScreenSettings()
    {
        Vector2Int size = _resolutions[_resolutionIndex];
        SettingsStepRow("Resolution", "Разрешение", $"{size.x} × {size.y}",
            () => _resolutionIndex = (_resolutionIndex - 1 + _resolutions.Count) % _resolutions.Count,
            () => _resolutionIndex = (_resolutionIndex + 1) % _resolutions.Count);
        SettingsStepRow("Mode", "Режим окна", _modeNames[_modeIndex],
            () => _modeIndex = (_modeIndex - 1 + _modes.Length) % _modes.Length,
            () => _modeIndex = (_modeIndex + 1) % _modes.Length);
        var apply = CompactSettingsRow("ApplyDisplay");
        SettingsSpacer(apply);
        SettingsButton(apply, "Применить экран…", Apply, 176, true);

        AtlasUi.Divider(_atlasSettingsBody, "DisplayDivider");
        var scale = CompactSettingsRow("Scale");
        AtlasUi.Text(scale, "Масштаб UI", 16, height: 38);
        SettingsSpacer(scale);
        SettingsButton(scale, "−", () => _uiScale.SetScale(LocalUiScale.Value - .1f), 36);
        SettingsButton(scale, "100%", () => _uiScale.SetScale(1), 64);
        SettingsButton(scale, "+", () => _uiScale.SetScale(LocalUiScale.Value + .1f), 36);
    }

    private void DrawControlSettings()
    {
        AtlasUi.Text(_atlasSettingsBody,
            "ЛКМ — инструмент · ПКМ / Esc — завершить\nWASD — камера · F1 — вид камеры\nПКМ по токену — свойства",
            16, height: 90);
        var speed = CompactSettingsRow("CameraSpeed");
        AtlasUi.Text(speed, "Скорость камеры", 16, height: 38);
        SettingsSpacer(speed);
        SettingsButton(speed, "−", () => LocalControls.SetCameraSpeed(LocalControls.CameraSpeed - .1f), 36);
        SettingsButton(speed, "+", () => LocalControls.SetCameraSpeed(LocalControls.CameraSpeed + .1f), 36);
        var actions = CompactSettingsRow("ControlActions");
        SettingsButton(actions, LocalControls.TokenHover ? "Убрать подсказки" : "Показать подсказки",
            () => LocalControls.SetTokenHover(!LocalControls.TokenHover), 172);
        SettingsButton(actions, "Сбросить управление",
            () => { LocalControls.SetCameraSpeed(1); LocalControls.SetTokenHover(true); }, 180);
    }

    private void DrawCampaignSettings()
    {
        AtlasUi.Text(_atlasSettingsBody, "Правила перемещения", 19, title: true, height: 32);
        var names = new[] { "Свободное", "Предупреждать", "Решает мастер", "По клеткам" };
        for (int i = 0; i < names.Length; i += 2)
        {
            var row = AtlasUi.Row(_atlasSettingsBody, "MovementPolicies", 36, 8);
            for (int j = i; j < i + 2; j++)
            {
                var policy = (PlayerMovementPolicy)j;
                AtlasUi.Button(row, names[j], () => SceneFileStore.SetMovementPolicy(policy),
                    SceneFileStore.MovementPolicy == policy, height: 36);
            }
        }
        var preferences = AtlasUi.Row(_atlasSettingsBody, "CampaignPreferences", 38, 8);
        AtlasUi.Button(preferences, "Автосохранение", () =>
        {
            var fog = FogManager.Instance;
            if (fog != null) SceneFileStore.SetCampaignPreferences(!fog.Autosave, fog.SaveWithHistory);
        }, height: 38);
        AtlasUi.Button(preferences, "История тумана в экспорте", () =>
        {
            var fog = FogManager.Instance;
            if (fog != null) SceneFileStore.SetCampaignPreferences(fog.Autosave, !fog.SaveWithHistory);
        }, height: 38);
        var files = CompactSettingsRow("CampaignFiles");
        SettingsButton(files, "Сохранить сессию…", () => { SetOpen(false); SceneFileStore.SaveCampaignDialog(); }, 173);
        SettingsButton(files, "Загрузить сессию…", () => { SetOpen(false); SceneFileStore.LoadCampaignDialog(); }, 173);
    }

    private void DrawSettingsFooter(bool inSession, bool host)
    {
        AtlasUi.Divider(_atlasSettingsBody, "FooterDivider");
        var footer = CompactSettingsRow("SettingsActions");
        if (inSession)
            SettingsButton(footer, host ? "Завершить сессию" : "Выйти из сессии",
                () => { SetOpen(false); DiceUI.Instance?.ShowLeaveConfirm(); }, 172);
        SettingsSpacer(footer);
        SettingsButton(footer, "Готово", () => SetOpen(false), 98, true);
    }

    private RectTransform CompactSettingsRow(string name, float height = 38)
    {
        var row = AtlasUi.Row(_atlasSettingsBody, name, height, 8);
        row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        return row;
    }

    private Button SettingsTab(Transform row, string caption, int section, float width) =>
        SettingsButton(row, caption, () => _section = section, width, _section == section);

    private void SettingsStepRow(string name, string caption, string value, UnityAction previous, UnityAction next)
    {
        var row = CompactSettingsRow(name);
        AtlasUi.Width(AtlasUi.Text(row, caption, 16, height: 38), 128);
        SettingsButton(row, "‹", previous, 36);
        var valueText = AtlasUi.Text(row, value, 16, height: 38);
        valueText.alignment = TextAnchor.MiddleCenter;
        var layout = valueText.GetComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 0;
        layout.flexibleWidth = 1;
        SettingsButton(row, "›", next, 36);
    }

    private static Button SettingsButton(Transform parent, string caption, UnityAction action, float width, bool primary = false)
    {
        var button = AtlasUi.Button(parent, caption, action, primary, height: 36);
        AtlasUi.Width(button, width);
        button.GetComponentInChildren<Text>().alignment = TextAnchor.MiddleCenter;
        if (width == 36) AtlasUi.Fill(button.GetComponentInChildren<Text>().rectTransform, 0, 2, 0, 2);
        return button;
    }

    private static void SettingsSpacer(Transform row) =>
        AtlasUi.Node(row, "Spacer").gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
}
