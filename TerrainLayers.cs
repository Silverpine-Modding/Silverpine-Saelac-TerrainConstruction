#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SilverpineMods.TerrainConstruction;

internal static class TerrainLayers
{
    private const string OverlayPrefix = "prefab_tile_terrainconstruction_overlay_";
    private const string SnowyPrefix = "terrainconstruction_snowy_";

    internal static string GetOverlayPrefabName(string nativeName) =>
        OverlayPrefix + nativeName.Substring("prefab_tile_".Length);

    internal static bool IsOverlayName(string name)
    {
        name = GetNativeName(name);
        return name.Equals("prefab_tile_water_bathhouse", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_water_shallow", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_water_shallow_swamp", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("prefab_tile_icy_slip", StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetNativeName(string name)
    {
        // Include native instances and the existing frozen names so old saves
        // are classified correctly, without renaming their serialized records.
        if (name.StartsWith(SnowyPrefix, StringComparison.OrdinalIgnoreCase))
            name = name.Substring(SnowyPrefix.Length);
        else if (name.StartsWith(OverlayPrefix, StringComparison.OrdinalIgnoreCase))
            name = "prefab_tile_" + name.Substring(OverlayPrefix.Length);
        return name;
    }

    internal static bool IsOverlay(GameObject item) =>
        item != null && IsOverlayName(SerializationManager.GetPrefabName(item));

    internal static List<GameObject> GetFloorReplacementCandidates(Vector2Int position) =>
        Turfs.GetTurfGameObjects(position).Where(item => !IsOverlay(item)).ToList();

    internal static bool IsNativeFloor(string prefabName) =>
        prefabName == "prefab_tile_wood" || prefabName == "prefab_tile_rock";

    internal static bool BlocksConstruction(Vector2Int position, TileTag tag, string prefabName)
    {
        if (!TileTags.HasTileTag(position, tag)) return false;
        if (tag != TileTag.Submerge || !IsNativeFloor(prefabName)) return true;
        List<GameObject> objects = Turfs.GetTurfGameObjects(position);
        // Only waive water's placement veto for floors over an actual base.
        // Do not modify the water tags: swimming, soaking and NPC behavior stay native.
        return !objects.Any(item => Plugin.IsReplaceableTerrain(item) && !IsOverlay(item)) ||
            TileTags.multiDictionary.GetValues(position).Any(tags =>
                tags.tags.Contains(tag) && !IsOverlay(tags.gameObject));
    }
}

// No GrassTileHandler: removal must reveal the existing base, never spawn a
// replacement grass tile or lose the underlying floor's serialized state.
internal sealed class TerrainOverlay : MonoBehaviour, ISerializableMonoBehavior, IDeconstructedHandler
{
    internal static void ConfigureTemplate(GameObject template)
    {
        if (template.activeSelf)
            throw new InvalidOperationException("Overlay templates must be inactive before adding target registration.");
        template.AddComponent<TerrainOverlay>();
        Deconstructable deconstructable = template.AddComponent<Deconstructable>();
        deconstructable.playerPropertyOnly = false;
        // The native component supplies Targetable and an empty material refund.
        AccessTools.Field(typeof(Deconstructable), "interactable").SetValue(deconstructable, true);
    }

    public void OnDeconstructed() =>
        TerrainPlacement.RequestTileVisualRefresh(transform.GetVector2IntPosition());

    // Makes even bathhouse water eligible for the normal terrain save stream.
    // Native seasonal components serialize their own data independently.
    public void Serialize(BinaryWriter writer) { }
    public void Deserialize(BinaryReader reader) { }
}

[HarmonyPatch(typeof(GrassTileHandler), nameof(GrassTileHandler.OnConstructed))]
internal static class FloorTerrainSelectionPatch
{
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = typeof(Turfs).GetMethod(nameof(Turfs.GetTurfGameObjects))!;
        MethodInfo replacement = typeof(TerrainLayers).GetMethod(nameof(TerrainLayers.GetFloorReplacementCandidates), BindingFlags.Static | BindingFlags.NonPublic)!;
        List<CodeInstruction> result = instructions.ToList();
        List<CodeInstruction> matches = result.Where(instruction => Equals(instruction.operand, original)).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException("Could not locate the native floor's terrain selection.");
        matches[0].operand = replacement;
        return result;
    }
}

// The game's private validation lambda otherwise rejects ALL construction on
// Submerge-tagged cells before GrassTileHandler gets a chance to change the floor.
[HarmonyPatch]
internal static class FloorWaterValidationPatch
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    private static readonly Type ConstructionItem = typeof(PlayerAbility_Construct).GetNestedType("ConstructionItem", All)!;
    private static readonly MethodInfo HasTag = typeof(TileTags).GetMethod(nameof(TileTags.HasTileTag))!;

    internal static MethodBase TargetMethod() => ConstructionItem.GetNestedTypes(All)
        .SelectMany(type => type.GetMethods(All | BindingFlags.DeclaredOnly))
        .Single(method => !method.IsStatic && method.ReturnType == typeof(bool) &&
            method.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual(new[] { typeof(Vector2), typeof(bool) }));

    internal static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        FieldInfo owner = __originalMethod.DeclaringType!.GetFields(All)
            .Single(field => field.FieldType == ConstructionItem);
        FieldInfo prefabName = ConstructionItem.GetField("prefabName", All)!;
        List<CodeInstruction> result = instructions.ToList();
        if (result.Count(instruction => Equals(instruction.operand, HasTag)) != 1)
            throw new InvalidOperationException("Could not locate the native construction water validation.");
        foreach (CodeInstruction instruction in result)
        {
            if (!Equals(instruction.operand, HasTag)) { yield return instruction; continue; }
            var loadOwner = new CodeInstruction(OpCodes.Ldarg_0);
            loadOwner.labels.AddRange(instruction.labels);
            loadOwner.blocks.AddRange(instruction.blocks);
            instruction.labels.Clear();
            instruction.blocks.Clear();
            yield return loadOwner;
            yield return new CodeInstruction(OpCodes.Ldfld, owner);
            yield return new CodeInstruction(OpCodes.Ldfld, prefabName);
            yield return new CodeInstruction(OpCodes.Call,
                typeof(TerrainLayers).GetMethod(nameof(TerrainLayers.BlocksConstruction), All));
        }
    }
}
