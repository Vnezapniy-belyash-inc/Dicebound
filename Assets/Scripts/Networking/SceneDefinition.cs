using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable] public sealed class SceneEdge
{
    public string id;
    public int x, y;
    public bool vertical;
    public bool door, open;
}
[Serializable] public sealed class SceneObstacle
{
    public string id;
    public int x, y;
    public bool round;
    public float diameter = 0.5f;
}
[Serializable] public sealed class SceneGeometry
{
    public SceneEdge[] edges = Array.Empty<SceneEdge>();
    public SceneObstacle[] obstacles = Array.Empty<SceneObstacle>();
    public bool revealPaused = true;
}
[Serializable] public sealed class SceneToken
{
    public string id, name, nameBase, portrait;
    public Vector3 position, scale;
    public bool hidden;
    public bool hero, everyoneCanMove, unassigned;
    public string ownerNickname;
    public int visionFeet;
}
[Serializable] public sealed class SavedFog
{
    public bool enabled = true;
    public int width, height;
    public string explored = "", revealed = "", hidden = "";
    public static bool[] Decode(string value, int count)
    {
        if (string.IsNullOrEmpty(value)) return new bool[count];
        int bytes = (count + 7) / 8;
        if (value.Length != ((bytes + 2) / 3) * 4) throw new FormatException("Некорректный размер истории тумана.");
        return FogVisibility.Unpack(Convert.FromBase64String(value), count);
    }
}
[Serializable] public sealed class SceneDefinition
{
    // Version 2 adds a stable scene ID. Version 1 files are upgraded by
    // SceneSaveMigration before validation or application.
    public int version = 2;
    public string sceneId = "";
    public string title = "Сцена";
    public string mapAssetId;
    public int gridWidth, gridHeight;
    public float cellSize;
    public Vector3 gridPosition, gridRotation, mapPosition, mapRotation;
    public float mapScale = 1;
    public string mapImage;
    public SceneGeometry geometry = new();
    public SceneToken[] tokens = Array.Empty<SceneToken>();
    public SceneMasterData masterData = new();
    public SavedFog fog = new();
    public bool includesPlayers;
}

/// <summary>Master-only, scene-local token data. Never put this in replicated token state.</summary>
[Serializable] public sealed class MasterTokenData
{
    public string tokenId;
    public int currentHp;
    public int maxHp;
    public int armorClass = 10;
    public bool hideHp = true;
    public bool hideConditions = true;
    public string[] conditionIds = Array.Empty<string>();
    public string statBlockId;
}

/// <summary>Master-authored data which must not be replicated to players.</summary>
[Serializable] public sealed class SceneMasterData
{
    public MasterTokenData[] tokens = Array.Empty<MasterTokenData>();
}

/// <summary>A campaign scene and all data owned by that scene.</summary>
[Serializable] public sealed class CampaignScene
{
    public string sceneId;
    public string title;
    public SceneDefinition scene = new();
    public SceneBattleState battle = new();
}

[Serializable] public sealed class BattleParticipant
{
    public string id;
    public string name;
    public string colorHex;
    public string tokenId;
    public ulong playerId = ulong.MaxValue;
    public int initiative;
    public int hitPoints;
    public bool hasHitPoints;
}

[Serializable] public sealed class SceneBattleState
{
    public int round = 1;
    public string activeParticipantId;
    public BattleParticipant[] participants = Array.Empty<BattleParticipant>();
}

[Serializable] public sealed class StatBlockAction
{
    public string name;
    public string description;
}

[Serializable] public sealed class StatBlockDefinition
{
    public string id;
    public string name;
    public string size;
    public string creatureType;
    public string alignment;
    public int armorClass = 10;
    public int hitPoints;
    public string speed;
    public int strength = 10, dexterity = 10, constitution = 10;
    public int intelligence = 10, wisdom = 10, charisma = 10;
    public string challengeRating;
    public string description;
    public StatBlockAction[] actions = Array.Empty<StatBlockAction>();
    public int publicFieldsMask;
}

