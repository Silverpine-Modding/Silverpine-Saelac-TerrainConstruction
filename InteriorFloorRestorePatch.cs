#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SilverpineMods.TerrainConstruction;

// Native floors remember the previous terrain prefab. Their deconstruction
// instantiates it directly, outside Modding Tools' save-load activation hook.
[HarmonyPatch(typeof(GrassTileHandler), nameof(GrassTileHandler.OnDeconstructed))]
internal static class InteriorFloorRestorePatch
{
    [HarmonyAfter("renegadex.silverpine.customstructures.restored-ground")]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        List<CodeInstruction> result = instructions.ToList();
        List<CodeInstruction> matches = result.Where(i => i.opcode == OpCodes.Call && i.operand is MethodInfo method &&
            method.ReturnType == typeof(GameObject) &&
            ((method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate" &&
              method.IsGenericMethod && method.GetGenericArguments().SequenceEqual(new[] { typeof(GameObject) })) ||
             (method.DeclaringType?.FullName == "StructureHandler.DestinationGround" && method.Name == "InstantiateRestoredGround")) &&
            method.GetParameters().Select(p => p.ParameterType)
                .SequenceEqual(new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) })).ToList();
        if (matches.Count != 1)
            throw new InvalidOperationException("Could not locate the native floor's terrain restoration.");
        // Keep the original call (including Structure Handler's persistence
        // bookkeeping), then activate only our returned interior instance.
        result.Insert(result.IndexOf(matches[0]) + 1, new CodeInstruction(OpCodes.Call,
            typeof(InteriorFloorRestorePatch).GetMethod(nameof(ActivateRestoredTerrain), BindingFlags.NonPublic | BindingFlags.Static)!));
        return result;
    }

    private static GameObject ActivateRestoredTerrain(GameObject placed)
    {
        if (placed.GetComponent<InteriorTerrain>() != null)
        {
            placed.hideFlags = HideFlags.None;
            placed.SetActive(true);
            // Start registers if needed. Structure Handler may have registered
            // it already, so do not call Register a second time here.
            TerrainPlacement.RequestTileVisualRefresh(placed.transform.GetVector2IntPosition());
        }
        return placed;
    }
}
