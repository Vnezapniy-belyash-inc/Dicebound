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
            gridWidth = grid.Width, gridHeight = grid.Height, cellSize = grid.CellSize,
            gridPosition = grid.GridOrigin, gridRotation = grid.GridRotation.eulerAngles,
            mapPosition = map.transform.position, mapRotation = map.transform.eulerAngles, mapScale = map.CurrentScale,
            geometry = SceneEditor.Instance.Model.Snapshot(), mapImage = Encode(mapBytes), includesPlayers = true,
            fog = FogManager.Instance?.Capture(history) ?? new SavedFog()
        };
        var tokens = new List<SceneToken>();
        foreach (var token in UnityEngine.Object.FindObjectsByType<TokenController>(FindObjectsInactive.Exclude))
        {
            if (!token.IsSpawned) continue;
            if (token.IsLocalDragActiveAny()) throw new InvalidOperationException("Закончите перемещение токенов перед сохранением.");
            tokens.Add(CaptureToken(token, grid));
        }
        scene.tokens = tokens.ToArray();
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
        var scene = JsonUtility.FromJson<SceneDefinition>(File.ReadAllText(path));
        SceneValidation.Validate(scene);
        byte[] map = Decode(scene.mapImage, MapSync.MaxMapBytes);
        var portraits = new Dictionary<string, byte[]>();
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
                var token = TokenManager.Instance.RestoreSceneToken(data, grid);
                if (token == null) throw new InvalidOperationException("Не удалось восстановить токен.");
                byte[] portrait = portraits[data.id];
                if (portrait != null) TokenImageSync.BroadcastImage(token.NetworkObjectId, portrait);
            }
            TokenController.RebuildCellOccupancy();
        }
        finally { if (!curtain && HostSceneCurtain.IsCurtainDown) SceneEditor.Instance.RevealAfterMapTransfer(); }
        DiceUI.Instance?.ShowToolNotice("Сцена загружена. Раскрытие карты приостановлено.");
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
}