public static class StatBlockPublicFields
{
    public const int Name = 1 << 0;
    public const int Size = 1 << 1;
    public const int CreatureType = 1 << 2;
    public const int Alignment = 1 << 3;
    public const int ArmorClass = 1 << 4;
    public const int HitPoints = 1 << 5;
    public const int Speed = 1 << 6;
    public const int Abilities = 1 << 7;
    public const int ChallengeRating = 1 << 8;
    public const int Description = 1 << 9;
    public const int Actions = 1 << 10;
    public const int All = (1 << 11) - 1;
}

[Serializable] public sealed class PublicStatBlockView
{
    public int visibleFields;
    public bool truncated;
    public string name;
    public string size;
    public string creatureType;
    public string alignment;
    public int armorClass;
    public int hitPoints;
    public string speed;
    public int strength, dexterity, constitution, intelligence, wisdom, charisma;
    public string challengeRating;
    public string description;
    public StatBlockPublicAction[] actions = Array.Empty<StatBlockPublicAction>();
}

[Serializable] public sealed class StatBlockPublicAction
{
    public string name;
    public string description;
}

[Serializable] public sealed class CampaignMapAsset
{
    public string id;
    public string name;
    public string imageData;
}

[Serializable] public sealed class ReferenceEntry
{
    public string id;
    public string title;
    public string category;
    public string body;
    public string[] tags = Array.Empty<string>();
    public bool masterOnly;
    public bool pinned;
}

[Serializable] public sealed class ReferenceLibraryFile
{
    public int version = 1;
    public ReferenceEntry[] entries = Array.Empty<ReferenceEntry>();
}

/// <summary>Portable campaign container; the active encounter can be stored independently.</summary>
[Serializable] public sealed class CampaignDefinition
{
    public int version = 1;
    public string campaignId;
    public string title = "Кампания";
    public string activeSceneId;
    public CampaignScene[] scenes = Array.Empty<CampaignScene>();
    public CampaignMapAsset[] mapAssets = Array.Empty<CampaignMapAsset>();
    public StatBlockDefinition[] statBlocks = Array.Empty<StatBlockDefinition>();
    public ReferenceEntry[] referenceEntries = Array.Empty<ReferenceEntry>();
}

/// <summary>Converts existing single-scene v1 files without changing their authored state.</summary>
public static class SceneSaveMigration
{
    public const int CurrentSceneVersion = 2;

    public static SceneDefinition UpgradeScene(SceneDefinition scene)
    {
        if (scene == null) throw new FormatException("Файл сцены пуст.");
        if (scene.version == 1)
        {
            scene.version = CurrentSceneVersion;
            scene.sceneId = Guid.NewGuid().ToString("N");
        }
        else if (scene.version == CurrentSceneVersion)
        {
            if (string.IsNullOrWhiteSpace(scene.sceneId))
                scene.sceneId = Guid.NewGuid().ToString("N");
        }
        else throw new FormatException("Неподдерживаемая версия сцены.");

        scene.masterData ??= new SceneMasterData();

        return scene;
    }

    public static CampaignDefinition UpgradeSingleScene(SceneDefinition scene)
        => UpgradeSingleScene(scene, scene?.title);

    public static CampaignDefinition UpgradeSingleScene(SceneDefinition scene, string campaignTitle)
    {
        scene = UpgradeScene(scene);
        return new CampaignDefinition
        {
            campaignId = Guid.NewGuid().ToString("N"),
            title = string.IsNullOrWhiteSpace(campaignTitle) ? "Кампания" : campaignTitle,
            activeSceneId = scene.sceneId,
            scenes = new[]
            {
                new CampaignScene
                {
                    sceneId = scene.sceneId,
                    title = string.IsNullOrWhiteSpace(scene.title) ? "Сцена" : scene.title,
                    scene = scene,
                    battle = Unity.Netcode.NetworkManager.Singleton?.IsHost == true
                        && InitiativeTracker.Instance != null
                        ? InitiativeTracker.Instance.CaptureBattleState() : new SceneBattleState()
                }
            }
        };
    }
}

