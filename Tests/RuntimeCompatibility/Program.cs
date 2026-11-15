using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Logging;
using HarmonyLib;

internal static class Program
{
    private static int checks;
    private static string game;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Console.WriteLine("PASS " + message);
    }
    private static void Main(string[] args)
    {
        try
        {
            game = Path.GetFullPath(args.Single());
            AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
            {
                string name = new AssemblyName(e.Name).Name + ".dll";
                foreach (string folder in new[] { "Silverpine_Data/Managed", "BepInEx/core", "BepInEx/plugins/ModdingTools", "BepInEx/plugins/StructureHandler" })
                {
                    string path = Path.Combine(game, folder, name);
                    if (File.Exists(path)) return Assembly.LoadFrom(path);
                }
                return null;
            };
            Run();
            Console.WriteLine(checks + " Unity-Mono compatibility checks passed. No game or save was opened.");
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        var terrain = typeof(SilverpineMods.TerrainConstruction.Plugin).Assembly;
        var structure = Assembly.LoadFrom(Path.Combine(game, "BepInEx/plugins/StructureHandler/StructureHandler.dll"));
        Type terrainPatch = terrain.GetType("SilverpineMods.TerrainConstruction.InteriorFloorRestorePatch", true);
        Type structurePatch = structure.GetType("StructureHandler.StructureRestoredGroundPatch", true);
        MethodInfo target = typeof(GrassTileHandler).GetMethod("OnDeconstructed");
        string structureId = "renegadex.silverpine.customstructures.restored-ground";
        string terrainId = "terrainconstruction.runtime-check";
        foreach (bool structureFirst in new[] { true, false })
        {
            try
            {
                if (structureFirst) Harmony.CreateAndPatchAll(structurePatch, structureId);
                Harmony.CreateAndPatchAll(terrainPatch, terrainId);
                if (!structureFirst) Harmony.CreateAndPatchAll(structurePatch, structureId);
                var calls = PatchProcessor.GetCurrentInstructions(target).Select(i => i.operand).OfType<MethodInfo>().ToArray();
                Check(calls.Count(m => m.Name == "InstantiateRestoredGround") == 1 && calls.Count(m => m.Name == "ActivateRestoredTerrain") == 1,
                    "actual Harmony patches compose on Unity Mono (Structure Handler first: " + structureFirst + ")");
            }
            finally
            {
                new Harmony(terrainId).UnpatchSelf();
                new Harmony(structureId).UnpatchSelf();
            }
        }
        try
        {
            Harmony.CreateAndPatchAll(terrainPatch, terrainId);
            Check(PatchProcessor.GetCurrentInstructions(target).Any(i => i.operand is MethodInfo m && m.Name == "ActivateRestoredTerrain"),
                "interior restoration also installs without Structure Handler's patch");
        }
        finally { new Harmony(terrainId).UnpatchSelf(); }

        var logger = new ManualLogSource("Terrain compatibility checks");
        terrain.GetType("SilverpineMods.TerrainConstruction.Plugin", true).GetField("Log", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, logger);
        Type compatibility = terrain.GetType("SilverpineMods.TerrainConstruction.StructureHandlerCompatibility", true);
        compatibility.GetMethod("Initialize", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Type editor = structure.GetType("StructureHandler.StructureEditorUI", true);
        object EditorCall(string name, string value) => editor.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { value });
        string prefix = "prefab_tile_terrainconstruction_interior_";
        Check((string)EditorCall("CatalogDisplayName", prefix + "grass") == "Grass (Interior / Roofed)",
            "patched installed Structure Handler displays the roofed interior grass name");
        Check((string)EditorCall("CatalogDisplayName", prefix + "snowy_water_shallow") == "Frozen Shallow Water (Interior / Roofed)",
            "patched installed Structure Handler displays frozen interior names distinctly");
        Check(!(bool)EditorCall("IsUnsafeQuickPlaceName", prefix + "grass") &&
            (bool)EditorCall("IsUnsafeQuickPlaceName", "terrainconstruction_" + prefix + "grass"),
            "real interior prefab remains visible while its menu-only proxy is hidden");
        Check((bool)EditorCall("IsQuickPlaceSupportingTerrain", prefix + "grass") &&
            (bool)EditorCall("IsQuickPlaceSupportingTerrain", "terrainconstruction_snowy_prefab_tile_grass"),
            "installed editor treats real interior and snowy variants as supporting terrain");
        Check((string)EditorCall("CatalogDisplayName", "prefab_furniture_table") == "furniture table",
            "native Structure Handler labels stay unchanged");
        MethodInfo category = editor.GetMethod("GetQuickPlaceCategory", BindingFlags.Static | BindingFlags.NonPublic);
        Check(Harmony.GetPatchInfo(category).Postfixes.Any(p => p.PatchMethod.DeclaringType == compatibility),
            "Terrain-tab grouping hook is installed on the real editor");
        new Harmony("renegadex.silverpine.terrainconstruction.structure-handler-names").UnpatchSelf();
    }
}
