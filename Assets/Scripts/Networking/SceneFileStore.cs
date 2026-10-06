using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Netcode;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>Portable single-file scene: JSON plus embedded map/portrait bytes. Battle state is excluded.</summary>
public static class SceneFileStore
{
    private static string _lastSavedState;
    private static string _currentSceneId = Guid.NewGuid().ToString("N");
    private static CampaignDefinition _campaign;

    public static string ActiveSceneLabel
    {
        get
        {
            if (_campaign == null) return "Сцены: текущая";
            int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
            if (index < 0) return "Сцены: текущая";
            return $"Сцена {index + 1}/{_campaign.scenes.Length}: {_campaign.scenes[index].title}";
        }
    }

    public static bool HasCampaign => _campaign != null && _campaign.scenes != null && _campaign.scenes.Length > 0;

    public static CampaignDefinition CaptureCampaign(string title = null)
    {
        var scene = Capture(true);
        if (_campaign == null)
            _campaign = SceneSaveMigration.UpgradeSingleScene(scene,
                string.IsNullOrWhiteSpace(title) ? "Кампания" : title);
        else
        {
            if (!string.IsNullOrWhiteSpace(title)) _campaign.title = title;
            UpdateCampaignScene(scene, InitiativeTracker.Instance?.CaptureBattleState());
        }
        SceneValidation.Validate(_campaign);
        CampaignFileStore.ValidateEmbeddedImageBudget(_campaign);
        return _campaign;
    }

    public static void SaveCampaign(string path, string title = null)
    {
        CampaignFileStore.Save(path, CaptureCampaign(title));
        DiceUI.Instance?.ShowToolNotice("Сессия сохранена: " + Path.GetFileName(path));
    }

