using System.Collections.Generic;

namespace VoRoulette.Core;

public static class Presets
{
    public static IReadOnlyDictionary<string, string[]> All { get; } = new Dictionary<string, string[]>
    {
        ["OMG"] = ["テムジン", "バイパ－Ⅱ", "ドルカス", "ベルグドル", "バルバスバウ", "アファームド", "フェイ", "ライデン"],
        ["オラタン"] = ["ライデン", "シュタイン", "グリス", "テムジン", "テンパチ", "バル", "エンジェ", "アジム", "スぺ", "コマンダー", "バトラー", "ストライカー", "サイファー", "フェイ", "ドル"],
        ["フォース"] = ["TEMJIN系列", "RAIDEN系列", "VOX系列", "BAL系列", "APHARMD J系列", "APHARMD T系列", "MYZR系列", "SPECINEFF系列", "景清系列", "FEI-YEN系列", "ANGELAN系列", "GUARAYAKHA"],
        ["禁書VO"] = ["テムジン", "バルルルーン", "ライデン", "スペシネフ", "フェイ・イェン", "エンジェラン", "グリスボック", "アファームドS", "アファームドB", "アファームドC", "ドルドレイ", "サイファー", "バルバドス", "ブルーストーカー"],
    };
}
