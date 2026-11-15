#nullable enable
using System;
using System.Reflection;
using HarmonyLib;

namespace SilverpineMods.TerrainConstruction;

// Optional integration kept in this plugin: no Structure Handler binary or
// source changes, and no changes to persisted prefab names/structure files.
internal static class StructureHandlerCompatibility
{
    internal const string PluginGuid = "renegadex.silverpine.customstructures";
    internal const string PatchId = Plugin.PluginGuid + ".structure-handler-names";

    internal static void Initialize()
    {
        Type? editor = AccessTools.TypeByName("StructureHandler.StructureEditorUI");
        if (editor == null) return;
        var harmony = new Harmony(PatchId);
        try
        {
            Patch("CatalogDisplayName", typeof(string), nameof(DisplayName));
            Patch("GetQuickPlaceCategory", typeof(int), nameof(Category));
            Patch("IsUnsafeQuickPlaceName", typeof(bool), nameof(HideProxy));
            Patch("IsQuickPlaceSupportingTerrain", typeof(bool), nameof(SupportingTerrain));
            Plugin.Log.LogInfo("Structure Handler: named terrain variants are available under Terrain; search Interior or Roofed. Menu-only proxies are hidden.");
        }
        catch (Exception exception)
        {
            harmony.UnpatchSelf();
            Plugin.Log.LogWarning("Optional Structure Handler naming integration could not be installed: " + exception.Message);
        }

        void Patch(string name, Type returnType, string callback)
        {
            MethodInfo? target = AccessTools.Method(editor, name, new[] { typeof(string) });
            if (target == null || !target.IsStatic || target.ReturnType != returnType)
                throw new MissingMethodException(editor.FullName, name);
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(StructureHandlerCompatibility), callback));
        }
    }

    private static void DisplayName(string __0, ref string __result)
    {
        if (TerrainDisplayNames.TryGetName(__0, out string label)) __result = label;
    }

    private static void Category(string __0, ref int __result)
    {
        if (TerrainDisplayNames.IsManagedTerrainName(__0)) __result = 0; // Terrain tab
    }

    private static void HideProxy(string __0, ref bool __result)
    {
        if (TerrainDisplayNames.IsMenuProxyName(__0)) __result = true;
    }

    private static void SupportingTerrain(string __0, ref bool __result)
    {
        if (TerrainDisplayNames.IsManagedTerrainName(__0)) __result = true;
    }
}
