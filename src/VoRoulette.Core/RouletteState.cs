using System.Collections.Generic;

namespace VoRoulette.Core;

public sealed class RouletteState
{
    public List<RouletteEntry> Entries { get; init; } = [];

    public List<List<RouletteEntry>?> Favorites { get; init; } = [null, null, null];

    public string SelectedPreset { get; init; } = "オラタン";
}
