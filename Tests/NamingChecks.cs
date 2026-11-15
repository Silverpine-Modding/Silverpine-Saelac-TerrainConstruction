using System.Reflection;
using HarmonyLib;

internal static class NamingChecks
{
    internal static void Run(Assembly assembly, string game, Action<bool, string> check)
    {
        Type Type(string name) => assembly.GetType("SilverpineMods.TerrainConstruction." + name)!;
        object? Call(string type, string method, params object[] args) =>
            Type(type).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        string? Name(string prefab)
        {
            object[] args = { prefab, "" };
            return (bool)Call("TerrainDisplayNames", "TryGetName", args)! ? (string)args[1] : null;
        }
        string prefix = "prefab_tile_terrainconstruction_interior_";
        foreach (var pair in new Dictionary<string, string>
        {
            ["grass"] = "Grass (Interior / Roofed)",
            ["grass_swamp"] = "Swamp Grass (Interior / Roofed)",
            ["rock_mountain"] = "Mountain Rock (Interior / Roofed)",
            ["water_lake"] = "Water (Interior / Roofed)",
            ["water_bathhouse"] = "Bathhouse Water (Interior / Roofed)",
            ["snowy_grass"] = "Snow (Interior / Roofed)",
            ["snowy_water_shallow"] = "Frozen Shallow Water (Interior / Roofed)"
        }) check(Name(prefix + pair.Key) == pair.Value && Name((prefix + pair.Key).ToUpperInvariant()) == pair.Value,
            "readable persisted-name label: " + pair.Value);
        check(Name("terrainconstruction_snowy_prefab_tile_grass") == "Snow" &&
            Name("prefab_tile_terrainconstruction_overlay_water_bathhouse") == "Bathhouse Water",
            "outdoor winter and overlay variants retain distinguishable readable names");
        check(Name("prefab_tile_grass") == null && Name("prefab_furniture_chair") == null &&
            Name("terrainconstruction_prefab_tile_grass") == null, "native assets and menu proxies are not relabeled as interiors");
        check(new[] { "terrainconstruction_prefab_tile_grass", "terrainconstruction_terrainconstruction_snowy_prefab_tile_grass",
            "terrainconstruction_" + prefix + "grass" }.All(n => (bool)Call("TerrainDisplayNames", "IsMenuProxyName", n)!) &&
            !(bool)Call("TerrainDisplayNames", "IsMenuProxyName", "terrainconstruction_snowy_prefab_tile_grass")!,
            "menu-only proxies are distinguished from real frozen terrain prefabs");
        check(Type("InteriorTerrain").GetInterfaces().Contains(typeof(INPCVisiblePrefixProvider)),
            "interior terrain supplies native descriptive prefixes without replacing seasonal descriptions");
        object[] category = { prefix + "grass", 3 };
        Call("StructureHandlerCompatibility", "Category", category);
        check((int)category[1] == 0, "interior grass is grouped with Terrain, not Nature, in Structure Handler");
        object[] unchanged = { "prefab_tree_pine", 3 };
        Call("StructureHandlerCompatibility", "Category", unchanged);
        check((int)unchanged[1] == 3, "unrelated Structure Handler categories remain unchanged");

        Assembly structure = Assembly.LoadFrom(Path.Combine(game, "BepInEx/plugins/StructureHandler/StructureHandler.dll"));
        Type editor = structure.GetType("StructureHandler.StructureEditorUI", true)!;
        check(new[] { ("CatalogDisplayName", typeof(string)), ("GetQuickPlaceCategory", typeof(int)),
            ("IsUnsafeQuickPlaceName", typeof(bool)), ("IsQuickPlaceSupportingTerrain", typeof(bool)) }.All(pair =>
            editor.GetMethod(pair.Item1, BindingFlags.Static | BindingFlags.NonPublic) is MethodInfo m &&
                m.ReturnType == pair.Item2 && m.GetParameters().Single().ParameterType == typeof(string)),
            "optional naming integration matches all four installed Structure Handler APIs");
        // HarmonyX's patch machinery requires Mono, not this .NET test host.
        // Verify the actual wrapper signature here; RuntimeCompatibility runs
        // both real patches in the game's Mono runtime in both load orders.
        MethodInfo wrapper = structure.GetType("StructureHandler.DestinationGround", true)!
            .GetMethod("InstantiateRestoredGround", BindingFlags.NonPublic | BindingFlags.Static)!;
        var afterStructure = new List<CodeInstruction> { new(System.Reflection.Emit.OpCodes.Call, wrapper) };
        var afterTerrain = ((IEnumerable<CodeInstruction>)Call("InteriorFloorRestorePatch", "Transpiler", afterStructure)!).ToArray();
        check(afterTerrain.Length == 2 && afterTerrain[0].operand is MethodInfo { Name: "InstantiateRestoredGround" } &&
            afterTerrain[1].operand is MethodInfo { Name: "ActivateRestoredTerrain" },
            "installed Structure Handler restoration call is retained before interior activation");
        check(Type("InteriorFloorRestorePatch").GetMethod("Transpiler", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetCustomAttribute<HarmonyAfter>()!.info.after.Contains("renegadex.silverpine.customstructures.restored-ground"),
            "Harmony explicitly runs interior activation after Structure Handler's restoration patch");
    }
}
