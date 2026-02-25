using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace VoRoulette.Core;

public static class RouletteStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };
    private static readonly Encoding ShiftJisEncoding = CreateShiftJisEncoding();

    public static RouletteState Load(string appName)
    {
        var path = GetPath(appName);
        if (!File.Exists(path))
        {
            return new RouletteState();
        }

        try
        {
            var json = File.ReadAllText(path, ShiftJisEncoding);
            return JsonSerializer.Deserialize<RouletteState>(json, JsonOptions) ?? new RouletteState();
        }
        catch
        {
            return new RouletteState();
        }
    }

    public static void Save(string appName, RouletteState state)
    {
        string? tempPath = null;
        try
        {
            var path = GetPath(appName);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(state, JsonOptions);
            tempPath = $"{path}.tmp";
            File.WriteAllText(tempPath, json, ShiftJisEncoding);
            File.Copy(tempPath, path, overwrite: true);
            File.Delete(tempPath);
        }
        catch
        {
            // App should keep running even if state persistence fails.
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }
    }

    private static string GetPath(string _appName)
    {
        return Path.Combine(AppContext.BaseDirectory, "state.json");
    }

    private static Encoding CreateShiftJisEncoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932);
    }
}
