using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using HarmonyLib;
using Mono.Cecil;

if (args.Length != 1) throw new ArgumentException("Usage: TerrainChecks <game-directory>");
string game = Path.GetFullPath(args[0]);
AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    foreach (string folder in new[] { "Silverpine_Data/Managed", "BepInEx/core", "BepInEx/plugins/ModdingTools" })
    {
        string path = Path.Combine(game, folder, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
    }
    return null;
};
Checks.Run(game);

internal static class Checks
{
    private static int passed;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS " + message);
        passed++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(string game)
    {
        Assembly assembly = typeof(SilverpineMods.TerrainConstruction.Plugin).Assembly;
        Type Type(string name) => assembly.GetType("SilverpineMods.TerrainConstruction." + name)!;
        object? Call(string type, string method, params object[] args) =>
            Type(type).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        bool Overlay(string name) => (bool)Call("TerrainLayers", "IsOverlayName", name)!;
        string[] overlays = { "water_bathhouse", "water_shallow", "water_shallow_swamp", "icy_slip" };
        foreach (string suffix in overlays)
        {
            string native = "prefab_tile_" + suffix;
            string custom = (string)Call("TerrainLayers", "GetOverlayPrefabName", native)!;
            Check(Overlay(native) && Overlay(custom) && Overlay(custom.ToUpperInvariant()), suffix + " recognizes native and registered overlays");
            Check(custom.StartsWith("prefab_tile_") && custom.EndsWith(suffix), suffix + " remains recognizable to terrain/structure tools");
        }
        Check(Overlay("terrainconstruction_snowy_prefab_tile_water_shallow") &&
            Overlay("terrainconstruction_snowy_prefab_tile_water_shallow_swamp"), "existing frozen shallow-water save names remain overlays");
        string[] bases = { "grass", "grass_swamp", "wood", "rock", "abyss_bricks", "abyss_snow", "dirt_dry", "dirt_dry_roofed", "water", "water_lake", "water_swamp", "icy_tiles", "rock_mountain", "resin", "sand_pebbles", "rock_overgrown", "rock_temple", "rock_temple_outside" };
        Check(bases.All(name => !Overlay("prefab_tile_" + name)) &&
            !Overlay("terrainconstruction_snowy_prefab_tile_water_lake"), "all opaque bases, including deep/frozen deep water and abyss, remain replaceable bases");
        Check(!Overlay("prefab_tile_water_shallow_custom") && !Overlay("prefab_tile_teleporter_world"), "overlay matching does not absorb unrelated prefabs");
        Check((bool)Call("TerrainLayers", "IsNativeFloor", "prefab_tile_wood")! &&
            (bool)Call("TerrainLayers", "IsNativeFloor", "prefab_tile_rock")! &&
            !(bool)Call("TerrainLayers", "IsNativeFloor", "prefab_furniture_firepit")!, "underwater validation exception is restricted to native floors");

        MethodBase target = (MethodBase)Call("FloorWaterValidationPatch", "TargetMethod")!;
        using var nativeModule = ModuleDefinition.ReadModule(Path.Combine(game, "Silverpine_Data/Managed/Assembly-CSharp.dll"));
        bool MenuIncludes(string name) => (bool)Call("Plugin", "IsTerrainMenuPrefab", name)!;
        string[] nativeConstructionTerrain = nativeModule.Types.Single(t => t.Name == "PlayerAbility_Construct")
            .Methods.Single(m => m.Name == "OnUse").Body.Instructions
            .Where(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Ldstr).Select(i => (string)i.Operand)
            .Where(name => name.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase))
            .Distinct().OrderBy(name => name).ToArray();
        Check(nativeConstructionTerrain.SequenceEqual(new[] { "prefab_tile_rock", "prefab_tile_wood" }),
            "installed base construction offers exactly the expected rock and wood terrain prefabs");
        Check(nativeConstructionTerrain.All(name => !MenuIncludes(name) && !MenuIncludes(name.ToUpperInvariant())),
            "all base construction terrain duplicates are excluded from the Terrains menu");
        Check(new[] { "rock_mountain", "rock_overgrown", "rock_temple", "rock_temple_outside", "dirt_dry_roofed" }
            .All(name => MenuIncludes("prefab_tile_" + name)), "distinct rocky and roofed terrain variants remain available");
        Check(bases.Concat(overlays).Except(new[] { "wood", "rock", "water" })
            .All(name => MenuIncludes("prefab_tile_" + name)), "other existing base terrain and overlay entries remain available");
        Check(new[] { "prefab_tile_teleporter_world", "prefab_tile_wood_bridge", "prefab_tile_water", "prefab_furniture_firepit" }
            .All(name => !MenuIncludes(name) && !MenuIncludes(name.ToUpperInvariant())),
            "previous duplicate, transition-trigger, and non-terrain exclusions remain intact");
        // Read the native IL without invoking Harmony's Mono-only dynamic runtime.
        // Non-target operands remain opaque; these patches must preserve them.
        List<CodeInstruction> Instructions(MethodBase method, MethodInfo redirect)
        {
            var codes = typeof(System.Reflection.Emit.OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)!).ToDictionary(c => c.Value);
            var body = ((MethodDefinition)nativeModule.LookupToken(method.MetadataToken)).Body;
            return body.Instructions.Select(i => new CodeInstruction(codes[i.OpCode.Value],
                i.Operand is MethodReference m && m.DeclaringType.FullName == redirect.DeclaringType!.FullName &&
                    m.Name == redirect.Name ? redirect : i.Operand)).ToList();
        }
        var original = Instructions(target, typeof(TileTags).GetMethod("HasTileTag")!);
        var branchTarget = new System.Reflection.Emit.DynamicMethod("testLabels", typeof(void), System.Type.EmptyTypes)
            .GetILGenerator().DefineLabel();
        original.Single(i => i.operand is MethodInfo m && m.Name == "HasTileTag").labels.Add(branchTarget);
        var patched = ((IEnumerable<CodeInstruction>)Call("FloorWaterValidationPatch", "Transpiler", original, target)!).ToList();
        Check(target.DeclaringType?.DeclaringType?.Name == "ConstructionItem" &&
            patched.Count(i => i.operand is MethodInfo m && m.Name == "BlocksConstruction") == 1 &&
            !patched.Any(i => i.operand is MethodInfo m && m.DeclaringType?.Name == "TileTags" && m.Name == "HasTileTag"), "real game validation IL redirects only the water check");
        int index = patched.FindIndex(i => i.operand is MethodInfo m && m.Name == "BlocksConstruction");
        Check(patched[index - 3].opcode == System.Reflection.Emit.OpCodes.Ldarg_0 &&
            patched[index - 2].operand is FieldInfo owner && owner.FieldType.Name == "ConstructionItem" &&
            patched[index - 1].operand is FieldInfo field && field.Name == "prefabName", "water validation supplies the actual construction prefab name");
        Check(patched[index - 3].labels.Contains(branchTarget), "branches into the replaced check still load the added prefab argument");
        MethodInfo floor = typeof(GrassTileHandler).GetMethod("OnConstructed")!;
        var floorPatched = ((IEnumerable<CodeInstruction>)Call("FloorTerrainSelectionPatch", "Transpiler",
            Instructions(floor, typeof(Turfs).GetMethod("GetTurfGameObjects")!))!).ToArray();
        Check(floorPatched.Count(i => i.operand is MethodInfo m && m.Name == "GetFloorReplacementCandidates") == 1 &&
            !floorPatched.Any(i => i.operand is MethodInfo m && m.DeclaringType?.Name == "Turfs" && m.Name == "GetTurfGameObjects"), "real native floor construction filters overlays before consuming/restoring terrain");
        bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (Exception e) when (e is InvalidOperationException || e is TargetInvocationException { InnerException: InvalidOperationException }) { return true; }
        }
        Check(Rejects(() => Call("FloorTerrainSelectionPatch", "Transpiler", new List<CodeInstruction>())) &&
            Rejects(() => ((IEnumerable<CodeInstruction>)Call("FloorWaterValidationPatch", "Transpiler", new List<CodeInstruction>(), target)!).ToList()),
            "changed native IL fails safely rather than silently applying an incomplete patch");

        using var module = ModuleDefinition.ReadModule(assembly.Location);
        TypeDefinition Definition(string name) => module.Types.Single(t => t.Name == name);
        MethodReference[] Calls(string type, string method) => Definition(type).Methods.Single(m => m.Name == method)
            .Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Check(Calls("Plugin", "RegisterTerrainConstructions").Any(m => m.Name == "IsTerrainMenuPrefab"),
            "terrain registration uses the tested menu filter");
        Check(!Calls("Plugin", "IsReplaceableTerrain").Any(m => m.Name is "IsTerrainMenuPrefab" or "IsNativeFloor"),
            "hiding native floors from the menu does not exclude existing floors from terrain replacement");
        Check(!Definition("Plugin").Methods.Any(m => m.Name is "OnDestroy" or "OnDisable"), "plugin lifecycle does not tear down on bootstrap destruction");
        Check(Definition("TerrainOverlay").Interfaces.Select(i => i.InterfaceType.Name).ToHashSet()
            .IsSupersetOf(new[] { "ISerializableMonoBehavior", "IDeconstructedHandler" }), "overlay removal and serialization use native interfaces");
        var configure = Calls("TerrainOverlay", "ConfigureTemplate");
        Check(configure.Any(m => m is GenericInstanceMethod g && g.GenericArguments.Any(a => a.Name == "Deconstructable")) &&
            !configure.Any(m => m is GenericInstanceMethod g && g.GenericArguments.Any(a => a.Name == "GrassTileHandler")), "overlay gains deconstruction without a grass-restoration handler");
        Check(Calls("TerrainOverlay", "OnDeconstructed").Any(m => m.Name == "RequestTileVisualRefresh") &&
            !Calls("TerrainOverlay", "OnDeconstructed").Any(m => m.Name.Contains("Instantiate") || m.Name.Contains("Destroy")), "overlay removal queues visual refresh without touching the base");
        Check(Calls("TerrainPlacement", "RefreshPlacedTileVisuals").Any(m => m.DeclaringType.Name == "ModTileVisuals" && m.Name == "Refresh") &&
            Calls("TerrainPlacement", "RefreshPlacedTileVisuals").Any(m => m.Name == "RequestTileVisualRefresh") &&
            Calls("TerrainPlacement", "RequestTileVisualRefresh").Any(m => m.DeclaringType.Name == "ModTileVisuals" && m.Name == "RequestRefresh"), "placement refreshes existing neighbors immediately and queues the post-Start pass through Modding Tools");
        Check(Calls("Plugin", "RegisterTerrain").Any(m => m.Name == "ConfigureTemplate") &&
            Calls("Plugin", "TryRegisterSnowyTerrain").Any(m => m.Name == "ConfigureTemplate"), "normal and frozen overlays both receive persistent removal support");
        Check(!Definition("TerrainLayers").Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Any(i => i.OpCode.Code == Mono.Cecil.Cil.Code.Stfld && i.Operand is FieldReference f && f.Name == "tags"), "construction exception does not disable native water tags");
        AnimationChecks.Run(assembly, Check);
        InteriorChecks.Run(assembly, Check);
        NamingChecks.Run(assembly, game, Check);
        Console.WriteLine($"{passed} checks passed. Unity placement, rendering, and save/load still require in-game verification.");
    }
}
