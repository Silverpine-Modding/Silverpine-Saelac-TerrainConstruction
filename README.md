# Terrain Construction

Adds a **Terrains** category to the Modding Tools construction window. The
category discovers Silverpine's serialized `prefab_tile_*` resources and makes
visually distinct terrain/floor tiles not already offered by base-game construction
placeable at no material or hammer cost. Rock Floor and Wood Floor are omitted;
use the normal construction entries for those. Existing floors placed by earlier
versions remain intact and can still be replaced with terrain or used beneath
overlays. The redundant Wood Bridge entry is omitted because it looks identical to
Wood Floor. The river-specific water prefab is likewise omitted; its visually
and mechanically identical lake prefab is presented as **Water** instead.
The generated Snowy Swamp Grass entry is also omitted because its winter
sprites, tint, sounds, and description are identical to regular **Snow**.

Unroofed entries also have an **(Interior / Roofed)** variant, including the snowy/frozen
variants. These retain the original tile appearance and behavior while adding
the native **Roofed** tag and the full-tile weather mask used by normal floors.
Rain, snow, clouds and the native fog overlay use that mask; roof-aware gameplay
and weather audio use the tag. Dry Dirt uses the existing **Roofed Dry Dirt**
instead of adding a duplicate, and already-roofed tiles are not duplicated.
The roof tag lives on a child, so it does not change water's native seasonal tags.
These variants do not create named rooms or override the biome/season: Snow
remains snowy, ordinary grass can still change with the season, and water still
freezes/thaws normally. Global weather and ordinary outdoor tiles are unchanged.

Interior overlays remain removable without changing the floor underneath. The
roof belongs to its tile: replacing an interior base with an outdoor base, or
removing an interior overlay, removes that tile's roof too. Another roofed tile
at the same location can still provide shelter. Normal floor construction over
an interior base remembers its registered prefab; deconstruction reactivates
and restores that interior tile. Interior names and components persist through
the normal save system. Keep this plugin installed when loading saves containing
interior tiles (or replace them with native terrain before uninstalling).

Structure Handler integration gives these prefabs the same readable labels in
its quick-place catalog, brush status and placement messages, for example
**Grass (Interior / Roofed)** and **Frozen Shallow Water (Interior / Roofed)**.
Open the **Terrain** tab and search **Interior** or **Roofed**. Real mod terrain
is grouped there even when its name contains "grass" or "rock"; construction-menu
proxy objects are hidden so they cannot be mistaken for placeable terrain.
Native descriptive-name consumers also receive a **roofed interior** prefix,
including the seasonal name of the surface. Persisted prefab IDs are unchanged;
raw IDs in structure files still contain `terrainconstruction_interior_`.
Structure Handler is optional and no changes to its files are required.

Version 1.1.8 also fixes 1.1.7's startup conflict with Structure Handler's floor
restoration patch. Both mods now keep Structure Handler's persistence handling
and activate the restored interior afterwards; registration is left to native
Start if Structure Handler has not already done it.

Selecting a base terrain replaces the existing ground tile. This also permits
repainting deep water, abyss, and other impassable terrain. The placement ghost is owned by Modding Tools and remains
locked to the same integer grid as normal floor construction.

Bathhouse Water, Shallow Water, Shallow Swamp Water, and Slippery Ice instead
occupy an **overlay layer**, matching their native transparent renderers. Frozen
shallow-water variants use that layer too. Placing an overlay preserves the real
base object and replaces only another overlay; placing any base terrain changes
the ground **under** the overlay. The native opacity, tint, sorting, animation,
seasonal behavior, soaking, slipping, and bathhouse effects are retained.

Matching native and mod-placed water animations share Unity's physics clock, so
adjacent tiles no longer start their animated surface at different times. This
includes existing river/lake/swamp water and newly placed or loaded tiles;
no tile replacement or save migration is needed. Slippery Ice follows the same
clock with its own native frame sequence and loop pause. The original frames,
frame cadence, opacity and sorting are preserved. Frozen/disabled animations
remain disabled, thawed water rejoins the current phase, and unrelated effects
and character animations are not changed. Synchronization is independent of
the Modding Tools shoreline/grass refresh service.

Mod-placed overlays can be removed with the normal **Deconstruct** ability (or
their Deconstruct interaction), without removing/recreating the floor beneath
them or refunding materials. They are registered serialized prefabs, so both
layers follow the game's normal persistence rules. Normal wood/rock floor
construction can also replace the base beneath shallow/bathhouse water; its
material, hammer, ownership, and other non-water restrictions remain unchanged.
Furniture still follows its native water restrictions.