/// <summary>Validation completes before replacing the current table. No file or network IDs are trusted.</summary>
public static class SceneValidation
{
    public const int MaxFileBytes = 96 * 1024 * 1024;
    public const int MaxEdges = 3000;
    public const int MaxObstacles = 512;
    public const int MaxTokens = 256;
    public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
    private static void Require(bool valid, string message) { if (!valid) throw new FormatException(message); }
    public static void Geometry(SceneGeometry geometry, int width, int height)
    {
        Require(geometry != null && geometry.edges != null && geometry.obstacles != null, "Нет данных разметки.");
        Require(geometry.edges.Length <= MaxEdges && geometry.obstacles.Length <= MaxObstacles, "Слишком много объектов разметки.");
        var ids = new HashSet<string>();
        var edges = new HashSet<(int, int, bool)>();
        foreach (var edge in geometry.edges)
        {
            Require(edge != null && !string.IsNullOrWhiteSpace(edge.id) && edge.id.Length <= 64 && ids.Add(edge.id), "Некорректный ID стены.");
            Require(edge.x >= 0 && edge.y >= 0 && edge.x <= (edge.vertical ? width : width - 1)
                && edge.y <= (edge.vertical ? height - 1 : height) && edges.Add((edge.x, edge.y, edge.vertical)), "Некорректное или повторное ребро.");
            Require(edge.door || !edge.open, "Открытой может быть только дверь.");
        }
        var cells = new HashSet<(int, int)>();
        foreach (var obstacle in geometry.obstacles)
        {
            Require(obstacle != null && !string.IsNullOrWhiteSpace(obstacle.id) && obstacle.id.Length <= 64 && ids.Add(obstacle.id), "Некорректный ID препятствия.");
            Require(obstacle.x >= 0 && obstacle.x < width && obstacle.y >= 0 && obstacle.y < height
                && cells.Add((obstacle.x, obstacle.y)), "Некорректная или повторная клетка препятствия.");
            Require(Finite(obstacle.diameter) && obstacle.diameter >= 0.1f && obstacle.diameter <= 1, "Диаметр колонны должен быть 0.1–1 клетки.");
        }
    }
    public static void Validate(SceneDefinition scene)
    {
        Require(scene != null && scene.version == SceneSaveMigration.CurrentSceneVersion,
            "Неподдерживаемая версия сцены.");
        Require(!string.IsNullOrWhiteSpace(scene.sceneId) && scene.sceneId.Length <= 64,
            "Некорректный ID сцены.");
        Require(!string.IsNullOrEmpty(scene.mapImage), "Сначала загрузите изображение карты.");
        Require(scene.gridWidth > 0 && scene.gridWidth <= 256 && scene.gridHeight > 0 && scene.gridHeight <= 256,
            "Размер сетки должен быть 1–256 клеток.");
        Require(Finite(scene.cellSize) && scene.cellSize >= 0.1f && scene.cellSize <= 10, "Некорректный размер клетки.");
        Require(Finite(scene.gridPosition) && Finite(scene.gridRotation) && Finite(scene.mapPosition) && Finite(scene.mapRotation)
            && scene.gridPosition.sqrMagnitude < 100000000 && scene.mapPosition.sqrMagnitude < 100000000,
            "Некорректное преобразование карты.");
        Require(Finite(scene.mapScale) && scene.mapScale >= 0.1f && scene.mapScale <= 5, "Некорректный масштаб карты.");
        Geometry(scene.geometry, scene.gridWidth, scene.gridHeight);
        if (scene.fog != null)
        {
            bool history = !string.IsNullOrEmpty(scene.fog.explored) || !string.IsNullOrEmpty(scene.fog.revealed) || !string.IsNullOrEmpty(scene.fog.hidden);
            if (history) Require(scene.fog.width == scene.gridWidth && scene.fog.height == scene.gridHeight, "История не соответствует размеру сетки.");
            int count = scene.gridWidth * scene.gridHeight * 16;
            SavedFog.Decode(scene.fog.explored, count); SavedFog.Decode(scene.fog.revealed, count); SavedFog.Decode(scene.fog.hidden, count);
        }
        Require(scene.tokens != null && scene.tokens.Length <= MaxTokens, "Слишком много токенов.");
        var ids = new HashSet<string>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in scene.tokens)
        {
            Require(token != null && !string.IsNullOrWhiteSpace(token.id) && token.id.Length <= 64 && ids.Add(token.id), "Некорректный ID токена.");
            Require(!string.IsNullOrWhiteSpace(token.name) && System.Text.Encoding.UTF8.GetByteCount(token.name) <= 125 && names.Add(token.name), "Повторное или слишком длинное имя токена.");
            Require(Finite(token.position) && token.position.x >= 0 && token.position.x < scene.gridWidth
                && token.position.z >= 0 && token.position.z < scene.gridHeight && Mathf.Abs(token.position.y) <= 100,
                "Токен за пределами сетки.");
            Require(Finite(token.scale) && token.scale.x > 0 && token.scale.x <= 10 && token.scale.y > 0
                && token.scale.y <= 10 && token.scale.z > 0 && token.scale.z <= 10, "Некорректный размер токена.");
            Require(token.visionFeet >= 0 && token.visionFeet <= TokenController.MaxVisionFeet, "Некорректное зрение токена.");
            Require(token.ownerNickname == null || token.ownerNickname.Length <= 64, "Слишком длинное имя владельца.");
        }
        ValidateMasterData(scene.masterData, scene.tokens);
    }

    public static void Validate(CampaignDefinition campaign)
    {
        Require(campaign != null && campaign.version == 1, "Неподдерживаемая версия кампании.");
        Require(!string.IsNullOrWhiteSpace(campaign.campaignId) && campaign.campaignId.Length <= 64,
            "Некорректный ID кампании.");
        Require(!string.IsNullOrWhiteSpace(campaign.title) && campaign.title.Length <= 256,
            "Некорректное название кампании.");
        Require(campaign.scenes != null && campaign.scenes.Length > 0 && campaign.scenes.Length <= 128,
            "В кампании должно быть от 1 до 128 сцен.");
        var ids = new HashSet<string>();
        bool activeFound = false;
        foreach (var entry in campaign.scenes)
        {
            Require(entry != null && !string.IsNullOrWhiteSpace(entry.sceneId)
                && entry.sceneId.Length <= 64 && ids.Add(entry.sceneId), "Некорректный или повторный ID сцены.");
            Require(entry.scene != null && entry.scene.sceneId == entry.sceneId,
                "ID контейнера и данных сцены не совпадают.");
            Validate(entry.scene);
            ValidateBattle(entry.battle, entry.scene);
            if (entry.sceneId == campaign.activeSceneId) activeFound = true;
        }
        Require(activeFound, "Активная сцена отсутствует в кампании.");
        var mapIds = new HashSet<string>();
        if (campaign.mapAssets == null) campaign.mapAssets = Array.Empty<CampaignMapAsset>();
        Require(campaign.mapAssets.Length <= 256, "Слишком много карт в каталоге кампании.");
        foreach (var map in campaign.mapAssets)
            Require(map != null && !string.IsNullOrWhiteSpace(map.id) && map.id.Length <= 64
                && mapIds.Add(map.id) && !string.IsNullOrWhiteSpace(map.name) && map.name.Length <= 128
                && !string.IsNullOrWhiteSpace(map.imageData), "Некорректная или повторная карта каталога.");
        foreach (var entry in campaign.scenes)
            Require(string.IsNullOrEmpty(entry.scene.mapAssetId) || mapIds.Contains(entry.scene.mapAssetId),
                "Сцена ссылается на отсутствующую карту каталога.");
        Require(campaign.statBlocks != null && campaign.statBlocks.Length <= 4096,
            "Некорректный каталог статблоков.");
        var statBlockIds = new HashSet<string>();
        foreach (var statBlock in campaign.statBlocks)
        {
            Require(statBlock != null && !string.IsNullOrWhiteSpace(statBlock.id)
                && statBlock.id.Length <= 64 && statBlockIds.Add(statBlock.id)
                && !string.IsNullOrWhiteSpace(statBlock.name) && statBlock.name.Length <= 128,
                "Некорректная или повторная запись статблока.");
            Require(statBlock.armorClass >= 0 && statBlock.armorClass <= 999
                && statBlock.hitPoints >= 0 && statBlock.hitPoints <= 999999
                && statBlock.publicFieldsMask >= 0
                && (statBlock.publicFieldsMask & ~StatBlockPublicFields.All) == 0,
                "Некорректные параметры статблока.");
            Require(statBlock.strength >= 1 && statBlock.strength <= 40
                && statBlock.dexterity >= 1 && statBlock.dexterity <= 40
                && statBlock.constitution >= 1 && statBlock.constitution <= 40
                && statBlock.intelligence >= 1 && statBlock.intelligence <= 40
                && statBlock.wisdom >= 1 && statBlock.wisdom <= 40
                && statBlock.charisma >= 1 && statBlock.charisma <= 40,
                "Некорректные характеристики статблока.");
            Require(statBlock.actions != null && statBlock.actions.Length <= 256,
                "Слишком много действий в статблоке.");
            Require(statBlock.size != null && statBlock.size.Length <= 64
                && statBlock.creatureType != null && statBlock.creatureType.Length <= 128
                && statBlock.alignment != null && statBlock.alignment.Length <= 128
                && statBlock.speed != null && statBlock.speed.Length <= 256
                && statBlock.challengeRating != null && statBlock.challengeRating.Length <= 64
                && statBlock.description != null && statBlock.description.Length <= 65536,
                "Слишком большой или некорректный статблок.");
            foreach (var action in statBlock.actions)
                Require(action != null && !string.IsNullOrWhiteSpace(action.name)
                    && action.name.Length <= 128 && action.description != null
                    && action.description.Length <= 16384, "Некорректное действие статблока.");
        }
        foreach (var entry in campaign.scenes)
            foreach (var tokenData in entry.scene.masterData.tokens)
                Require(string.IsNullOrEmpty(tokenData.statBlockId)
                    || statBlockIds.Contains(tokenData.statBlockId), "Токен ссылается на отсутствующий статблок.");
        Require(campaign.referenceEntries != null && campaign.referenceEntries.Length <= 10000,
            "Некорректный справочник кампании.");
        var referenceIds = new HashSet<string>();
        foreach (var reference in campaign.referenceEntries)
        {
            Require(reference != null && !string.IsNullOrWhiteSpace(reference.id)
                && reference.id.Length <= 64 && referenceIds.Add(reference.id)
                && !string.IsNullOrWhiteSpace(reference.title) && reference.title.Length <= 256,
                "Некорректная или повторная запись справочника.");
            Require(reference.body != null && reference.body.Length <= 65536
                && reference.tags != null && reference.tags.Length <= 64,
                "Некорректное содержимое записи справочника.");
            Require(reference.category != null && reference.category.Length <= 128,
                "Некорректная категория справочника.");
            foreach (string tag in reference.tags)
                Require(!string.IsNullOrWhiteSpace(tag) && tag.Length <= 64,
                    "Некорректная метка справочника.");
        }
    }

    private static void ValidateMasterData(SceneMasterData masterData, SceneToken[] sceneTokens)
    {
        Require(masterData != null && masterData.tokens != null && masterData.tokens.Length <= MaxTokens,
            "Некорректные мастерские данные сцены.");
        var ids = new HashSet<string>();
        foreach (var tokenData in masterData.tokens)
        {
            Require(tokenData != null && !string.IsNullOrWhiteSpace(tokenData.tokenId)
                && ids.Add(tokenData.tokenId), "Некорректная или повторная мастерская запись токена.");
            Require(Array.Exists(sceneTokens, token => token.id == tokenData.tokenId),
                "Мастерские данные ссылаются на отсутствующий токен.");
            Require(tokenData.currentHp >= 0 && tokenData.currentHp <= 999999
                && tokenData.maxHp >= 0 && tokenData.maxHp <= 999999
                && tokenData.currentHp <= tokenData.maxHp, "Некорректные HP токена.");
            Require(tokenData.armorClass >= 0 && tokenData.armorClass <= 999,
                "Некорректный КД токена.");
            Require(tokenData.conditionIds != null && tokenData.conditionIds.Length <= 64,
                "Слишком много состояний у токена.");
            foreach (string conditionId in tokenData.conditionIds)
                Require(!string.IsNullOrWhiteSpace(conditionId) && conditionId.Length <= 64
                    && !conditionId.Contains("|"),
                    "Некорректный ID состояния.");
            Require(System.Text.Encoding.UTF8.GetByteCount(string.Join("|", tokenData.conditionIds))
                <= Unity.Collections.FixedString4096Bytes.UTF8MaxLengthInBytes,
                "Слишком длинный список состояний.");
            Require(string.IsNullOrEmpty(tokenData.statBlockId) || tokenData.statBlockId.Length <= 64,
                "Некорректный ID статблока.");
        }
    }

    private static void ValidateBattle(SceneBattleState battle, SceneDefinition scene)
    {
        Require(battle != null && battle.round >= 1 && battle.round <= 100000
            && battle.participants != null && battle.participants.Length <= MaxTokens + 64,
            "Некорректное состояние боя.");
        var ids = new HashSet<string>();
        bool activeFound = string.IsNullOrEmpty(battle.activeParticipantId);
        foreach (var participant in battle.participants)
        {
            Require(participant != null && !string.IsNullOrWhiteSpace(participant.id)
                && participant.id.Length <= 64 && ids.Add(participant.id),
                "Некорректная или повторная запись инициативы.");
            Require(participant.initiative >= -999 && participant.initiative <= 999,
                "Некорректное значение инициативы.");
            Require(participant.name == null || participant.name.Length <= 256,
                "Слишком длинное имя участника инициативы.");
            Require(participant.colorHex == null || participant.colorHex.Length <= 16,
                "Некорректный цвет участника инициативы.");
            Require(!participant.hasHitPoints || participant.hitPoints >= 0 && participant.hitPoints <= 99999,
                "Некорректное значение HP участника инициативы.");
            if (!string.IsNullOrEmpty(participant.tokenId))
                Require(Array.Exists(scene.tokens, token => token.id == participant.tokenId),
                    "Инициатива ссылается на отсутствующий токен.");
            else Require(participant.playerId != ulong.MaxValue || !string.IsNullOrWhiteSpace(participant.name),
                "Для участника инициативы без токена укажите имя или игрока.");
            if (participant.id == battle.activeParticipantId) activeFound = true;
        }
        Require(activeFound, "Активный участник инициативы отсутствует в списке.");
    }
}

