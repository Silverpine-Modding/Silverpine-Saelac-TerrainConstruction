using SilverpineMods.TerrainConstruction;
using UnityEngine;

int passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    passed++; Console.WriteLine("PASS " + message);
}
GameObject Template(string name)
{
    var item = new GameObject(name) { layer = 7 };
    item.SetActive(false);
    return item;
}
bool Rejects(Action action)
{
    try { action(); return false; }
    catch (InvalidOperationException) { return true; }
}
var donor = new GameObject("native floor");
var native = donor.AddComponent<SpriteMask>();
native.sprite = new Sprite(); native.alphaCutoff = 0.2f;
native.frontSortingLayerID = 12; native.frontSortingOrder = 34;
native.backSortingLayerID = 56; native.backSortingOrder = 78;
native.spriteSortPoint = SpriteSortPoint.Pivot; native.isCustomRangeActive = true;

foreach (string kind in new[] { "grass", "deep water", "shallow water", "bathhouse water", "snow", "abyss" })
{
    GameObject tile = Template(kind);
    TileTags? original = null;
    SpriteMask? originalMask = null;
    if (kind is "shallow water" or "bathhouse water")
        (original = tile.AddComponent<TileTags>()).tags = new List<TileTag> { TileTag.Submerge };
    if (kind == "bathhouse water") originalMask = tile.AddComponent<SpriteMask>();
    TerrainInteriors.ConfigureTemplate(tile, native);
    Check(!tile.activeSelf && tile.GetComponent<InteriorTerrain>() is ISerializableMonoBehavior,
        kind + " template remains inactive and has native serialization support");
    Check(tile.transform.Children.Count == 1 && tile.transform.Children[0].WorldPositionStays == false,
        kind + " roof is an attached tile-local child");
    GameObject roof = tile.transform.Children.Single().Owner;
    Check(roof.activeSelf && !roof.activeInHierarchy && roof.layer == tile.layer,
        kind + " roof waits for parent activation and inherits its rendering layer");
    Check(roof.GetComponent<TileTags>().tags.SequenceEqual(new[] { TileTag.Roofed }) && TerrainInteriors.HasRoof(tile),
        kind + " gets only the Roofed tag on the child");
    Check(ReferenceEquals(tile.GetComponent<TileTags>(), original) &&
        (original == null || original.tags.SequenceEqual(new[] { TileTag.Submerge })),
        kind + " keeps root water/seasonal tags unchanged");
    SpriteMask mask = roof.GetComponent<SpriteMask>();
    Check(mask.sprite == native.sprite && mask.alphaCutoff == native.alphaCutoff && mask.enabled &&
        mask.isCustomRangeActive == native.isCustomRangeActive && mask.frontSortingLayerID == native.frontSortingLayerID &&
        mask.frontSortingOrder == native.frontSortingOrder && mask.backSortingLayerID == native.backSortingLayerID &&
        mask.backSortingOrder == native.backSortingOrder && mask.spriteSortPoint == native.spriteSortPoint &&
        ReferenceEquals(tile.GetComponent<SpriteMask>(), originalMask),
        kind + " copies the native mask without modifying any original mask");
    using var bytes = new MemoryStream();
    tile.GetComponent<InteriorTerrain>().Serialize(new BinaryWriter(bytes));
    tile.GetComponent<InteriorTerrain>().Deserialize(new BinaryReader(bytes));
    Check(bytes.Length == 0, kind + " roof persistence adds no mutable serialized state");
    Check(tile.GetComponent<INPCVisiblePrefixProvider>().GetPrefix() == "roofed interior",
        kind + " exposes a roofed interior description prefix to native naming systems");
    Check(Rejects(() => TerrainInteriors.ConfigureTemplate(tile, native)) && tile.transform.Children.Count == 1,
        kind + " rejects duplicate roof configuration");
}
var active = new GameObject("active");
Check(Rejects(() => TerrainInteriors.ConfigureTemplate(active, native)) && active.Components.Count == 0,
    "active objects are rejected before adding components");
var invalid = Template("invalid");
Check(Rejects(() => TerrainInteriors.ConfigureTemplate(invalid, null!)) && invalid.Components.Count == 0,
    "missing native mask fails before changing the template");
var emptyMask = new GameObject("missing sprite").AddComponent<SpriteMask>();
Check(Rejects(() => TerrainInteriors.ConfigureTemplate(invalid, emptyMask)) && invalid.Components.Count == 0,
    "missing native mask sprite fails before changing the template");
native.enabled = false;
Check(Rejects(() => TerrainInteriors.ConfigureTemplate(invalid, native)) && invalid.Components.Count == 0,
    "disabled native weather mask fails before changing the template");
Console.WriteLine($"{passed} template checks passed. Unity rendering and save/load require in-game verification.");
