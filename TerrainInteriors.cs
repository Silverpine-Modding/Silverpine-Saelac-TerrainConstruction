#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.TerrainConstruction;

internal static class TerrainInteriors
{
    internal const string PrefabPrefix = "prefab_tile_terrainconstruction_interior_";
    private const string SnowyPrefix = "terrainconstruction_snowy_";

    internal static string GetPrefabName(string sourceName) =>
        PrefabPrefix + (sourceName.StartsWith(SnowyPrefix, StringComparison.OrdinalIgnoreCase) ? "snowy_" : "") +
        TerrainLayers.GetNativeName(sourceName).Substring("prefab_tile_".Length);

    internal static string GetSourceName(string interiorName)
    {
        string suffix = interiorName.Substring(PrefabPrefix.Length);
        if (suffix.StartsWith("snowy_", StringComparison.OrdinalIgnoreCase))
            suffix = suffix.Substring("snowy_".Length);
        return "prefab_tile_" + suffix;
    }

    internal static bool ShouldCreateVariant(string sourceName, bool alreadyRoofed) =>
        !alreadyRoofed && !sourceName.StartsWith(PrefabPrefix, StringComparison.OrdinalIgnoreCase) &&
        // Dry Dirt already has a native Roofed Dry Dirt entry. Do not add a
        // second version, or reintroduce floors supplied by base construction.
        !string.Equals(TerrainLayers.GetNativeName(sourceName), "prefab_tile_dirt_dry", StringComparison.OrdinalIgnoreCase) &&
        !TerrainLayers.IsNativeFloor(TerrainLayers.GetNativeName(sourceName));

    internal static bool HasRoof(GameObject source) =>
        source.GetComponentsInChildren<TileTags>(true)
            .Any(tags => tags.tags != null && tags.tags.Contains(TileTag.Roofed));

    internal static void ConfigureTemplate(GameObject template, SpriteMask nativeMask)
    {
        if (template.activeSelf)
            throw new InvalidOperationException("Interior templates must be inactive before adding roof components.");
        if (nativeMask == null || nativeMask.sprite == null || !nativeMask.enabled)
            throw new InvalidOperationException("The native floor weather mask is unavailable.");
        if (template.GetComponent<InteriorTerrain>() != null)
            throw new InvalidOperationException("This terrain template already has an interior roof.");

        template.AddComponent<InteriorTerrain>();
        var roof = new GameObject("Terrain Interior Roof");
        roof.SetActive(false);
        roof.transform.SetParent(template.transform, worldPositionStays: false);
        roof.layer = template.layer;

        // A separate child keeps the root's native tags unchanged. In
        // particular, adding TileTags to deep water's root would make
        // SeasonWater start adding/removing Submerge there on season changes.
        roof.AddComponent<TileTags>().tags = new List<TileTag> { TileTag.Roofed };
        SpriteMask mask = roof.AddComponent<SpriteMask>();
        mask.sprite = nativeMask.sprite;
        mask.alphaCutoff = nativeMask.alphaCutoff;
        mask.isCustomRangeActive = nativeMask.isCustomRangeActive;
        mask.frontSortingLayerID = nativeMask.frontSortingLayerID;
        mask.frontSortingOrder = nativeMask.frontSortingOrder;
        mask.backSortingLayerID = nativeMask.backSortingLayerID;
        mask.backSortingOrder = nativeMask.backSortingOrder;
        mask.spriteSortPoint = nativeMask.spriteSortPoint;
        mask.enabled = true;
        // The parent is still inactive: TileTags.Awake only runs on the placed
        // or loaded instance, at its final grid position, not on this template.
        roof.SetActive(true);
    }
}

// The registered prefab carries its roof child across save/load. The empty
// native serialization interface also guarantees every interior is saveable.
internal sealed class InteriorTerrain : MonoBehaviour, ISerializableMonoBehavior, INPCVisiblePrefixProvider
{
    public string GetPrefix() => "roofed interior";
    public void Serialize(BinaryWriter writer) { }
    public void Deserialize(BinaryReader reader) { }
}
