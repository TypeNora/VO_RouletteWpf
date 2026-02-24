using System;
using System.Collections.Generic;
using System.Linq;

namespace VoRoulette.Core;

public static class RouletteEngine
{
    public static IReadOnlyList<RouletteEntry> Normalize(IEnumerable<RouletteEntry> entries)
    {
        return entries
            .Where(e => !string.IsNullOrWhiteSpace(e.Name))
            .Select(e => e with { Weight = ClampWeight(e.Weight) })
            .ToArray();
    }

    public static RouletteEntry? PickWeighted(IReadOnlyList<RouletteEntry> entries, Random random)
    {
        var enabled = entries.Where(e => e.Enabled).ToArray();
        if (enabled.Length == 0)
        {
            return null;
        }

        var total = enabled.Sum(e => ClampWeight(e.Weight));
        if (total <= 0)
        {
            return enabled[0];
        }

        var roll = random.NextDouble() * total;
        double cumulative = 0;
        foreach (var entry in enabled)
        {
            cumulative += ClampWeight(entry.Weight);
            if (roll <= cumulative)
            {
                return entry;
            }
        }

        return enabled[^1];
    }

    public static double ClampWeight(double weight)
    {
        if (double.IsNaN(weight) || weight < 0.1)
        {
            return 0.1;
        }

        return weight > 10 ? 10 : weight;
    }
}