    private static void UpdateCampaignScene(SceneDefinition scene, SceneBattleState battle)
    {
        int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == scene.sceneId);
        var snapshot = new CampaignScene
        {
            sceneId = scene.sceneId,
            title = index >= 0 ? _campaign.scenes[index].title : scene.title,
            scene = scene,
            battle = battle ?? (index >= 0 ? _campaign.scenes[index].battle : new SceneBattleState())
        };
        if (index < 0)
        {
            var scenes = new CampaignScene[_campaign.scenes.Length + 1];
            Array.Copy(_campaign.scenes, scenes, _campaign.scenes.Length);
            scenes[scenes.Length - 1] = snapshot;
            _campaign.scenes = scenes;
        }
        else _campaign.scenes[index] = snapshot;
        _campaign.activeSceneId = scene.sceneId;
    }

    public static void CreateSceneCopy() => Safely(CreateSceneCopyCore);

    private static void CreateSceneCopyCore()
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        if (_campaign.scenes.Length >= 128)
        {
            DiceUI.Instance?.ShowToolNotice("В кампании достигнут лимит в 128 сцен.");
            return;
        }
        var current = Array.Find(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
        if (current == null) return;
        string title = $"Сцена {_campaign.scenes.Length + 1}";
        var scene = JsonUtility.FromJson<SceneDefinition>(JsonUtility.ToJson(current.scene));
        scene.sceneId = Guid.NewGuid().ToString("N");
        scene.title = title;
        var copy = new CampaignScene
        {
            sceneId = scene.sceneId,
            title = title,
            scene = scene,
            battle = JsonUtility.FromJson<SceneBattleState>(JsonUtility.ToJson(current.battle))
        };
        var scenes = new CampaignScene[_campaign.scenes.Length + 1];
        Array.Copy(_campaign.scenes, scenes, _campaign.scenes.Length);
        scenes[scenes.Length - 1] = copy;
        _campaign.scenes = scenes;
        SwitchToScene(copy.sceneId);
    }

    public static void CycleScene(int direction) => Safely(() => CycleSceneCore(direction));

    private static void CycleSceneCore(int direction)
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
        if (index < 0 || _campaign.scenes.Length <= 1) return;
        int next = (index + direction % _campaign.scenes.Length + _campaign.scenes.Length)
            % _campaign.scenes.Length;
        SwitchToScene(_campaign.scenes[next].sceneId);
    }

    private static void SwitchToScene(string sceneId)
    {
        var target = Array.Find(_campaign.scenes, item => item.sceneId == sceneId);
        if (target == null) return;
        ApplyScene(target.scene, target.battle);
        _campaign.activeSceneId = sceneId;
        DiceUI.Instance?.ShowToolNotice($"Активная сцена: {target.title}");
    }

    public static void RenameActiveScene(string title) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        title = (title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 64) throw new ArgumentException("Название сцены должно содержать от 1 до 64 символов.");
        int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
        if (index < 0) throw new InvalidOperationException("Активная сцена не найдена.");
        _campaign.scenes[index].title = title;
        _campaign.scenes[index].scene.title = title;
        DiceUI.Instance?.ShowToolNotice("Сцена переименована: " + title);
    });

    public static void DeleteActiveScene() => Safely(DeleteActiveSceneCore);

    private static void DeleteActiveSceneCore()
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        CaptureCampaign(_campaign.title);
        if (_campaign.scenes.Length <= 1)
        {
            DiceUI.Instance?.ShowToolNotice("Нельзя удалить последнюю сцену кампании.");
            return;
        }
        int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
        if (index < 0) throw new InvalidOperationException("Активная сцена не найдена.");
        int nextIndex = (index + 1) % _campaign.scenes.Length;
        string nextSceneId = _campaign.scenes[nextIndex].sceneId;
        CampaignDefinition previousCampaign = JsonUtility.FromJson<CampaignDefinition>(JsonUtility.ToJson(_campaign));
        var remaining = new CampaignScene[_campaign.scenes.Length - 1];
        for (int source = 0, destination = 0; source < _campaign.scenes.Length; source++)
            if (source != index) remaining[destination++] = _campaign.scenes[source];
        _campaign.scenes = remaining;
        try { SwitchToScene(nextSceneId); }
        catch { _campaign = previousCampaign; throw; }
        DiceUI.Instance?.ShowToolNotice("Сцена удалена.");
    }

    public static void EnsureCampaign() => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        if (!HasCampaign) CaptureCampaign("Кампания");
    });
    public static bool HasUnsavedChanges()
    {
        if (NetworkManager.Singleton?.IsHost != true) return false;
        try { return JsonUtility.ToJson(Capture(true)) != _lastSavedState; }
        catch { return true; }
    }
    public static SceneDefinition Capture(bool history = true)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер сохраняет сцену.");
        var grid = UnityEngine.Object.FindAnyObjectByType<GridManager>();
        var map = MapController.Instance;
        if (grid == null || map == null || SceneEditor.Instance == null) throw new InvalidOperationException("Карта не готова.");
        var mapBytes = map.GetCurrentPngData();
        if (mapBytes != null && mapBytes.Length > MapSync.MaxMapBytes)
            throw new FormatException("PNG карты превышает 16 МБ. Уменьшите изображение перед сохранением.");
        var scene = new SceneDefinition {
            sceneId = _currentSceneId,
            title = _campaign == null ? "Сцена" :
                Array.Find(_campaign.scenes, item => item.sceneId == _currentSceneId)?.title ?? "Сцена",
            gridWidth = grid.Width, gridHeight = grid.Height, cellSize = grid.CellSize,
            gridPosition = grid.GridOrigin, gridRotation = grid.GridRotation.eulerAngles,
            mapPosition = map.transform.position, mapRotation = map.transform.eulerAngles, mapScale = map.CurrentScale,
            geometry = SceneEditor.Instance.Model.Snapshot(), mapImage = Encode(mapBytes), includesPlayers = true,
            fog = FogManager.Instance?.Capture(history) ?? new SavedFog()
        };
        var tokens = new List<SceneToken>();
        var masterTokens = new List<MasterTokenData>();
        foreach (var token in UnityEngine.Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (!token.IsSpawned) continue;
            if (token.IsLocalDragActiveAny()) throw new InvalidOperationException("Закончите перемещение токенов перед сохранением.");
            tokens.Add(CaptureToken(token, grid));
            masterTokens.Add(token.CaptureMasterData());
        }
        scene.tokens = tokens.ToArray();
        scene.masterData.tokens = masterTokens.ToArray();
        SceneValidation.Validate(scene);
        return scene;
    }
    public static SceneToken CaptureToken(TokenController token, GridManager grid) => new SceneToken {
        id = token.SceneId, name = token.TokenName, nameBase = token.NameBase,
        position = grid.WorldToGridCoordinates(token.CommittedPosition), scale = token.transform.localScale,
        hidden = token.IsHidden, visionFeet = token.VisionFeet, portrait = Encode(token.GetPortraitJpg()),
        hero = token.IsHero, everyoneCanMove = token.EveryoneCanMove,
        unassigned = token.ControllerClientId == ulong.MaxValue && string.IsNullOrEmpty(token.SavedOwnerNickname),
        ownerNickname = token.ControllerClientId != NetworkManager.ServerClientId
            ? PlayerColors.GetNickname(token.ControllerClientId) ?? token.SavedOwnerNickname : null
    };
    private static string Encode(byte[] bytes) => bytes == null || bytes.Length == 0 ? "" : Convert.ToBase64String(bytes);
    private static byte[] Decode(string value, int max)
    {
        if (string.IsNullOrEmpty(value)) return null;
        if (value.Length > (long)max * 4 / 3 + 8) throw new FormatException("Изображение слишком большое.");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length > max) throw new FormatException("Изображение слишком большое.");
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        try
        {
            if (!texture.LoadImage(bytes) || texture.width > 16384 || texture.height > 16384)
                throw new FormatException("Не удалось прочитать изображение.");
        }
        finally { UnityEngine.Object.Destroy(texture); }
        return bytes;
    }
    public static void Save(string path) => SaveOptions(path, null, true);
    public static void SaveOptions(string path, bool? history, bool notify)
    {
        SceneEditor.Instance?.Deactivate();
        string json = JsonUtility.ToJson(Capture(history ?? FogManager.Instance?.SaveWithHistory ?? true), true);
        if (Encoding.UTF8.GetByteCount(json) > SceneValidation.MaxFileBytes) throw new FormatException("Сцена превышает 96 МБ.");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(temporary, path, null);
        else File.Move(temporary, path);
        if (notify) _lastSavedState = JsonUtility.ToJson(Capture(true));
        if (notify) DiceUI.Instance?.ShowToolNotice("Сцена сохранена: " + Path.GetFileName(path));
    }
    public static void Load(string path)
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        if (new FileInfo(path).Length > SceneValidation.MaxFileBytes) throw new FormatException("Сцена превышает 96 МБ.");
        var scene = SceneSaveMigration.UpgradeScene(
            JsonUtility.FromJson<SceneDefinition>(File.ReadAllText(path)));
        _campaign = null;
        ApplyScene(scene, null);
    }

    public static void LoadCampaign(string path)
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        CampaignDefinition campaign = CampaignFileStore.LoadCompatible(path);
        CampaignScene active = Array.Find(campaign.scenes, item => item.sceneId == campaign.activeSceneId);
        if (active == null) throw new FormatException("Активная сцена не найдена в сессии.");
        var previousCampaign = _campaign;
        _campaign = campaign;
        try { ApplyScene(active.scene, active.battle); }
        catch { _campaign = previousCampaign; throw; }
        DiceUI.Instance?.ShowToolNotice($"Сессия «{campaign.title}» загружена · сцена «{active.title}».");
    }

    private static void ApplyScene(SceneDefinition scene, SceneBattleState battle)
    {
        SceneValidation.Validate(scene);
        byte[] map = Decode(scene.mapImage, MapSync.MaxMapBytes);
        var portraits = new Dictionary<string, byte[]>();
        var masterTokens = new Dictionary<string, MasterTokenData>();
        foreach (var master in scene.masterData.tokens) masterTokens.Add(master.tokenId, master);
        foreach (var token in scene.tokens) portraits.Add(token.id, Decode(token.portrait, TokenImageSync.MaxPortraitBytes));
        if (MapController.Instance == null || MapSync.Instance == null || TokenManager.Instance?.tokenPrefab == null || SceneEditor.Instance == null)
            throw new InvalidOperationException("Редактор сцены не готов.");
        var grid = UnityEngine.Object.FindAnyObjectByType<GridManager>();
        if (grid == null) throw new InvalidOperationException("Сетка не готова.");
        // No current object is changed until the complete document and all images have been validated.
        SceneEditor.Instance.Deactivate();
        FogManager.Instance?.StopManual();
        GameMasterUndo.Clear();
        bool curtain = HostSceneCurtain.IsCurtainDown;
        if (!curtain) HostSceneCurtain.Instance?.ToggleCurtainOnHost();
        try
        {
            ulong host = NetworkManager.Singleton.LocalClientId;
            foreach (var token in UnityEngine.Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
                if (token.IsSpawned && (scene.includesPlayers || token.SpawnerClientId == host)) token.NetworkObject.Despawn();
            CellMarker.ClearAllMarkersAsHost();
            grid.RestoreGrid(scene.gridWidth, scene.gridHeight, scene.cellSize, scene.gridPosition, Quaternion.Euler(scene.gridRotation));
            SceneEditor.Instance.Replace(scene.geometry);
            FogManager.Instance?.Restore(scene.fog);
            MapController.Instance.RestoreSceneMap(map, scene.mapPosition, scene.mapRotation, scene.mapScale);
            foreach (var data in scene.tokens)
            {
                masterTokens.TryGetValue(data.id, out MasterTokenData masterData);
                var token = TokenManager.Instance.RestoreSceneToken(data, grid, masterData);
                if (token == null) throw new InvalidOperationException("Не удалось восстановить токен.");
                byte[] portrait = portraits[data.id];
                if (portrait != null) TokenImageSync.BroadcastImage(token.NetworkObjectId, portrait);
            }
            TokenController.RebuildCellOccupancy();
            InitiativeTracker.Instance?.RestoreBattleState(battle ?? new SceneBattleState());
        }
        finally { if (!curtain && HostSceneCurtain.IsCurtainDown) SceneEditor.Instance.RevealAfterMapTransfer(); }
        _currentSceneId = scene.sceneId;
        _lastSavedState = JsonUtility.ToJson(Capture(true));
    }
    private static void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { DiceUI.Instance?.ShowToolNotice("Сцена: " + ex.Message); Debug.LogWarning("[Scene] " + ex.Message); }
    }
    public static void LoadAutosave()
    {
        string path = Path.Combine(Application.persistentDataPath, "autosave-scene.json");
        if (!File.Exists(path)) { DiceUI.Instance?.ShowToolNotice("Автосохранения пока нет."); return; }
        DiceUI.Instance?.ConfirmAction("Восстановить автосохранение?", "Текущая сцена будет заменена последней сохранённой копией.", () => Safely(() => Load(path)));
    }
    public static void SaveDialog()
    {
#if UNITY_EDITOR
        string path = EditorUtility.SaveFilePanel("Сохранить сцену", "", "scene.json", "json");
        if (!string.IsNullOrEmpty(path)) Safely(() => Save(path));
#else
        SimpleFileBrowser.FileBrowser.ShowSaveDialog(paths => { if (paths.Length > 0) Safely(() => Save(paths[0])); },
            null, SimpleFileBrowser.FileBrowser.PickMode.Files, false, null, "scene.json", "Сохранить сцену", "Сохранить");
#endif
    }
    public static void LoadDialog()
    {
        void Confirm(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            DiceUI.Instance?.ConfirmAction("Заменить сцену?", "Карта, разметка и сохранённые токены будут восстановлены из файла. Персонажи назначаются по никам участников.",
                () => Safely(() => Load(path)));
        }
#if UNITY_EDITOR
        Confirm(EditorUtility.OpenFilePanel("Загрузить сцену", "", "json"));
#else
        SimpleFileBrowser.FileBrowser.ShowLoadDialog(paths => { if (paths.Length > 0) Confirm(paths[0]); }, null,
            SimpleFileBrowser.FileBrowser.PickMode.Files, false, null, null, "Загрузить сцену", "Загрузить");
#endif
    }

    public static void SaveCampaignDialog()
    {
#if UNITY_EDITOR
        string path = EditorUtility.SaveFilePanel("Сохранить сессию", "", "campaign.json", "json");
        if (!string.IsNullOrEmpty(path)) Safely(() => SaveCampaign(path));
#else
        SimpleFileBrowser.FileBrowser.ShowSaveDialog(paths =>
        {
            if (paths.Length > 0) Safely(() => SaveCampaign(paths[0]));
        }, null, SimpleFileBrowser.FileBrowser.PickMode.Files, false, null,
            "campaign.json", "Сохранить сессию", "Сохранить");
#endif
    }

    public static void LoadCampaignDialog()
    {
        void Confirm(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            DiceUI.Instance?.ConfirmAction("Загрузить сессию?",
                "Текущая сцена и инициатива будут заменены активной сценой из файла сессии.",
                () => Safely(() => LoadCampaign(path)));
        }
#if UNITY_EDITOR
        Confirm(EditorUtility.OpenFilePanel("Загрузить сессию", "", "json"));
#else
        SimpleFileBrowser.FileBrowser.ShowLoadDialog(paths =>
        {
            if (paths.Length > 0) Confirm(paths[0]);
        }, null, SimpleFileBrowser.FileBrowser.PickMode.Files, false, null, null,
            "Загрузить сессию", "Загрузить");
#endif
    }
}