After placement, Modding Tools immediately refreshes existing native grass
blending and water/abyss edges on the changed tile and its eight neighbors,
then queues a second pass after new tiles finish initializing. Overlay removal
queues the same refresh after destruction. Dry Dirt keeps its native
AllowOvergrowth tag; this does not invent blends between arbitrary floor types.

Every included base terrain can replace every other base terrain within placement
range, including abyss with grass and deep water with dry ground. Target
validation does not require passable ground or line of sight. The old base objects and their
colliders are removed before the chosen prefab is instantiated; no construction
site or construction/deconstruction callbacks are used during terrain placement.
The chosen prefab keeps its native components and behavior. Previously placed
wood/rock floors retain their native deconstruction behavior.

Terrain with a real winter presentation also receives a persistent snowy or
frozen variant. These variants mark cloned Silverpine tiles as forced-winter,
so their existing seasonal sprite, sound, footprint, passability, and tile-tag
components behave the same way they do in the High Mountains. Normal terrain
retains its original Silverpine prefab name and behavior except removable
overlays; overlays and snowy variants use plugin-owned serialized prefab names
so the added behavior survives save/load. Existing frozen prefab names are kept.

Old mod placements that already deleted the base cannot recover that lost floor
automatically. Place the desired base tile underneath them. Re-place an old
native-named water/ice overlay to give it the new Deconstruct action.

`prefab_tile_teleporter_world` is intentionally excluded. It is the invisible
world-edge transition trigger rather than a terrain surface, and placing it in
the world would create unintended map transitions.

## Requirements

- Silverpine 1.7.3
- BepInEx 5
- Modding Tools 1.12.0 or newer

## Use

Open the player's **Construct** ability, select **Terrains**, choose a tile, and
click an existing ground tile within placement range. Terrain placement is
immediate and free.

## Verification

Build the Release configuration, then run `Tests/TerrainChecks.csproj` with the
game directory as its argument. The checks cover menu exclusions against the
installed game's construction list, layer classification, saved
prefab names, both patches against the installed game's IL, refresh calls,
deconstruction/serialization wiring, and plugin lifecycle.
Animation checks also compare the shared clock against native fixed-update
timing, test late joins, and validate the hooks against the installed assembly.
Run `Tests/InteriorTemplates/InteriorTemplateChecks.csproj` as well to exercise
the real interior template configurator against a minimal component test host.
Interior checks cover prefab-name round trips, overlay/animation classification,
duplicate avoidance, native mask settings, unchanged water tags, serialization
and floor-deconstruction restoration. The test host does not simulate Unity.

With Structure Handler installed, build `Tests/RuntimeCompatibility/RuntimeCompatibility.csproj`
and `Tests/RuntimeCompatibility/Host/Host.csproj` in Release, then run the host DLL
with the game directory and the compatibility test EXE as arguments. This starts
the game's Mono in a disposable process, applies both real restoration patches
in both load orders, and checks the installed editor's patched labels/filters.
No game or save is opened. Engine-internal-call warnings can appear in this
standalone host; they do not substitute for in-game rendering/placement tests.

In game, confirm the Terrains menu excludes Rock Floor and Wood Floor while the
normal construction entries remain available. Build a named Rock Floor normally,
place Bathhouse Water over it, then use normal construction to change the floor to
Wood Floor while retaining the water, save/reload, and deconstruct only the
water. Also test Dry Dirt surrounded by grass: the grass fringe should update
without reloading. Automated checks do not execute Unity rendering or saves.
For animation, place Bathhouse Water beside existing water several seconds apart,
pan away/back, and save/reload; the surface frames should remain aligned. Check
that frozen water stays frozen and ordinary fire animations remain unaffected.
For interiors, compare adjacent outdoor/interior grass during rain, snow and fog;
weather should remain outside but disappear over the interior cell. Test interior
deep/shallow/bathhouse water, seasonal thaw, and frozen variants. Save/reload,
leave/re-enter the area, then build/deconstruct a normal floor over interior grass.
Confirm the roof persists or disappears with its owning tile, the underlying
floor can still be changed beneath interior water, and roof masks do not leave
gaps or hide the terrain/actors themselves. Back up a save before testing.

## Credits

Created by **Saelac and ChatGPT**.
