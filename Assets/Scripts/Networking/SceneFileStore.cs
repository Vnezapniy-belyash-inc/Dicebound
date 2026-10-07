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
    private static string _currentMapAssetId;
    private static CampaignDefinition _campaign;
    private static bool _campaignDirty;

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
    public static string ActiveSceneId => _campaign?.activeSceneId;

    public static StatBlockDefinition[] GetStatBlocks() => _campaign?.statBlocks ?? Array.Empty<StatBlockDefinition>();

    public static ReferenceEntry[] GetReferenceEntries() => _campaign?.referenceEntries ?? Array.Empty<ReferenceEntry>();

    public static void UpsertReferenceEntry(ReferenceEntry entry)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер редактирует справочник.");
        if (entry == null) throw new ArgumentNullException(nameof(entry));
        CaptureCampaign(_campaign?.title ?? "Кампания");
        entry.id = string.IsNullOrWhiteSpace(entry.id) ? Guid.NewGuid().ToString("N") : entry.id;
        entry.title = (entry.title ?? string.Empty).Trim();
        entry.category = (entry.category ?? string.Empty).Trim();
        entry.body ??= string.Empty;
        entry.tags ??= Array.Empty<string>();
        if (entry.title.Length == 0) throw new FormatException("Укажите название записи.");
        if (entry.category.Length == 0) entry.category = "Без категории";
        for (int i = 0; i < entry.tags.Length; i++) entry.tags[i] = (entry.tags[i] ?? string.Empty).Trim();
        var previous = _campaign.referenceEntries ?? Array.Empty<ReferenceEntry>();
        int index = Array.FindIndex(previous, item => item.id == entry.id);
        var updated = (ReferenceEntry[])previous.Clone();
        if (index < 0)
        {
            Array.Resize(ref updated, updated.Length + 1);
            index = updated.Length - 1;
        }
        updated[index] = entry;
        _campaign.referenceEntries = updated;
        try { SceneValidation.Validate(_campaign); }
        catch { _campaign.referenceEntries = previous; throw; }
        _campaignDirty = true;
        DmPanelUI.Instance?.OnReferenceLibraryChanged();
    }

    public static void DeleteReferenceEntry(string id)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер редактирует справочник.");
        if (string.IsNullOrWhiteSpace(id)) return;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        var entries = _campaign.referenceEntries ?? Array.Empty<ReferenceEntry>();
        int index = Array.FindIndex(entries, item => item.id == id);
        if (index < 0) return;
        var updated = new ReferenceEntry[entries.Length - 1];
        if (index > 0) Array.Copy(entries, 0, updated, 0, index);
        if (index < updated.Length) Array.Copy(entries, index + 1, updated, index, updated.Length - index);
        _campaign.referenceEntries = updated;
        _campaignDirty = true;
        DmPanelUI.Instance?.OnReferenceLibraryChanged();
    }

    public static void ExportReferenceLibrary(string path)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер экспортирует справочник.");
        if (string.IsNullOrWhiteSpace(path)) return;
        var file = new ReferenceLibraryFile { entries = GetReferenceEntries() };
        string json = JsonUtility.ToJson(file, true);
        if (Encoding.UTF8.GetByteCount(json) > 16 * 1024 * 1024)
            throw new InvalidOperationException("Файл справочника превышает лимит 16 МБ.");
        WriteAtomically(path, json);
    }

    public static void ImportReferenceLibrary(string path)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер импортирует справочник.");
        if (string.IsNullOrWhiteSpace(path)) return;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 16 * 1024 * 1024)
            throw new InvalidOperationException("Файл справочника отсутствует или превышает лимит 16 МБ.");
        var file = JsonUtility.FromJson<ReferenceLibraryFile>(File.ReadAllText(path, Encoding.UTF8));
        ValidateReferenceLibrary(file);
        CaptureCampaign(_campaign?.title ?? "Кампания");
        var previous = _campaign.referenceEntries;
        _campaign.referenceEntries = file.entries;
        try { SceneValidation.Validate(_campaign); }
        catch { _campaign.referenceEntries = previous; throw; }
        _campaignDirty = true;
        DmPanelUI.Instance?.OnReferenceLibraryChanged();
    }

    private static void ValidateReferenceLibrary(ReferenceLibraryFile file)
    {
        if (file == null || file.version != 1 || file.entries == null || file.entries.Length > 10000)
            throw new FormatException("Неподдерживаемый или повреждённый файл справочника.");
        var ids = new HashSet<string>();
        foreach (var entry in file.entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id) || entry.id.Length > 64 || !ids.Add(entry.id)
                || string.IsNullOrWhiteSpace(entry.title) || entry.title.Length > 256
                || entry.category == null || entry.category.Length > 128
                || entry.body == null || entry.body.Length > 65536
                || entry.tags == null || entry.tags.Length > 64)
                throw new FormatException("Файл содержит некорректную запись справочника.");
            foreach (string tag in entry.tags)
                if (string.IsNullOrWhiteSpace(tag) || tag.Length > 64)
                    throw new FormatException("Файл содержит некорректную метку справочника.");
        }
    }

    private static void WriteAtomically(string path, string content)
    {
        string fullPath = Path.GetFullPath(path);
        string temporaryPath = fullPath + ".tmp";
        File.WriteAllText(temporaryPath, content, Encoding.UTF8);
        try
        {
            if (File.Exists(fullPath)) File.Replace(temporaryPath, fullPath, null);
            else File.Move(temporaryPath, fullPath);
        }
        finally { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
    }

    public static void UpsertStatBlock(StatBlockDefinition definition)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер редактирует статблоки.");
        if (definition == null) throw new ArgumentNullException(nameof(definition));
        CaptureCampaign(_campaign?.title ?? "Кампания");
        definition.id = string.IsNullOrWhiteSpace(definition.id) ? Guid.NewGuid().ToString("N") : definition.id;
        definition.name = (definition.name ?? string.Empty).Trim();
        if (definition.name.Length == 0) throw new FormatException("Укажите название статблока.");
        var previous = _campaign.statBlocks ?? Array.Empty<StatBlockDefinition>();
        int index = Array.FindIndex(previous, item => item.id == definition.id);
        var updated = (StatBlockDefinition[])previous.Clone();
        if (index < 0)
        {
            Array.Resize(ref updated, updated.Length + 1);
            index = updated.Length - 1;
        }
        updated[index] = definition;
        _campaign.statBlocks = updated;
        try { SceneValidation.Validate(_campaign); }
        catch { _campaign.statBlocks = previous; throw; }
        _campaignDirty = true;
        foreach (var token in FindObjectsByType<TokenController>(FindObjectsSortMode.None))
            if (token != null && token.IsSpawned) token.RefreshPublicStatBlock();
    }

    public static void DeleteStatBlock(string statBlockId)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер редактирует статблоки.");
        if (string.IsNullOrWhiteSpace(statBlockId)) return;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        foreach (var scene in _campaign.scenes)
            foreach (var token in scene.scene.masterData.tokens)
                if (token.statBlockId == statBlockId)
                    throw new InvalidOperationException("Сначала отвяжите этот статблок от всех токенов.");
        int index = Array.FindIndex(_campaign.statBlocks, item => item.id == statBlockId);
        if (index < 0) return;
        var updated = new StatBlockDefinition[_campaign.statBlocks.Length - 1];
        if (index > 0) Array.Copy(_campaign.statBlocks, 0, updated, 0, index);
        if (index < updated.Length) Array.Copy(_campaign.statBlocks, index + 1, updated, index, updated.Length - index);
        _campaign.statBlocks = updated;
        _campaignDirty = true;
    }

    public static bool HasStatBlock(string statBlockId) => !string.IsNullOrWhiteSpace(statBlockId)
        && Array.Exists(GetStatBlocks(), item => item != null && item.id == statBlockId);

    public static CampaignMapAsset[] GetMapAssets() => _campaign?.mapAssets ?? Array.Empty<CampaignMapAsset>();

    public static void ResetCurrentMapAssetLink()
    {
        if (NetworkManager.Singleton?.IsHost == true) _currentMapAssetId = null;
    }

    public static string ValidateNewMapAssetName(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Length > 128)
            throw new FormatException("Название карты должно содержать от 1 до 128 символов.");
        if (_campaign?.mapAssets != null && Array.Exists(_campaign.mapAssets,
            item => string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase)))
            throw new FormatException("В кампании уже есть карта с таким названием.");
        if (_campaign?.mapAssets != null && _campaign.mapAssets.Length >= 256)
            throw new FormatException("В каталоге достигнут лимит в 256 карт.");
        return name;
    }

    public static void AddCurrentMapAsset(string name)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер редактирует каталог карт.");
        name = ValidateNewMapAssetName(name);
        var map = MapController.Instance;
        byte[] bytes = map?.GetCurrentPngData();
        if (bytes == null || bytes.Length == 0) throw new InvalidOperationException("Сначала загрузите изображение карты.");
        _currentMapAssetId = null;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        var asset = new CampaignMapAsset { id = Guid.NewGuid().ToString("N"), name = name, imageData = Convert.ToBase64String(bytes) };
        var assets = new CampaignMapAsset[_campaign.mapAssets.Length + 1];
        Array.Copy(_campaign.mapAssets, assets, _campaign.mapAssets.Length);
        assets[assets.Length - 1] = asset;
        var previousAssets = _campaign.mapAssets;
        _campaign.mapAssets = assets;
        _currentMapAssetId = asset.id;
        var scene = Array.Find(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
        if (scene != null) scene.scene.mapAssetId = asset.id;
        try
        {
            SceneValidation.Validate(_campaign);
            CampaignFileStore.ValidateEmbeddedImageBudget(_campaign);
        }
        catch
        {
            _campaign.mapAssets = previousAssets;
            _currentMapAssetId = null;
            if (scene != null) scene.scene.mapAssetId = null;
            throw;
        }
        _campaignDirty = true;
    }

    public static int ImportMapAssets(string[] names, byte[][] imageBytes)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер импортирует карты.");
        if (names == null || imageBytes == null || names.Length != imageBytes.Length
            || names.Length == 0 || names.Length > 32)
            throw new FormatException("Выберите от 1 до 32 файлов изображений.");
        CaptureCampaign(_campaign?.title ?? "Кампания");
        if (_campaign.mapAssets.Length + names.Length > 256)
            throw new InvalidOperationException("В каталоге кампании достигнут лимит в 256 карт.");

        var existingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in _campaign.mapAssets) existingNames.Add(map.name);
        var imported = new CampaignMapAsset[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            if (imageBytes[i] == null || imageBytes[i].Length == 0 || imageBytes[i].Length > MapSync.MaxMapBytes)
                throw new FormatException("Изображение должно занимать от 1 байта до 16 МБ после обработки.");
            string baseName = Path.GetFileNameWithoutExtension(names[i] ?? string.Empty).Trim();
            if (baseName.Length > 128) baseName = baseName.Substring(0, 128).Trim();
            if (baseName.Length == 0) baseName = "Карта";
            string name = baseName;
            int suffix = 2;
            while (existingNames.Contains(name))
            {
                string tail = " (" + suffix++ + ")";
                name = baseName.Substring(0, Mathf.Min(baseName.Length, 128 - tail.Length)) + tail;
            }
            existingNames.Add(name);
            imported[i] = new CampaignMapAsset
            {
                id = Guid.NewGuid().ToString("N"), name = name,
                imageData = Convert.ToBase64String(imageBytes[i])
            };
        }

        var previous = _campaign.mapAssets;
        var updated = new CampaignMapAsset[previous.Length + imported.Length];
        Array.Copy(previous, updated, previous.Length);
        Array.Copy(imported, 0, updated, previous.Length, imported.Length);
        _campaign.mapAssets = updated;
        try
        {
            SceneValidation.Validate(_campaign);
            CampaignFileStore.ValidateEmbeddedImageBudget(_campaign);
        }
        catch { _campaign.mapAssets = previous; throw; }
        _campaignDirty = true;
        return imported.Length;
    }

    public static void SelectMapAsset(string id)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер выбирает карту.");
        var asset = Array.Find(GetMapAssets(), item => item.id == id);
        if (asset == null) throw new InvalidOperationException("Карта не найдена в каталоге кампании.");
        var map = MapController.Instance;
        if (map == null) throw new InvalidOperationException("Контроллер карты не готов.");
        CaptureCampaign(_campaign.title);
        var bytes = Decode(asset.imageData, MapSync.MaxMapBytes);
        Vector3 position = map.transform.position;
        Vector3 rotation = map.transform.eulerAngles;
        float scale = map.CurrentScale;
        map.RestoreSceneMap(bytes, position, rotation, scale);
        _currentMapAssetId = asset.id;
        _campaignDirty = true;
    }

    public static void DeleteMapAsset(string id)
    {
        if (NetworkManager.Singleton?.IsHost != true) throw new InvalidOperationException("Только мастер редактирует каталог карт.");
        if (string.IsNullOrWhiteSpace(id)) return;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        foreach (var scene in _campaign.scenes)
            if (scene.scene.mapAssetId == id) throw new InvalidOperationException("Сначала отвяжите карту от всех сцен.");
        int index = Array.FindIndex(_campaign.mapAssets, item => item.id == id);
        if (index < 0) return;
        var assets = new CampaignMapAsset[_campaign.mapAssets.Length - 1];
        if (index > 0) Array.Copy(_campaign.mapAssets, 0, assets, 0, index);
        if (index < assets.Length) Array.Copy(_campaign.mapAssets, index + 1, assets, index, assets.Length - index);
        _campaign.mapAssets = assets;
        _campaignDirty = true;
    }

    public static CampaignDefinition CaptureCampaign(string title = null)
    {
        var scene = Capture(true);
        if (_campaign == null)
            _campaign = SceneSaveMigration.UpgradeSingleScene(scene,
                string.IsNullOrWhiteSpace(title) ? "Кампания" : title);
        else
        {
            if (!string.IsNullOrWhiteSpace(title) && _campaign.title != title)
            {
                _campaign.title = title;
                _campaignDirty = true;
            }
            SceneBattleState battle = InitiativeTracker.Instance?.CaptureBattleState();
            int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == scene.sceneId);
            if (index >= 0 && (JsonUtility.ToJson(_campaign.scenes[index].scene) != JsonUtility.ToJson(scene)
                || battle != null && JsonUtility.ToJson(_campaign.scenes[index].battle) != JsonUtility.ToJson(battle)))
                _campaignDirty = true;
            UpdateCampaignScene(scene, battle);
        }
        SceneValidation.Validate(_campaign);
        CampaignFileStore.ValidateEmbeddedImageBudget(_campaign);
        return _campaign;
    }

    public static void SaveCampaign(string path, string title = null)
    {
        CampaignFileStore.Save(path, CaptureCampaign(title));
        _lastSavedState = JsonUtility.ToJson(Capture(true));
        _campaignDirty = false;
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

    public static void CreateEmptyScene() => Safely(CreateEmptySceneCore);

    private static void CreateEmptySceneCore()
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        CaptureCampaign(_campaign?.title ?? "Кампания");
        bool previousDirty = _campaignDirty;
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
        scene.transitions = Array.Empty<SceneTransition>();
        scene.tokens = Array.Empty<SceneToken>();
        scene.masterData = new SceneMasterData();
        scene.geometry = new SceneGeometry { revealPaused = true };
        scene.fog = new SavedFog { enabled = current.scene.fog == null || current.scene.fog.enabled };
        var created = new CampaignScene
        {
            sceneId = scene.sceneId,
            title = title,
            scene = scene,
            battle = new SceneBattleState()
        };
        var scenes = new CampaignScene[_campaign.scenes.Length + 1];
        var previousScenes = _campaign.scenes;
        Array.Copy(_campaign.scenes, scenes, _campaign.scenes.Length);
        scenes[scenes.Length - 1] = created;
        _campaign.scenes = scenes;
        _campaignDirty = true;
        try
        {
            SceneValidation.Validate(_campaign);
            CampaignFileStore.ValidateEmbeddedImageBudget(_campaign);
            SwitchToScene(created.sceneId);
        }
        catch
        {
            _campaign.scenes = previousScenes;
            _campaignDirty = previousDirty;
            throw;
        }
    }

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
        scene.transitions = Array.Empty<SceneTransition>();
        var copy = new CampaignScene
        {
            sceneId = scene.sceneId,
            title = title,
            scene = scene,
            battle = JsonUtility.FromJson<SceneBattleState>(JsonUtility.ToJson(current.battle))
        };
        var scenes = new CampaignScene[_campaign.scenes.Length + 1];
        var previousScenes = _campaign.scenes;
        bool previousDirty = _campaignDirty;
        Array.Copy(_campaign.scenes, scenes, _campaign.scenes.Length);
        scenes[scenes.Length - 1] = copy;
        _campaign.scenes = scenes;
        _campaignDirty = true;
        try
        {
            SceneValidation.Validate(_campaign);
            CampaignFileStore.ValidateEmbeddedImageBudget(_campaign);
            SwitchToScene(copy.sceneId);
        }
        catch
        {
            _campaign.scenes = previousScenes;
            _campaignDirty = previousDirty;
            throw;
        }
    }

    public static void CycleScene(int direction) => Safely(() => CycleSceneCore(direction));

    public static CampaignScene[] GetCampaignScenes() => _campaign?.scenes ?? Array.Empty<CampaignScene>();

    public static SceneTransition[] GetActiveSceneTransitions()
    {
        var active = Array.Find(GetCampaignScenes(), item => item != null && item.sceneId == _campaign?.activeSceneId);
        return active?.scene?.transitions ?? Array.Empty<SceneTransition>();
    }

    public static void AddSceneTransition(string title, string targetSceneId, int x = 0, int y = 0) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        var active = Array.Find(_campaign.scenes, item => item.sceneId == _campaign.activeSceneId);
        var target = Array.Find(_campaign.scenes, item => item.sceneId == targetSceneId);
        if (active == null || target == null || active.sceneId == target.sceneId)
            throw new InvalidOperationException("Выберите другую существующую сцену.");
        title = (title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 128) throw new ArgumentException("Название перехода должно содержать от 1 до 128 символов.");
        var grid = UnityEngine.Object.FindAnyObjectByType<GridManager>();
        if (grid == null || !grid.IsPointOnMap(grid.GetCellCenter(x, y)))
            throw new InvalidOperationException("Выберите клетку внутри карты.");
        var transitions = active.scene.transitions ?? Array.Empty<SceneTransition>();
        if (transitions.Length >= 256) throw new InvalidOperationException("В сцене достигнут лимит переходов.");
        var updated = new SceneTransition[transitions.Length + 1];
        Array.Copy(transitions, updated, transitions.Length);
        var created = new SceneTransition
        {
            id = Guid.NewGuid().ToString("N"), title = title, targetSceneId = target.sceneId, x = x, y = y
        };
        updated[updated.Length - 1] = created;
        active.scene.transitions = updated;
        try { SceneValidation.Validate(_campaign); }
        catch { active.scene.transitions = transitions; throw; }
        _campaignDirty = true;
        RefreshTransitionMarkers();
        DiceUI.Instance?.ShowToolNotice("Переход добавлен: " + title);
    });

    public static void UpdateSceneTransition(string transitionId, string title, string targetSceneId, int x, int y) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true || string.IsNullOrWhiteSpace(transitionId)) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        CaptureCampaign(_campaign.title);
        var active = Array.Find(_campaign.scenes, item => item != null && item.sceneId == _campaign.activeSceneId);
        var target = Array.Find(_campaign.scenes, item => item != null && item.sceneId == targetSceneId);
        var transition = Array.Find(active?.scene?.transitions ?? Array.Empty<SceneTransition>(), item => item != null && item.id == transitionId);
        if (active == null || transition == null || target == null || target.sceneId == active.sceneId)
            throw new InvalidOperationException("Выберите другую существующую сцену.");
        title = (title ?? string.Empty).Trim();
        if (title.Length == 0 || title.Length > 128) throw new ArgumentException("Название перехода должно содержать от 1 до 128 символов.");
        var grid = UnityEngine.Object.FindAnyObjectByType<GridManager>();
        if (grid == null || !grid.IsPointOnMap(grid.GetCellCenter(x, y)))
            throw new InvalidOperationException("Выберите клетку внутри карты.");
        var backup = JsonUtility.FromJson<SceneTransition>(JsonUtility.ToJson(transition));
        transition.title = title;
        transition.targetSceneId = target.sceneId;
        transition.x = x;
        transition.y = y;
        try { SceneValidation.Validate(_campaign); }
        catch
        {
            transition.title = backup.title;
            transition.targetSceneId = backup.targetSceneId;
            transition.x = backup.x;
            transition.y = backup.y;
            throw;
        }
        RefreshTransitionMarkers();
        _campaignDirty = true;
        DiceUI.Instance?.ShowToolNotice("Переход обновлён: " + title);
    });

    public static void DeleteSceneTransition(string transitionId) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true || string.IsNullOrWhiteSpace(transitionId)) return;
        var active = Array.Find(GetCampaignScenes(), item => item != null && item.sceneId == _campaign?.activeSceneId);
        var transitions = active?.scene?.transitions ?? Array.Empty<SceneTransition>();
        int index = Array.FindIndex(transitions, item => item != null && item.id == transitionId);
        if (index < 0) return;
        var deleted = transitions[index];
        var updated = new SceneTransition[transitions.Length - 1];
        if (index > 0) Array.Copy(transitions, 0, updated, 0, index);
        if (index < updated.Length) Array.Copy(transitions, index + 1, updated, index, updated.Length - index);
        active.scene.transitions = updated;
        var marker = Array.Find(UnityEngine.Object.FindObjectsByType<SceneTransitionMarker>(FindObjectsInactive.Include),
            item => item != null && item.TransitionId == deleted.id);
        if (marker != null) marker.SetMarkerEnabled(false);
        _campaignDirty = true;
        RefreshTransitionMarkers();
    });

    public static void RefreshTransitionMarkers()
    {
        var existing = UnityEngine.Object.FindObjectsByType<SceneTransitionMarker>(FindObjectsInactive.Include);
        if (NetworkManager.Singleton?.IsServer != true)
        {
            foreach (var marker in existing) if (marker != null) marker.SetMarkerEnabled(false);
            return;
        }
        if (!HasCampaign)
        {
            foreach (var marker in existing) if (marker != null) marker.SetMarkerEnabled(false);
            return;
        }
        var scene = Array.Find(_campaign.scenes, item => item != null && item.sceneId == _campaign.activeSceneId)?.scene;
        var grid = UnityEngine.Object.FindAnyObjectByType<GridManager>();
        if (scene?.transitions == null || grid == null)
        {
            foreach (var marker in existing) if (marker != null) marker.SetMarkerEnabled(false);
            return;
        }
        var seen = new HashSet<string>();
        foreach (var transition in scene.transitions)
        {
            if (transition == null || !transition.markerEnabled
                || !grid.IsPointOnMap(grid.GetCellCenter(transition.x, transition.y))) continue;
            seen.Add(transition.id);
            var marker = Array.Find(existing, item => item != null && item.TransitionId == transition.id);
            Vector3 position = grid.GetCellCenter(transition.x, transition.y, 0.025f);
            if (marker == null) SceneTransitionMarker.Spawn(position, transition.id, transition.title);
            else
            {
                marker.SetMarkerPosition(position);
                marker.SetMarkerEnabled(true);
                marker.Refresh(transition.title);
            }
        }
        foreach (var marker in existing)
            if (marker != null && !seen.Contains(marker.TransitionId))
            {
                if (marker.IsSpawned) marker.SetMarkerEnabled(false);
                else UnityEngine.Object.Destroy(marker.gameObject);
            }
    }

    public static void UseSceneTransition(string transitionId) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true || string.IsNullOrWhiteSpace(transitionId)) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        CaptureCampaign(_campaign.title);
        var active = Array.Find(_campaign.scenes, item => item != null && item.sceneId == _campaign.activeSceneId);
        var transition = Array.Find(active?.scene?.transitions ?? Array.Empty<SceneTransition>(),
            item => item != null && item.id == transitionId);
        if (transition == null) throw new InvalidOperationException("Переход не найден.");
        SwitchToScene(transition.targetSceneId);
    });

    public static void ToggleSceneTransitionMarker(string transitionId) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true || string.IsNullOrWhiteSpace(transitionId)) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        CaptureCampaign(_campaign.title);
        var active = Array.Find(_campaign.scenes, item => item != null && item.sceneId == _campaign.activeSceneId);
        var transition = Array.Find(active?.scene?.transitions ?? Array.Empty<SceneTransition>(),
            item => item != null && item.id == transitionId);
        if (transition == null) throw new InvalidOperationException("Переход не найден.");
        var grid = UnityEngine.Object.FindAnyObjectByType<GridManager>();
        if (grid == null || !grid.IsPointOnMap(grid.GetCellCenter(transition.x, transition.y)))
            throw new InvalidOperationException("Клетка перехода находится за пределами карты.");
        transition.markerEnabled = !transition.markerEnabled;
        RefreshTransitionMarkers();
        _campaignDirty = true;
        DiceUI.Instance?.ShowToolNotice(transition.markerEnabled
            ? "Маркер перехода включён на карте."
            : "Маркер перехода скрыт на карте.");
    });

    public static void TransferHeroesToScene(string targetSceneId, string[] tokenIds)
        => TransferTokensToScene(targetSceneId, tokenIds);

    public static void TransferTokensToScene(string targetSceneId, string[] tokenIds) => Safely(() =>
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        if (!HasCampaign) throw new InvalidOperationException("Сначала загрузите или сохраните сессию.");
        CaptureCampaign(_campaign.title);
        var source = Array.Find(_campaign.scenes, item => item != null && item.sceneId == _campaign.activeSceneId);
        var target = Array.Find(_campaign.scenes, item => item != null && item.sceneId == targetSceneId);
        if (source == null || target == null || source.sceneId == target.sceneId)
            throw new InvalidOperationException("Выберите другую существующую сцену.");

        var selectedIds = new HashSet<string>(tokenIds ?? Array.Empty<string>());
        if (selectedIds.Count == 0) throw new InvalidOperationException("Выберите хотя бы один токен.");
        var moving = Array.FindAll(source.scene.tokens,
            token => token != null && selectedIds.Contains(token.id));
        if (moving.Length == 0) throw new InvalidOperationException("В активной сцене нет выбранных токенов для переноса.");
        if (moving.Length != selectedIds.Count)
            throw new InvalidOperationException("Выбран несуществующий токен или токен не из активной сцены.");
        var backup = JsonUtility.FromJson<CampaignDefinition>(JsonUtility.ToJson(_campaign));
        bool previousDirty = _campaignDirty;
        var masterById = new Dictionary<string, MasterTokenData>();
        foreach (var master in source.scene.masterData.tokens) masterById[master.tokenId] = master;
        var movingIds = new HashSet<string>();
        var newIds = new Dictionary<string, string>();
        foreach (var token in moving)
        {
            string oldId = token.id;
            movingIds.Add(oldId);
            string id = oldId;
            if (Array.Exists(target.scene.tokens, existing => existing != null && existing.id == id))
                id = Guid.NewGuid().ToString("N");
            newIds[oldId] = id;
        }

        var targetTokens = new List<SceneToken>(target.scene.tokens);
        var targetMasters = new List<MasterTokenData>(target.scene.masterData.tokens);
        var occupiedCells = new HashSet<(int X, int Y)>();
        foreach (var existing in target.scene.tokens)
            if (existing != null) occupiedCells.Add((Mathf.FloorToInt(existing.position.x), Mathf.FloorToInt(existing.position.z)));
        foreach (var obstacle in target.scene.geometry.obstacles)
            if (obstacle != null) occupiedCells.Add((obstacle.x, obstacle.y));
        for (int i = 0; i < moving.Length; i++)
        {
            var copy = JsonUtility.FromJson<SceneToken>(JsonUtility.ToJson(moving[i]));
            string oldId = copy.id;
            copy.id = newIds[oldId];
            int cells = target.scene.gridWidth * target.scene.gridHeight;
            int cell = -1;
            for (int attempt = 0; attempt < cells; attempt++)
            {
                int x = (target.scene.gridWidth / 2 + attempt % target.scene.gridWidth) % target.scene.gridWidth;
                int y = (target.scene.gridHeight / 2 + attempt / target.scene.gridWidth) % target.scene.gridHeight;
                if (occupiedCells.Contains((x, y))) continue;
                cell = y * target.scene.gridWidth + x;
                occupiedCells.Add((x, y));
                break;
            }
            if (cell < 0) throw new InvalidOperationException("На карте назначения не осталось свободных клеток для токенов.");
            copy.position = new Vector3(cell % target.scene.gridWidth + 0.5f, 0,
                cell / target.scene.gridWidth + 0.5f);
            targetTokens.Add(copy);
            if (masterById.TryGetValue(oldId, out MasterTokenData master))
            {
                var masterCopy = JsonUtility.FromJson<MasterTokenData>(JsonUtility.ToJson(master));
                masterCopy.tokenId = copy.id;
                targetMasters.Add(masterCopy);
            }
        }
        source.scene.tokens = Array.FindAll(source.scene.tokens, token => token == null || !movingIds.Contains(token.id));
        source.scene.masterData.tokens = Array.FindAll(source.scene.masterData.tokens,
            master => master == null || !movingIds.Contains(master.tokenId));
        target.scene.tokens = targetTokens.ToArray();
        target.scene.masterData.tokens = targetMasters.ToArray();
        source.scene.includesPlayers = true;
        target.scene.includesPlayers = true;

        var movedParticipants = Array.FindAll(source.battle.participants,
            participant => participant != null && !string.IsNullOrEmpty(participant.tokenId)
                && movingIds.Contains(participant.tokenId));
        source.battle.participants = Array.FindAll(source.battle.participants,
            participant => participant == null || !movingIds.Contains(participant.tokenId));
        var targetParticipants = new List<BattleParticipant>(target.battle.participants);
        foreach (var participant in movedParticipants)
        {
            var copy = JsonUtility.FromJson<BattleParticipant>(JsonUtility.ToJson(participant));
            copy.id = Guid.NewGuid().ToString("N");
            copy.tokenId = newIds[participant.tokenId];
            targetParticipants.Add(copy);
        }
        target.battle.participants = targetParticipants.ToArray();
        bool activeParticipantMoved = false;
        foreach (var participant in movedParticipants)
            if (participant.id == source.battle.activeParticipantId) { activeParticipantMoved = true; break; }
        if (activeParticipantMoved) source.battle.activeParticipantId = string.Empty;
        source.battle.round = NormalizeRound(source.battle);
        target.battle.round = NormalizeRound(target.battle);
        try { SceneValidation.Validate(_campaign); }
        catch { _campaign = backup; _campaignDirty = previousDirty; throw; }
        _campaignDirty = true;
        try { SwitchToScene(target.sceneId); }
        catch { _campaign = backup; _campaignDirty = previousDirty; throw; }
        DiceUI.Instance?.ShowToolNotice($"В сцену «{target.title}» перенесено токенов: {moving.Length}.");
    });

    private static int NormalizeRound(SceneBattleState battle)
    {
        if (battle?.participants == null || battle.participants.Length == 0) return 1;
        Array.Sort(battle.participants, (left, right) =>
        {
            if (left == null) return right == null ? 0 : 1;
            if (right == null) return -1;
            int initiative = right.initiative.CompareTo(left.initiative);
            return initiative != 0 ? initiative : string.CompareOrdinal(left.id, right.id);
        });
        return Mathf.Max(1, battle.round);
    }

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
        string previousSceneId = _campaign.activeSceneId;
        _campaign.activeSceneId = sceneId;
        try { ApplyScene(target.scene, target.battle); }
        catch { _campaign.activeSceneId = previousSceneId; throw; }
        _campaignDirty = true;
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
        _campaignDirty = true;
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
        bool previousDirty = _campaignDirty;
        var remaining = new CampaignScene[_campaign.scenes.Length - 1];
        for (int source = 0, destination = 0; source < _campaign.scenes.Length; source++)
            if (source != index) remaining[destination++] = _campaign.scenes[source];
        foreach (var scene in remaining)
        {
            var transitions = scene.scene.transitions ?? Array.Empty<SceneTransition>();
            scene.scene.transitions = Array.FindAll(transitions,
                transition => transition != null && transition.targetSceneId != _campaign.activeSceneId);
        }
        _campaign.scenes = remaining;
        try { SwitchToScene(nextSceneId); }
        catch { _campaign = previousCampaign; _campaignDirty = previousDirty; throw; }
        _campaignDirty = true;
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
        try
        {
            var current = Capture(true);
            var battle = InitiativeTracker.Instance?.CaptureBattleState();
            if (_campaign != null)
            {
                int index = Array.FindIndex(_campaign.scenes, item => item.sceneId == current.sceneId);
                if (index >= 0 && (JsonUtility.ToJson(_campaign.scenes[index].scene) != JsonUtility.ToJson(current)
                    || battle != null && JsonUtility.ToJson(_campaign.scenes[index].battle) != JsonUtility.ToJson(battle)))
                    _campaignDirty = true;
            }
            else if (battle != null && (battle.participants.Length > 0 || battle.round != 1
                || !string.IsNullOrEmpty(battle.activeParticipantId))) return true;
            return _campaignDirty || JsonUtility.ToJson(current) != _lastSavedState;
        }
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
            mapAssetId = _currentMapAssetId,
            gridWidth = grid.Width, gridHeight = grid.Height, cellSize = grid.CellSize,
            gridPosition = grid.GridOrigin, gridRotation = grid.GridRotation.eulerAngles,
            mapPosition = map.transform.position, mapRotation = map.transform.eulerAngles, mapScale = map.CurrentScale,
            geometry = SceneEditor.Instance.Model.Snapshot(), mapImage = Encode(mapBytes), includesPlayers = true,
            fog = FogManager.Instance?.Capture(history) ?? new SavedFog()
        };
        var savedActive = Array.Find(_campaign?.scenes ?? Array.Empty<CampaignScene>(),
            item => item != null && item.sceneId == _currentSceneId);
        if (savedActive?.scene?.transitions != null)
            scene.transitions = (SceneTransition[])savedActive.scene.transitions.Clone();
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
        var scene = Capture(history ?? FogManager.Instance?.SaveWithHistory ?? true);
        scene.mapAssetId = null; // Standalone scene files contain their own image bytes.
        string json = JsonUtility.ToJson(scene, true);
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
        scene.mapAssetId = null;
        _campaign = null;
        _campaignDirty = false;
        _currentMapAssetId = null;
        foreach (var marker in UnityEngine.Object.FindObjectsByType<SceneTransitionMarker>(FindObjectsInactive.Include))
            if (marker != null) marker.SetMarkerEnabled(false);
        ApplyScene(scene, null);
    }

    public static void LoadCampaign(string path)
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        CampaignDefinition campaign = CampaignFileStore.LoadCompatible(path);
        ValidateCampaignImages(campaign);
        CampaignScene active = Array.Find(campaign.scenes, item => item.sceneId == campaign.activeSceneId);
        if (active == null) throw new FormatException("Активная сцена не найдена в сессии.");
        var previousCampaign = _campaign;
        bool previousDirty = _campaignDirty;
        _campaign = campaign;
        try { ApplyScene(active.scene, active.battle); }
        catch { _campaign = previousCampaign; _campaignDirty = previousDirty; throw; }
        _campaignDirty = false;
        DiceUI.Instance?.ShowToolNotice($"Сессия «{campaign.title}» загружена · сцена «{active.title}».");
    }

    private static void ValidateCampaignImages(CampaignDefinition campaign)
    {
        var checkedMaps = new HashSet<string>(StringComparer.Ordinal);
        var checkedPortraits = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in campaign.mapAssets)
            if (checkedMaps.Add(asset.imageData)) Decode(asset.imageData, MapSync.MaxMapBytes);
        foreach (var entry in campaign.scenes)
        {
            if (checkedMaps.Add(entry.scene.mapImage)) Decode(entry.scene.mapImage, MapSync.MaxMapBytes);
            foreach (var token in entry.scene.tokens)
                if (checkedPortraits.Add(token.portrait)) Decode(token.portrait, TokenImageSync.MaxPortraitBytes);
        }
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
        bool applyCompleted = false;
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
            RefreshTransitionMarkers();
            foreach (var data in scene.tokens)
            {
                masterTokens.TryGetValue(data.id, out MasterTokenData masterData);
                var token = TokenManager.Instance.RestoreSceneToken(data, grid, masterData);
                if (token == null) throw new InvalidOperationException("Не удалось восстановить токен.");
                byte[] portrait = portraits[data.id];
                if (portrait != null && !TokenImageSync.BroadcastImage(token.NetworkObjectId, portrait))
                    throw new InvalidOperationException("Не удалось восстановить портрет токена.");
            }
            TokenController.RebuildCellOccupancy();
            InitiativeTracker.Instance?.RestoreBattleState(battle ?? new SceneBattleState());
            FogManager.Instance?.PrepareForSceneTransfer();
            applyCompleted = true;
        }
        finally
        {
            if (!curtain && HostSceneCurtain.IsCurtainDown)
            {
                if (applyCompleted) SceneEditor.Instance.RevealAfterMapTransfer();
                else DiceUI.Instance?.ShowToolNotice("Сцену не удалось применить полностью. Занавес оставлен закрытым.");
            }
        }
        _currentSceneId = scene.sceneId;
        _currentMapAssetId = scene.mapAssetId;
        _lastSavedState = JsonUtility.ToJson(Capture(true));
    }

    private static void Safely(Action action)
    {
        try { action(); }
        catch (Exception ex) { DiceUI.Instance?.ShowToolNotice("Сцена: " + ex.Message); Debug.LogWarning("[Scene] " + ex.Message); }
    }
    public static void LoadAutosave()
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        string path = Path.Combine(Application.persistentDataPath, "autosave-campaign.json");
        bool campaign = File.Exists(path);
        if (!campaign) path = Path.Combine(Application.persistentDataPath, "autosave-scene.json");
        if (!File.Exists(path)) { DiceUI.Instance?.ShowToolNotice("Автосохранения пока нет."); return; }
        DiceUI.Instance?.ConfirmAction("Восстановить автосохранение?",
            campaign ? "Сессия со всеми сценами, библиотеками и боем будет заменена последней сохранённой копией."
                : "Текущая сцена будет заменена последней сохранённой копией.",
            () => Safely(() => { if (campaign) LoadCampaign(path); else Load(path); }));
    }

    public static void SaveAutosave()
    {
        if (NetworkManager.Singleton?.IsHost != true) return;
        CampaignFileStore.Save(Path.Combine(Application.persistentDataPath, "autosave-campaign.json"), CaptureCampaign());
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

    public static void ExportReferenceLibraryDialog()
    {
#if UNITY_EDITOR
        string path = EditorUtility.SaveFilePanel("Экспорт справочника", "", "references.json", "json");
        if (!string.IsNullOrEmpty(path)) Safely(() => ExportReferenceLibrary(path));
#else
        SimpleFileBrowser.FileBrowser.ShowSaveDialog(paths =>
        {
            if (paths.Length > 0) Safely(() => ExportReferenceLibrary(paths[0]));
        }, null, SimpleFileBrowser.FileBrowser.PickMode.Files, false, null,
            "references.json", "Экспорт справочника", "Экспортировать");
#endif
    }

    public static void ImportReferenceLibraryDialog()
    {
        void Confirm(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            DiceUI.Instance?.ConfirmAction("Заменить справочник?",
                "Записи текущего справочника будут заменены содержимым выбранного файла.",
                () => Safely(() => ImportReferenceLibrary(path)));
        }
#if UNITY_EDITOR
        Confirm(EditorUtility.OpenFilePanel("Импорт справочника", "", "json"));
#else
        SimpleFileBrowser.FileBrowser.ShowLoadDialog(paths =>
        {
            if (paths.Length > 0) Confirm(paths[0]);
        }, null, SimpleFileBrowser.FileBrowser.PickMode.Files, false, null, null,
            "Импорт справочника", "Импортировать");
#endif
    }
}
