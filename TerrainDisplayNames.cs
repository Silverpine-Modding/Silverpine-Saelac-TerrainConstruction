#nullable enable
using System;

namespace SilverpineMods.TerrainConstruction;

internal static class TerrainDisplayNames
{
    private const string SnowyPrefix = "terrainconstruction_snowy_";
    private const string OverlayPrefix = "prefab_tile_terrainconstruction_overlay_";
    internal static string InteriorLabel(string baseLabel) => baseLabel + " (Interior / Roofed)";

    internal static bool IsManagedTerrainName(string name) =>
        name.StartsWith(TerrainInteriors.PrefabPrefix, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(SnowyPrefix, StringComparison.OrdinalIgnoreCase) ||
        name.StartsWith(OverlayPrefix, StringComparison.OrdinalIgnoreCase);

    internal static bool IsMenuProxyName(string name) =>
        name.StartsWith("terrainconstruction_", StringComparison.OrdinalIgnoreCase) &&
        !name.StartsWith(SnowyPrefix, StringComparison.OrdinalIgnoreCase);

    internal static bool TryGetName(string prefabName, out string label)
    {
        label = "";
        if (!IsManagedTerrainName(prefabName)) return false;
        string native = TerrainLayers.GetNativeName(prefabName);
        bool interior = prefabName.StartsWith(TerrainInteriors.PrefabPrefix, StringComparison.OrdinalIgnoreCase);
        bool winter = prefabName.StartsWith(SnowyPrefix, StringComparison.OrdinalIgnoreCase) ||
            prefabName.StartsWith(TerrainInteriors.PrefabPrefix + "snowy_", StringComparison.OrdinalIgnoreCase);
        label = winter ? Plugin.GetSnowyLabel(native) : Plugin.GetLabel(native);
        if (interior) label = InteriorLabel(label);
        return true;
    }
}
