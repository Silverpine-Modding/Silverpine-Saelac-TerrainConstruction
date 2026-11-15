#nullable enable

using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Silverpine.ModdingTools;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilverpineMods.TerrainConstruction;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(Silverpine.ModdingTools.Plugin.PluginGuid, "1.12.0")]
[BepInDependency(StructureHandlerCompatibility.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "renegadex.silverpine.terrainconstruction";
    public const string PluginName = "Terrain Construction";
    public const string PluginVersion = "1.1.8";

    private const string CategoryId = PluginGuid + ".category.terrains";
    private const string ProxyPrefix = "terrainconstruction_";
    private const string SnowyPrefabPrefix = "terrainconstruction_snowy_";
    private const string WorldTeleporterPrefab = "prefab_tile_teleporter_world";
    private const string WoodBridgePrefab = "prefab_tile_wood_bridge";
    private const string RiverWaterPrefab = "prefab_tile_water";
    private const string SwampGrassPrefab = "prefab_tile_grass_swamp";

    private static readonly Dictionary<string, string> Labels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["prefab_tile_abyss_bricks"] = "Abyss Bricks",
            ["prefab_tile_abyss_snow"] = "Snowy Abyss",
            ["prefab_tile_dirt_dry"] = "Dry Dirt",
            ["prefab_tile_dirt_dry_roofed"] = "Roofed Dry Dirt",
            ["prefab_tile_grass"] = "Grass",
            ["prefab_tile_grass_swamp"] = "Swamp Grass",
            ["prefab_tile_icy_slip"] = "Slippery Ice",
            ["prefab_tile_icy_tiles"] = "Ice Tiles",
            ["prefab_tile_resin"] = "Resin",
            ["prefab_tile_rock_mountain"] = "Mountain Rock",
            ["prefab_tile_rock_overgrown"] = "Overgrown Rock",
            ["prefab_tile_rock_temple"] = "Temple Rock",
            ["prefab_tile_rock_temple_outside"] = "Outdoor Temple Rock",
            ["prefab_tile_sand_pebbles"] = "Pebbled Sand",
            ["prefab_tile_water_bathhouse"] = "Bathhouse Water",
            ["prefab_tile_water_lake"] = "Water",
            ["prefab_tile_water_shallow"] = "Shallow Water",
            ["prefab_tile_water_shallow_swamp"] = "Shallow Swamp Water",
            ["prefab_tile_water_swamp"] = "Swamp Water"
        };

    private static readonly string[] TerrainOrder =
    {
        "prefab_tile_abyss_bricks",
        "prefab_tile_abyss_snow",
        "prefab_tile_dirt_dry",
        "prefab_tile_dirt_dry_roofed",
        "prefab_tile_grass",
        "prefab_tile_grass_swamp",
        "prefab_tile_icy_slip",
        "prefab_tile_icy_tiles",
        "prefab_tile_resin",
        "prefab_tile_rock_mountain",
        "prefab_tile_rock_overgrown",
        "prefab_tile_rock_temple",
        "prefab_tile_rock_temple_outside",
        "prefab_tile_sand_pebbles",
        "prefab_tile_water_bathhouse",
        "prefab_tile_water_lake",
        "prefab_tile_water_shallow",
        "prefab_tile_water_shallow_swamp",
        "prefab_tile_water_swamp"
    };

    private static readonly Dictionary<string, TerrainEntry> TerrainEntries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> constructionIds = new();
    private readonly List<SerializablePrefabRegistration> prefabRegistrations = new();
    private ConstructionCategoryRegistration? categoryRegistration;

    internal static ManualLogSource Log = null!;

    private void Awake()
    {
        Log = Logger;

        try
        {
            Harmony.CreateAndPatchAll(
                typeof(ForcedWinterSeasonPatch),
                PluginGuid + ".forced-winter");
            Harmony.CreateAndPatchAll(typeof(FloorTerrainSelectionPatch), PluginGuid + ".terrain-layers");
            Harmony.CreateAndPatchAll(typeof(FloorWaterValidationPatch), PluginGuid + ".terrain-layers");
            Harmony.CreateAndPatchAll(typeof(InteriorFloorRestorePatch), PluginGuid + ".terrain-layers");
            Harmony.CreateAndPatchAll(typeof(TerrainAnimationTickPatch), PluginGuid + ".terrain-animation");
            Harmony.CreateAndPatchAll(typeof(TerrainAnimationResetPatch), PluginGuid + ".terrain-animation");
            RegisterTerrainConstructions();
            StructureHandlerCompatibility.Initialize();
            Logger.LogInfo(
                $"Registered the Terrains construction category with " +
                $"{TerrainEntries.Count} placeable terrain variants.");
        }
        catch (Exception exception)
        {
            Logger.LogError(
                "Terrain construction registration failed; rolling back. " +
                exception);
            new Harmony(PluginGuid + ".forced-winter").UnpatchSelf();
            new Harmony(PluginGuid + ".terrain-layers").UnpatchSelf();
            new Harmony(PluginGuid + ".terrain-animation").UnpatchSelf();
            new Harmony(StructureHandlerCompatibility.PatchId).UnpatchSelf();
            RollBackRegistrationFailure();
            throw;
        }
    }

    private void RegisterTerrainConstructions()
    {
        categoryRegistration = ConstructionMenu.RegisterCategory(
            PluginGuid,
            new ConstructionCategoryDefinition
            {
                Id = CategoryId,
                Label = "Terrains",
                Order = 30,
                ShowWhenEmpty = false
            });

        List<string> prefabNames = SerializationManager.GetExistingPrefabNames()
            .Where(IsTerrainMenuPrefab)
            .OrderBy(GetOrder)
            .ThenBy(GetLabel, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (prefabNames.Count == 0)
            throw new InvalidOperationException(
                "Silverpine's serialized prefab registry contains no terrain tiles.");

        foreach (string prefabName in prefabNames)
        {
            RegisterTerrain(prefabName);
            TryRegisterSnowyTerrain(prefabName);
        }
    }

    internal static bool IsTerrainMenuPrefab(string name) =>
        name.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, WorldTeleporterPrefab, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, WoodBridgePrefab, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(name, RiverWaterPrefab, StringComparison.OrdinalIgnoreCase) &&
        // Keep the native floor prefabs and replacement/overlay handling intact;
        // only omit duplicate choices already offered by normal construction.
        !TerrainLayers.IsNativeFloor(name);

    private void RegisterTerrain(string sourcePrefabName)
    {
        GameObject source = SerializationManager.GetPrefabFromName(sourcePrefabName) ??
            throw new InvalidOperationException(
                $"Terrain prefab '{sourcePrefabName}' was listed but could not be loaded.");
        string label = GetLabel(sourcePrefabName);
        Sprite preview = PrefabPreviewGenerator.GeneratePreview(source);
        if (TerrainLayers.IsOverlayName(sourcePrefabName))
        {
            string overlayName = TerrainLayers.GetOverlayPrefabName(sourcePrefabName);
            GameObject template = CloneInactiveTerrain(source, overlayName);
            try
            {
                TerrainOverlay.ConfigureTemplate(template);
                prefabRegistrations.Add(SerializablePrefabs.Register(PluginGuid, overlayName, template));
                source = template;
            }
            catch
            {
                Destroy(template);
                throw;
            }
        }
        string proxyName = ProxyPrefix + sourcePrefabName;
        string constructionId = PluginGuid + "." + sourcePrefabName;
        GameObject proxy = CreateProxy(proxyName, label, preview);

        SerializablePrefabRegistration proxyRegistration;
        try
        {
            proxyRegistration = SerializablePrefabs.Register(
                PluginGuid,
                proxyName,
                proxy,
                new SerializablePrefabOptions
                {
                    RequireNpcVisibleObject = true
                });
        }
        catch
        {
            Destroy(proxy);
            throw;
        }

        try
        {
            var entry = new TerrainEntry(
                sourcePrefabName,
                source,
                preview);
            TerrainEntries.Add(proxyName, entry);
            ConstructionMenu.Register(
                PluginGuid,
                new ConstructionDefinition
                {
                    Id = constructionId,
                    PrefabName = proxyName,
                    Label = label,
                    Category = CategoryId,
                    Order = GetOrder(sourcePrefabName),
                    WorkMinutes = 0,
                    Mode = ConstructionMode.Immediate,
                    PlayerPropertyOnly = false,
                    RequiresHammer = false,
                    Preview = preview,
                    StartPlacement = () => TerrainPlacement.Start(entry)
                });
            constructionIds.Add(constructionId);
            prefabRegistrations.Add(proxyRegistration);
        }
        catch
        {
            TerrainEntries.Remove(proxyName);
            proxyRegistration.Unregister(destroyTemplate: true);
            throw;
        }

        TryRegisterInteriorTerrain(sourcePrefabName, source, label, preview, GetOrder(sourcePrefabName));
    }

    private void TryRegisterSnowyTerrain(string sourcePrefabName)
    {
        if (sourcePrefabName.IndexOf("snow", StringComparison.OrdinalIgnoreCase) >= 0 ||
            sourcePrefabName.IndexOf("icy", StringComparison.OrdinalIgnoreCase) >= 0 ||
            string.Equals(
                sourcePrefabName,
                SwampGrassPrefab,
                StringComparison.OrdinalIgnoreCase))
            return;

        GameObject source = SerializationManager.GetPrefabFromName(sourcePrefabName)!;
        Sprite? winterPreview = TryGetWinterPreview(source);
        if (winterPreview == null)
            return;

        string snowyPrefabName = SnowyPrefabPrefix + sourcePrefabName;
        GameObject snowyTemplate = CreateSnowyTemplate(
            source,
            snowyPrefabName);
        SerializablePrefabRegistration snowyRegistration;
        try
        {
            if (TerrainLayers.IsOverlayName(sourcePrefabName))
                TerrainOverlay.ConfigureTemplate(snowyTemplate);
            snowyRegistration = SerializablePrefabs.Register(
                PluginGuid,
                snowyPrefabName,
                snowyTemplate);
            prefabRegistrations.Add(snowyRegistration);
        }
        catch
        {
            Destroy(snowyTemplate);
            throw;
        }

        string label = GetSnowyLabel(sourcePrefabName);
        string proxyName = ProxyPrefix + snowyPrefabName;
        string constructionId = PluginGuid + ".snowy." + sourcePrefabName;
        GameObject proxy = CreateProxy(proxyName, label, winterPreview);
        SerializablePrefabRegistration proxyRegistration;
        try
        {
            proxyRegistration = SerializablePrefabs.Register(
                PluginGuid,
                proxyName,
                proxy,
                new SerializablePrefabOptions
                {
                    RequireNpcVisibleObject = true
                });
            prefabRegistrations.Add(proxyRegistration);
        }
        catch
        {
            Destroy(proxy);
            throw;
        }

        var entry = new TerrainEntry(
            snowyPrefabName,
            snowyTemplate,
            winterPreview);
        TerrainEntries.Add(proxyName, entry);
        ConstructionMenu.Register(
            PluginGuid,
            new ConstructionDefinition
            {
                Id = constructionId,
                PrefabName = proxyName,
                Label = label,
                Category = CategoryId,
                Order = GetOrder(sourcePrefabName) + 1000,
                WorkMinutes = 0,
                Mode = ConstructionMode.Immediate,
                PlayerPropertyOnly = false,
                RequiresHammer = false,
                Preview = winterPreview,
                StartPlacement = () => TerrainPlacement.Start(entry)
            });
        constructionIds.Add(constructionId);

        TryRegisterInteriorTerrain(snowyPrefabName, snowyTemplate, label, winterPreview, GetOrder(sourcePrefabName) + 1000);
    }

    private void TryRegisterInteriorTerrain(string sourceName, GameObject source, string label, Sprite preview, int order)
    {
        if (!TerrainInteriors.ShouldCreateVariant(sourceName, TerrainInteriors.HasRoof(source)))
            return;

        string interiorName = TerrainInteriors.GetPrefabName(sourceName);
        GameObject template = CloneInactiveTerrain(source, interiorName);
        try
        {
            SpriteMask nativeMask = SerializationManager.GetPrefabFromName("prefab_tile_rock").GetComponent<SpriteMask>();
            TerrainInteriors.ConfigureTemplate(template, nativeMask);
            prefabRegistrations.Add(SerializablePrefabs.Register(PluginGuid, interiorName, template));
        }
        catch
        {
            Destroy(template);
            throw;
        }

        string interiorLabel = TerrainDisplayNames.InteriorLabel(label);
        string proxyName = ProxyPrefix + interiorName;
        string constructionId = PluginGuid + ".interior." + sourceName;
        GameObject proxy = CreateProxy(proxyName, interiorLabel, preview);
        try
        {
            prefabRegistrations.Add(SerializablePrefabs.Register(PluginGuid, proxyName, proxy,
                new SerializablePrefabOptions { RequireNpcVisibleObject = true }));
        }
        catch
        {
            Destroy(proxy);
            throw;
        }

        var entry = new TerrainEntry(interiorName, template, preview);
        TerrainEntries.Add(proxyName, entry);
        ConstructionMenu.Register(PluginGuid, new ConstructionDefinition
        {
            Id = constructionId,
            PrefabName = proxyName,
            Label = interiorLabel,
            Category = CategoryId,
            Order = order + 2000,
            WorkMinutes = 0,
            Mode = ConstructionMode.Immediate,
            PlayerPropertyOnly = false,
            RequiresHammer = false,
            Preview = preview,
            StartPlacement = () => TerrainPlacement.Start(entry)
        });
        constructionIds.Add(constructionId);
    }

    private static GameObject CreateSnowyTemplate(
        GameObject source,
        string prefabName)
    {
        GameObject template = CloneInactiveTerrain(source, prefabName);
        template.AddComponent<ForcedWinterTerrain>();
        return template;
    }

    private static GameObject CloneInactiveTerrain(GameObject source, string prefabName)
    {
        // Instantiating at a remote coordinate prevents Awake-time tile
        // registries from touching a real map cell. ITileRegistered components
        // are explicitly removed from those registries before the template is
        // retained by Modding Tools.
        GameObject template = UnityEngine.Object.Instantiate(
            source,
            new Vector3(100000f, 100000f, source.transform.position.z),
            source.transform.rotation);
        template.name = prefabName;
        foreach (ITileRegistered registered in
                 template.GetComponentsInChildren<ITileRegistered>(true))
            registered.Unregister();
        template.SetActive(false);
        return template;
    }

    private static Sprite? TryGetWinterPreview(GameObject source)
    {
        foreach (SeasonRandomSprite component in
                 source.GetComponentsInChildren<SeasonRandomSprite>(true))
        {
            var sprites = (List<Sprite>?)AccessTools.Field(
                    typeof(SeasonRandomSprite),
                    "winterSprites")
                ?.GetValue(component);
            Sprite? sprite = sprites?.FirstOrDefault(value => value != null);
            if (sprite != null)
                return sprite;
        }

        foreach (SeasonSprite component in
                 source.GetComponentsInChildren<SeasonSprite>(true))
        {
            Sprite? sprite = (Sprite?)AccessTools.Field(
                    typeof(SeasonSprite),
                    "winterSprite")
                ?.GetValue(component);
            if (sprite != null)
                return sprite;
        }

        foreach (SeasonWater component in
                 source.GetComponentsInChildren<SeasonWater>(true))
        {
            Sprite? sprite = (Sprite?)AccessTools.Field(
                    typeof(SeasonWater),
                    "winterSprite")
                ?.GetValue(component);
            if (sprite != null)
                return sprite;
        }

        return null;
    }

    private static GameObject CreateProxy(
        string proxyName,
        string label,
        Sprite preview)
    {
        var proxy = new GameObject(proxyName);
        proxy.SetActive(false);
        proxy.AddComponent<TerrainProxyDescription>().Description = label;
        proxy.AddComponent<SpriteRenderer>().sprite = preview;
        return proxy;
    }

    private void RollBackRegistrationFailure()
    {
        for (int index = constructionIds.Count - 1; index >= 0; index--)
            ConstructionMenu.Unregister(PluginGuid, constructionIds[index]);
        constructionIds.Clear();

        TerrainEntries.Clear();
        for (int index = prefabRegistrations.Count - 1; index >= 0; index--)
            prefabRegistrations[index].Unregister(destroyTemplate: true);
        prefabRegistrations.Clear();

        categoryRegistration?.Unregister();
        categoryRegistration = null;
    }

    internal static bool IsReplaceableTerrain(GameObject gameObject)
    {
        if (gameObject == null)
            return false;
        string name = SerializationManager.GetPrefabName(gameObject);
        return (name.StartsWith("prefab_tile_", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    name,
                    WorldTeleporterPrefab,
                    StringComparison.OrdinalIgnoreCase)) ||
            gameObject.GetComponent<ForcedWinterTerrain>() != null;
    }

    private static int GetOrder(string prefabName)
    {
        int index = Array.FindIndex(
            TerrainOrder,
            name => string.Equals(
                name,
                prefabName,
                StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? index : TerrainOrder.Length + 100;
    }

    internal static string GetLabel(string prefabName)
    {
        if (Labels.TryGetValue(prefabName, out string label))
            return label;

        string value = prefabName.StartsWith(
                "prefab_tile_",
                StringComparison.OrdinalIgnoreCase)
            ? prefabName.Substring("prefab_tile_".Length)
            : prefabName;
        return string.Join(
            " ",
            value.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(word => char.ToUpperInvariant(word[0]) + word.Substring(1)));
    }

    internal static string GetSnowyLabel(string prefabName)
    {
        string label = GetLabel(prefabName);
        if (string.Equals(
                prefabName,
                "prefab_tile_grass",
                StringComparison.OrdinalIgnoreCase))
            return "Snow";
        if (prefabName.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0)
            return "Frozen " + label;
        return "Snowy " + label;
    }

}

internal sealed class TerrainEntry
{
    internal TerrainEntry(
        string sourcePrefabName,
        GameObject source,
        Sprite preview)
    {
        SourcePrefabName = sourcePrefabName;
        Source = source;
        Preview = preview;
    }

    internal string SourcePrefabName { get; }
    internal GameObject Source { get; }
    internal Sprite Preview { get; }
    internal bool IsOverlay => TerrainLayers.IsOverlayName(SourcePrefabName);
}

internal sealed class TerrainProxyDescription : MonoBehaviour, INPCVisibleObject
{
    internal string Description { get; set; } = "terrain";

    public string GetNPCVisibleObjectDescription() => Description;
}

internal static class TerrainPlacement
{
    internal static void Start(TerrainEntry entry)
    {
        if (GenericListUI.Instance != null)
            GenericListUI.Instance.Close();
        ConstructionPlacement.StartGrid(
            new GridConstructionPlacement
            {
                Preview = entry.Preview,
                RequiresLineOfSight = false,
                IsValidTarget = tile => IsValidTarget(entry, tile),
                Place = tile => PlaceTerrain(entry, tile),
                InvalidTargetMessage =
                    entry.IsOverlay ? "Place a ground tile beneath this overlay first." :
                    "Choose an existing terrain tile to replace."
            });
    }

    private static bool IsValidTarget(TerrainEntry entry, Vector2Int tile) =>
        Turfs.GetTurfGameObjects(tile).Any(item => Plugin.IsReplaceableTerrain(item) &&
            (!entry.IsOverlay || !TerrainLayers.IsOverlay(item)));

    private static void PlaceTerrain(
        TerrainEntry entry,
        Vector2Int tile)
    {
        if (!IsValidTarget(entry, tile))
        {
            UpperNotificationUI.Instance?.OneOff(
                entry.IsOverlay ? "Place a ground tile beneath this overlay first." :
                "Choose an existing terrain tile to replace.");
            return;
        }

        // Replace only the chosen layer. The base is a real, independently
        // saved terrain object, not a lossy prefab-name snapshot of the floor.
        GameObject[] replaced = Turfs.GetTurfGameObjects(tile)
            .Where(item => Plugin.IsReplaceableTerrain(item) &&
                TerrainLayers.IsOverlay(item) == entry.IsOverlay)
            .ToArray();

        foreach (GameObject oldTerrain in replaced)
            UnityEngine.Object.DestroyImmediate(oldTerrain);

        GameObject placed = UnityEngine.Object.Instantiate(
            entry.Source,
            new Vector3(tile.x, tile.y, entry.Source.transform.position.z),
            entry.Source.transform.rotation);
        placed.hideFlags = HideFlags.None;
        if (!placed.activeSelf)
            placed.SetActive(true);
        if (placed.TryGetComponent<TurfRegistrar>(out var registrar))
            registrar.Register();

        PlayerAbility_Construct.DestroyPlantAtPosition(tile);
        RefreshPlacedTileVisuals(tile);
        if (ActionQueue.Instance != null && WorldInfoManager.Instance != null)
            ActionQueue.Instance.DoAfterXFrames(
                1,
                WorldInfoManager.Instance.CheckDarknessMode);
    }

    private static void RefreshPlacedTileVisuals(Vector2Int tile)
    {
        try
        {
            // Dirt's AllowOvergrowth tag is registered during Awake. Refresh
            // already-initialized neighbors now, as the native shovel does,
            // instead of making those edges depend solely on the queued pass.
            ModTileVisuals.Refresh(tile, includeNeighbors: true);
        }
        catch (Exception exception)
        {
            FrameworkDiagnostics.Report(Plugin.PluginGuid, "refresh existing terrain neighbors", exception);
            Plugin.Log.LogWarning("Immediate terrain refresh failed: " + exception.Message);
        }
        // New seasonal/edge components still need their Start methods to finish.
        RequestTileVisualRefresh(tile);
    }

    internal static void RequestTileVisualRefresh(Vector2Int tile)
    {
        try
        {
            // Includes water corners and waits for native Start/registration.
            ModTileVisuals.RequestRefresh(tile, includeNeighbors: true);
        }
        catch (Exception exception)
        {
            // Placement has already succeeded; a visual-service failure must
            // not escape into the placement callback or obscure that result.
            FrameworkDiagnostics.Report(Plugin.PluginGuid, "refresh placed terrain", exception);
            Plugin.Log.LogWarning("Deferred terrain refresh failed: " + exception.Message);
        }
    }
}

[HarmonyPatch(
    typeof(WorldInfoManager),
    nameof(WorldInfoManager.GetSeasonFor))]
internal static class ForcedWinterSeasonPatch
{
    private static void Postfix(GameObject g, ref Season __result)
    {
        if (g != null &&
            g.GetComponentInParent<ForcedWinterTerrain>() != null)
            __result = Season.Winter;
    }
}

internal sealed class ForcedWinterTerrain : MonoBehaviour
{
}
