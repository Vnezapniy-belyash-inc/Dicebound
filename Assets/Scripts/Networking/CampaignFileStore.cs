using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Portable multi-scene campaign package. Application to the live table is handled elsewhere.</summary>
public static class CampaignFileStore
{
    public const int MaxCampaignFileBytes = 384 * 1024 * 1024;

    public static void ValidateEmbeddedImageBudget(CampaignDefinition campaign)
    {
        long aggregateBytes = 0;
        if (campaign.mapAssets != null)
            foreach (var map in campaign.mapAssets)
            {
                if (map == null) throw new FormatException("Некорректная карта каталога.");
                long mapBytes = DecodeBase64Size(map.imageData);
                if (mapBytes == 0 || mapBytes > MapSync.MaxMapBytes)
                    throw new FormatException("Карта в каталоге превышает 16 МБ или не содержит данных.");
                aggregateBytes += mapBytes;
                if (aggregateBytes > MaxCampaignFileBytes)
                    throw new FormatException("Суммарный размер данных кампании превышает 384 МБ.");
            }
        foreach (var entry in campaign.scenes)
        {
            long mapBytes = DecodeBase64Size(entry.scene.mapImage);
            if (mapBytes > MapSync.MaxMapBytes)
                throw new FormatException("Размер карты сцены превышает 16 МБ.");
            aggregateBytes += mapBytes;
            foreach (var token in entry.scene.tokens)
            {
                long portraitBytes = DecodeBase64Size(token.portrait);
                if (portraitBytes > TokenImageSync.MaxPortraitBytes)
                    throw new FormatException("Размер портрета токена превышает 2 МБ.");
                aggregateBytes += portraitBytes;
                if (aggregateBytes > MaxCampaignFileBytes)
                    throw new FormatException("Суммарный размер данных кампании превышает 384 МБ.");
            }
        }
        if (aggregateBytes > MaxCampaignFileBytes)
            throw new FormatException("Суммарный размер данных кампании превышает 384 МБ.");
    }

    private static long DecodeBase64Size(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        try { return Convert.FromBase64String(value).LongLength; }
        catch (FormatException ex) { throw new FormatException("Некорректные встроенные данные изображения.", ex); }
    }

    public static CampaignDefinition Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Путь к файлу кампании не задан.", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Файл кампании не найден.", path);
        if (info.Length > MaxCampaignFileBytes)
            throw new FormatException("Файл кампании превышает 384 МБ.");

        var campaign = JsonUtility.FromJson<CampaignDefinition>(File.ReadAllText(path, Encoding.UTF8));
        SceneValidation.Validate(campaign);
        ValidateEmbeddedImageBudget(campaign);
        return campaign;
    }

    public static CampaignDefinition LoadCompatible(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Путь к файлу не задан.", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Файл не найден.", path);
        if (info.Length > MaxCampaignFileBytes)
            throw new FormatException("Файл превышает 384 МБ.");
        string json = File.ReadAllText(path, Encoding.UTF8);
        var campaign = JsonUtility.FromJson<CampaignDefinition>(json);
        bool isCampaign = json.IndexOf("\"scenes\"", StringComparison.Ordinal) >= 0
            && json.IndexOf("\"campaignId\"", StringComparison.Ordinal) >= 0;
        if (isCampaign)
        {
            if (campaign == null) throw new FormatException("Файл кампании повреждён.");
            SceneValidation.Validate(campaign);
            ValidateEmbeddedImageBudget(campaign);
            return campaign;
        }
        var scene = SceneSaveMigration.UpgradeScene(JsonUtility.FromJson<SceneDefinition>(json));
        scene.mapAssetId = null; // A standalone scene embeds its map instead of referencing a campaign catalog.
        SceneValidation.Validate(scene);
        campaign = SceneSaveMigration.UpgradeSingleScene(scene);
        SceneValidation.Validate(campaign);
        ValidateEmbeddedImageBudget(campaign);
        return campaign;
    }

    public static CampaignDefinition LoadSingleSceneAsCampaign(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Путь к файлу сцены не задан.", nameof(path));
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("Файл сцены не найден.", path);
        if (info.Length > SceneValidation.MaxFileBytes)
            throw new FormatException("Файл сцены превышает 96 МБ.");

        var scene = SceneSaveMigration.UpgradeScene(
            JsonUtility.FromJson<SceneDefinition>(File.ReadAllText(path, Encoding.UTF8)));
        scene.mapAssetId = null;
        SceneValidation.Validate(scene);
        var campaign = SceneSaveMigration.UpgradeSingleScene(scene);
        SceneValidation.Validate(campaign);
        ValidateEmbeddedImageBudget(campaign);
        return campaign;
    }

    public static void Save(string path, CampaignDefinition campaign)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Путь к файлу кампании не задан.", nameof(path));
        SceneValidation.Validate(campaign);
        ValidateEmbeddedImageBudget(campaign);
        string json = JsonUtility.ToJson(campaign, true);
        if (Encoding.UTF8.GetByteCount(json) > MaxCampaignFileBytes)
            throw new FormatException("Файл кампании превышает 384 МБ.");

        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
