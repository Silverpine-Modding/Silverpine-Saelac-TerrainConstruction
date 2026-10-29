# Terrain Construction

Adds a **Terrains** category to the Modding Tools construction window. The
category discovers Silverpine's serialized `prefab_tile_*` resources and makes
every visually distinct terrain/floor tile placeable at no material or hammer
cost. The redundant Wood Bridge entry is omitted because it looks identical to
Wood Floor. The river-specific water prefab is likewise omitted; its visually
and mechanically identical lake prefab is presented as **Water** instead.
The generated Snowy Swamp Grass entry is also omitted because its winter
sprites, tint, sounds, and description are identical to regular **Snow**.

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
The chosen prefab keeps its native components and behavior (including
deconstruction on wood/rock floors).

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
game directory as its argument. The checks cover layer classification, saved
prefab names, both patches against the installed game's IL, refresh calls,
deconstruction/serialization wiring, and plugin lifecycle.
Animation checks also compare the shared clock against native fixed-update
timing, test late joins, and validate the hooks against the installed assembly.

In game, verify Bathhouse Water over a named Rock Floor, change the floor to
Wood Floor while retaining the water, save/reload, and deconstruct only the
water. Also test Dry Dirt surrounded by grass: the grass fringe should update
without reloading. Automated checks do not execute Unity rendering or saves.
For animation, place Bathhouse Water beside existing water several seconds apart,
pan away/back, and save/reload; the surface frames should remain aligned. Check
that frozen water stays frozen and ordinary fire animations remain unaffected.

## Credits

Created by **Saelac and ChatGPT**.
