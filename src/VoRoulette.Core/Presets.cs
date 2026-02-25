using System.Collections.Generic;
using System.Linq;

namespace VoRoulette.Core;

public static class Presets
{
    private static readonly Dictionary<string, string[]> BuiltInPresets = new()
    {
        ["OMG"] = ["テムジン", "バイパ－Ⅱ", "ドルカス", "ベルグドル", "バルバスバウ", "アファームド", "フェイ", "ライデン"],
        ["オラタン"] = ["ライデン", "シュタイン", "グリス", "テムジン", "テンパチ", "バル", "エンジェ", "アジム", "スぺ", "コマンダー", "バトラー", "ストライカー", "サイファー", "フェイ", "ドル"],
        ["フォース"] = ["TEMJIN系列", "RAIDEN系列", "VOX系列", "BAL系列", "APHARMD J系列", "APHARMD T系列", "MYZR系列", "SPECINEFF系列", "景清系列", "FEI-YEN系列", "ANGELAN系列", "GUARAYAKHA"],
        ["禁書VO"] = ["テムジン", "バルルルーン", "ライデン", "スペシネフ", "フェイ・イェン", "エンジェラン", "グリスボック", "アファームドS", "アファームドB", "アファームドC", "ドルドレイ", "サイファー", "バルバドス", "ブルーストーカー"],
    };

    private static readonly Dictionary<string, string[]> RegisteredPresets = new(BuiltInPresets);

    public static IReadOnlyDictionary<string, string[]> All => RegisteredPresets;

    public static bool IsBuiltIn(string name)
    {
        return BuiltInPresets.ContainsKey(name);
    }

    public static void ResetToBuiltIn()
    {
        RegisteredPresets.Clear();
        foreach (var (key, names) in BuiltInPresets)
        {
            RegisteredPresets[key] = names;
        }
    }

    public static bool Register(string name, IEnumerable<string> names, bool overwrite = false)
    {
        var key = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var list = names
            .Select(x => (x ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToArray();
        if (list.Length == 0)
        {
            return false;
        }

        if (!overwrite && RegisteredPresets.ContainsKey(key))
        {
            return false;
        }

        RegisteredPresets[key] = list;
        return true;
    }
}
