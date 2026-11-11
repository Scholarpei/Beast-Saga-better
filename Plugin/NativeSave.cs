using System.Text.Json;
using UnityEngine;

namespace SafeDialogueTools;

internal static class NativeSave
{
    internal static void Write(string folder)
    {
        var manager = SaveManager._Instance;
        var path = Path.Combine(folder, "save.es3");
        var originalUtil = SaveManager.SaveTempUtilDataPath;
        var originalCurrent = manager.CurrentSavePath;
        var isolatedUtil = Path.Combine(folder, "map-util.es3");
        if (!string.IsNullOrEmpty(originalUtil) && File.Exists(originalUtil)) File.Copy(originalUtil, isolatedUtil);
        try
        {
            SaveManager.SaveTempUtilDataPath = isolatedUtil;
            SaveUtilManager._Instance.SaveUtilCurrent();
            var file = new ES3File(path);
            manager.SaveMapDataInfo(path, file);
            manager.SavePlayerDataInfo(path, file);
            manager.SavePlayerFriendDataInfo(path, file);
            manager.SaveTimeData(path, file);
            manager.SaveCustomJunData(path, file);
            manager.SaveDialogueLJGData(file);
            manager.SaveWeatherData(path, file);
            manager.SaveUtilDataInfo(path, file);
            var mapData = File.ReadAllText(isolatedUtil);
            file.Save<string>(SaveManager.SaveMapUtil, mapData);
            var info = new SaveInfo
            {
                id = 1,
                name = "安全检查点",
                updateTime = DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss"),
                Path = "save.es3",
                playerLv = PlayerDataManager._Instance._PlayerData.Lv,
                mapName = MapManager._Instance.GetMapNameInfo(),
                mapUrl = "",
                round = PlayerDataManager._Instance._PlayerData._Round,
                _Dlc = manager._CurrentDlcIndex
            };
            file.Save<SaveInfo>(SaveManager.SaveInfoData, info);
            file.Sync();
        }
        finally
        {
            SaveManager.SaveTempUtilDataPath = originalUtil;
            manager.CurrentSavePath = originalCurrent;
        }
    }

    internal static SaveInfo ReadInfo(string folder) => new ES3File(Path.Combine(folder, "save.es3")).Load<SaveInfo>(SaveManager.SaveInfoData);

    internal static void WriteThumbnail(string folder)
    {
        var path = Path.Combine(folder, "thumbnail.png");
        var captured = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        try
        {
            captured.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0, false);
            captured.Apply(false, false);
            var render = RenderTexture.GetTemporary(320, 180, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            var thumbnail = new Texture2D(320, 180, TextureFormat.RGBA32, false);
            try
            {
                Graphics.Blit(captured, render);
                RenderTexture.active = render;
                thumbnail.ReadPixels(new Rect(0, 0, 320, 180), 0, 0, false);
                thumbnail.Apply(false, false);
                File.WriteAllBytes(path, ImageConversion.EncodeToPNG(thumbnail));
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(render);
                UnityEngine.Object.Destroy(thumbnail);
            }
        }
        finally { UnityEngine.Object.Destroy(captured); }
    }

    internal static void Validate(string folder)
    {
        var file = new ES3File(Path.Combine(folder, "save.es3"));
        foreach (var key in new[] { SaveManager.SaveInfoData, SaveManager.SaveMap, SaveManager.SavePlayerData,
                     SaveManager.SaveFriendData, SaveManager.SaveTime, SaveManager.SaveCustomData,
                     SaveManager.SaveDialogueData, SaveManager.SaveWeather, SaveManager.SaveUtilData, SaveManager.SaveMapUtil })
            if (!file.KeyExists(key)) throw new InvalidDataException("Missing native save key: " + key);
        var map = file.Load<MapSaveData>(SaveManager.SaveMap);
        var player = file.Load<PlayerData>(SaveManager.SavePlayerData);
        if (map == null || string.IsNullOrEmpty(map._CurrentMapName) || player == null || player.Lv < 1)
            throw new InvalidDataException("Incomplete player/map state");
        if (file.Load<SaveFriendData>(SaveManager.SaveFriendData) == null ||
            file.Load<TimeSaveData>(SaveManager.SaveTime) == null ||
            file.Load<CustomData>(SaveManager.SaveCustomData) == null ||
            file.Load<WeatherSaveData>(SaveManager.SaveWeather) == null ||
            file.Load<SaveSingleData>(SaveManager.SaveUtilData) == null ||
            file.Load<SaveInfo>(SaveManager.SaveInfoData) == null)
            throw new InvalidDataException("Incomplete native payload");
        var dialogue = file.Load<string>(SaveManager.SaveDialogueData);
        using var data = JsonDocument.Parse(dialogue);
        if (!data.RootElement.TryGetProperty("m_list", out var list) || list.GetArrayLength() == 0)
            throw new InvalidDataException("Missing dialogue persistent state");
        var mapFile = new ES3File(Path.Combine(folder, "map-util.es3"));
        foreach (var key in mapFile.GetKeys())
            if (mapFile.Load<SaveUtilDataDictionary>(key) == null) throw new InvalidDataException("Invalid map payload: " + key);
        if (file.Load<string>(SaveManager.SaveMapUtil) != File.ReadAllText(Path.Combine(folder, "map-util.es3")))
            throw new InvalidDataException("Embedded map state differs from checkpoint payload");
    }
}
