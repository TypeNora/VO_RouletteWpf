using System;
using System.IO;
using System.Text.Json;

namespace VoRoulette.Core;

public static class RouletteStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static RouletteState Load(string appName)
    {
        var path = GetPath(appName);
        if (!File.Exists(path))
        {
            return new RouletteState();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RouletteState>(json, JsonOptions) ?? new RouletteState();
        }
        catch
        {
            return new RouletteState();
        }
    }

    public static void Save(string appName, RouletteState state)
    {
        var path = GetPath(appName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(path, json);
    }

    private static string GetPath(string appName)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseDir, appName, "state.json");
    }
}
