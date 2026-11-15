// Minimal test host for the real template configurator. Does not simulate
// Unity rendering, serialization, physics or component lifecycle callbacks.
namespace UnityEngine
{
    public class Component
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
    }
    public class MonoBehaviour : Component { }
    public class GameObject
    {
        public GameObject(string name) { this.name = name; transform = new Transform(this); }
        public string name;
        public int layer;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.Parent?.Owner.activeInHierarchy ?? true);
        public Transform transform;
        public List<Component> Components = new();
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component, new()
        {
            var component = new T { gameObject = this };
            Components.Add(component);
            return component;
        }
        public T GetComponent<T>() => Components.OfType<T>().FirstOrDefault()!;
        public T[] GetComponentsInChildren<T>(bool includeInactive) =>
            (includeInactive || activeInHierarchy ? Components.OfType<T>() : Array.Empty<T>())
                .Concat(transform.Children.SelectMany(c => c.Owner.GetComponentsInChildren<T>(includeInactive))).ToArray();
    }
    public class Transform
    {
        public Transform(GameObject owner) { Owner = owner; }
        public GameObject Owner;
        public Transform? Parent;
        public List<Transform> Children = new();
        public bool? WorldPositionStays;
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            Parent = parent; parent.Children.Add(this); WorldPositionStays = worldPositionStays;
        }
    }
    public class Sprite { }
    public enum SpriteSortPoint { Center, Pivot }
    public class SpriteMask : Component
    {
        public Sprite sprite = null!;
        public float alphaCutoff;
        public bool isCustomRangeActive;
        public int frontSortingLayerID, frontSortingOrder, backSortingLayerID, backSortingOrder;
        public SpriteSortPoint spriteSortPoint;
        public bool enabled = true;
    }
}
public enum TileTag { Roofed, AllowOvergrowth, Submerge }
public interface INPCVisiblePrefixProvider { string GetPrefix(); }
public class TileTags : UnityEngine.MonoBehaviour { public List<TileTag> tags = null!; }
public interface ISerializableMonoBehavior
{
    void Serialize(BinaryWriter writer);
    void Deserialize(BinaryReader reader);
}
namespace SilverpineMods.TerrainConstruction
{
    // Naming/layer integration is separately tested against the built plugin
    // in TerrainChecks. Only these pure dependencies are substituted here.
    internal static class TerrainLayers
    {
        internal static string GetNativeName(string name) => name;
        internal static bool IsNativeFloor(string name) => name is "prefab_tile_rock" or "prefab_tile_wood";
    }
}
