# Terrain Construction

Adds a **Terrains** category to the Modding Tools construction window. The
category discovers Silverpine's serialized `prefab_tile_*` resources and makes
every visually distinct terrain/floor tile placeable at no material or hammer
cost. The redundant Wood Bridge entry is omitted because it looks identical to
Wood Floor. The river-specific water prefab is likewise omitted; its visually
and mechanically identical lake prefab is presented as **Water** instead.
The generated Snowy Swamp Grass entry is also omitted because its winter
sprites, tint, sounds, and description are identical to regular **Snow**.

Selecting a terrain replaces the existing ground tile instead of stacking a
second tile over it. This also permits repainting water, abyss, and other
impassable terrain. The placement ghost is owned by Modding Tools and remains
locked to the same integer grid as normal floor construction.

Terrain with a real winter presentation also receives a persistent snowy or
frozen variant. These variants mark cloned Silverpine tiles as forced-winter,
so their existing seasonal sprite, sound, footprint, passability, and tile-tag
components behave the same way they do in the High Mountains. Normal terrain
retains its original Silverpine prefab name and behavior; snowy variants use
plugin-owned serialized prefab names so the override survives save/load.

`prefab_tile_teleporter_world` is intentionally excluded. It is the invisible
world-edge transition trigger rather than a terrain surface, and placing it in
the world would create unintended map transitions.

## Requirements

- Silverpine 1.7.3
- BepInEx 5
- Modding Tools 1.7.0 or newer

## Use

Open the player's **Construct** ability, select **Terrains**, choose a tile, and
click an existing ground tile within placement range. Terrain placement is
immediate and free.

## Credits

Created by **Saelac and ChatGPT**.