/// <summary>One edge has exactly one wall or door; one cell has at most one solid obstacle.</summary>
public sealed class SceneGeometryModel
{
    private readonly Dictionary<(int, int, bool), SceneEdge> _edges = new();
    private readonly Dictionary<(int, int), SceneObstacle> _obstacles = new();
    public bool RevealPaused { get; set; } = true;
    public bool SetEdge(int x, int y, bool vertical, bool door, bool erase, bool toggle = false)
    {
        var key = (x, y, vertical);
        if (erase) return _edges.Remove(key);
        if (_edges.TryGetValue(key, out var edge))
        {
            if (toggle) { if (!edge.door) return false; edge.open = !edge.open; return true; }
            if (edge.door == door) return false;
            edge.door = door; edge.open = false; return true;
        }
        if (toggle || _edges.Count >= SceneValidation.MaxEdges) return false;
        _edges[key] = new SceneEdge { id = Guid.NewGuid().ToString("N"), x = x, y = y, vertical = vertical, door = door };
        return true;
    }
    public bool SetObstacle(int x, int y, bool round, float diameter, bool erase)
    {
        var key = (x, y);
        if (erase) return _obstacles.Remove(key);
        if (_obstacles.TryGetValue(key, out var old) && old.round == round && old.diameter == diameter) return false;
        if (!_obstacles.ContainsKey(key) && _obstacles.Count >= SceneValidation.MaxObstacles) return false;
        _obstacles[key] = new SceneObstacle { id = old?.id ?? Guid.NewGuid().ToString("N"), x = x, y = y, round = round, diameter = diameter };
        return true;
    }
    public SceneGeometry Snapshot() => new SceneGeometry { edges = new List<SceneEdge>(_edges.Values).ToArray(),
        obstacles = new List<SceneObstacle>(_obstacles.Values).ToArray(), revealPaused = RevealPaused };
    public void Replace(SceneGeometry geometry)
    {
        _edges.Clear(); _obstacles.Clear();
        foreach (var edge in geometry.edges) _edges.Add((edge.x, edge.y, edge.vertical), edge);
        foreach (var obstacle in geometry.obstacles) _obstacles.Add((obstacle.x, obstacle.y), obstacle);
        RevealPaused = geometry.revealPaused;
    }
}
