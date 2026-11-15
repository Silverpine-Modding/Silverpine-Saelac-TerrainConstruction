using System.Reflection;
using HarmonyLib;
using Mono.Cecil;
using UnityEngine;

internal static class InteriorChecks
{
    internal static void Run(Assembly assembly, Action<bool, string> check)
    {
        Type Type(string name) => assembly.GetType("SilverpineMods.TerrainConstruction." + name)!;
        object? Call(string type, string method, params object[] args) =>
            Type(type).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        string Interior(string name) => (string)Call("TerrainInteriors", "GetPrefabName", name)!;
        string Native(string name) => (string)Call("TerrainLayers", "GetNativeName", name)!;
        bool Eligible(string name, bool roofed) => (bool)Call("TerrainInteriors", "ShouldCreateVariant", name, roofed)!;
        string[] terrains = { "grass", "grass_swamp", "rock_mountain", "rock_overgrown", "rock_temple", "rock_temple_outside",
            "abyss_bricks", "abyss_snow", "icy_slip", "icy_tiles", "resin", "sand_pebbles", "water_bathhouse",
            "water_lake", "water_shallow", "water_shallow_swamp", "water_swamp" };
        check(terrains.All(n => Eligible("prefab_tile_" + n, false)), "all unroofed distinct terrain entries can receive an interior variant");
        check(terrains.All(n => !Eligible("prefab_tile_" + n, true)), "already roofed prefabs never receive a duplicate interior");
        check(!Eligible("prefab_tile_dirt_dry", false) && !Eligible("PREFAB_TILE_DIRT_DRY", false) &&
            !Eligible("prefab_tile_dirt_dry_roofed", true), "existing Roofed Dry Dirt supplies the native interior counterpart");
        check(!Eligible("prefab_tile_wood", false) && !Eligible("prefab_tile_rock", false), "interiors do not reintroduce base construction duplicates");
        check(terrains.All(n => Native(Interior("prefab_tile_" + n)) == "prefab_tile_" + n &&
            !Eligible(Interior("prefab_tile_" + n), false)), "interior prefab names round-trip without generating interiors of interiors");
        string snowy = "terrainconstruction_snowy_prefab_tile_grass";
        check(Interior(snowy) != Interior("prefab_tile_grass") && Native(Interior(snowy)) == "prefab_tile_grass" && Eligible(snowy, false),
            "snowy interiors have distinct persistent names and retain native terrain classification");
        check(!Interior("prefab_tile_grass").Contains("prefab_tile_grass") && !Interior(snowy).Contains("prefab_tile_grass"),
            "interior grass names avoid the native grass object pool and stale roof registrations");
        check(terrains.All(n => string.Equals(Native(Interior("prefab_tile_" + n).ToUpperInvariant()), "prefab_tile_" + n, StringComparison.OrdinalIgnoreCase)),
            "interior names remain case-insensitive");
        string[] overlays = { "water_bathhouse", "water_shallow", "water_shallow_swamp", "icy_slip" };
        check(overlays.All(n => (bool)Call("TerrainLayers", "IsOverlayName", Interior("prefab_tile_" + n))! &&
            (bool)Call("TerrainLayers", "IsOverlayName", Interior("terrainconstruction_snowy_prefab_tile_" + n))!),
            "normal and frozen interior water/ice retain their overlay layer");
        check(terrains.Except(overlays).All(n => !(bool)Call("TerrainLayers", "IsOverlayName", Interior("prefab_tile_" + n))!),
            "opaque interior terrain remains a replaceable base, including abyss and deep water");
        check(overlays.Concat(new[] { "water_lake", "water_swamp" }).All(n =>
            (bool)Call("TerrainAnimationSync", "IsAnimatedTerrainName", Interior("prefab_tile_" + n))!),
            "interior water and ice join the native synchronized animation clock");

        using var module = ModuleDefinition.ReadModule(assembly.Location);
        TypeDefinition Definition(string name) => module.Types.Single(t => t.Name == name);
        MethodReference[] Calls(string type, string method) => Definition(type).Methods.Single(m => m.Name == method)
            .Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        check(Definition("InteriorTerrain").Interfaces.Any(i => i.InterfaceType.Name == "ISerializableMonoBehavior"),
            "interior roots participate in the native terrain save stream");
        check(Calls("Plugin", "RegisterTerrain").Any(m => m.Name == "TryRegisterInteriorTerrain") &&
            Calls("Plugin", "TryRegisterSnowyTerrain").Any(m => m.Name == "TryRegisterInteriorTerrain"),
            "both normal and snowy menu registration offer missing interiors");
        check(Calls("Plugin", "TryRegisterInteriorTerrain").Any(m => m.Name == "CloneInactiveTerrain") &&
            Calls("Plugin", "TryRegisterInteriorTerrain").Any(m => m.DeclaringType.Name == "SerializablePrefabs" && m.Name == "Register"),
            "interiors clone existing terrain and register persistent prefab templates");
        var configure = Calls("TerrainInteriors", "ConfigureTemplate");
        check(new[] { "set_sprite", "set_alphaCutoff", "set_isCustomRangeActive", "set_frontSortingLayerID", "set_frontSortingOrder",
                "set_backSortingLayerID", "set_backSortingOrder", "set_spriteSortPoint" }.All(n => configure.Any(m => m.Name == n)),
            "interior weather masks copy the native floor sprite, cutoff, sorting range and sort point");
        check(!Definition("InteriorTerrain").Methods.Any(m => m.Name is "OnDestroy" or "OnDisable" or "Update") &&
            !configure.Any(m => m.Name.Contains("Destroy") || m.Name.Contains("Material") || m.Name.Contains("Season")),
            "interiors require no lifecycle teardown or weather/season overrides");

        MethodInfo instantiate = typeof(UnityEngine.Object).GetMethods().Single(m => m.Name == "Instantiate" && m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType == typeof(Vector3) &&
            m.GetParameters()[2].ParameterType == typeof(Quaternion)).MakeGenericMethod(typeof(GameObject));
        var replacement = ((IEnumerable<CodeInstruction>)Call("InteriorFloorRestorePatch", "Transpiler",
            new List<CodeInstruction> { new(System.Reflection.Emit.OpCodes.Call, instantiate) })!).ToArray();
        check(replacement.Length == 2 && Equals(replacement[0].operand, instantiate) &&
            replacement[1].operand is MethodInfo { Name: "ActivateRestoredTerrain" },
            "floor deconstruction keeps native restoration then activates its result");
        using var native = ModuleDefinition.ReadModule(typeof(GrassTileHandler).Assembly.Location);
        check(native.Types.Single(t => t.Name == "GrassTileHandler").Methods.Single(m => m.Name == "OnDeconstructed")
            .Body.Instructions.Select(i => i.Operand).OfType<GenericInstanceMethod>().Count(m =>
                m.Name == "Instantiate" && m.GenericArguments.Single().Name == "GameObject" && m.Parameters.Count == 3) == 1,
            "installed native floor restoration has exactly one supported instantiate call");
        var restore = Calls("InteriorFloorRestorePatch", "ActivateRestoredTerrain");
        check(restore.Any(m => m is GenericInstanceMethod g && g.GenericArguments.Any(a => a.Name == "InteriorTerrain")) &&
            restore.Any(m => m.Name == "SetActive") && !restore.Any(m => m.Name is "Register" or "Instantiate") &&
            restore.Any(m => m.Name == "RequestTileVisualRefresh"),
            "restored interiors activate and refresh without duplicate spawning or registration");
    }
}
