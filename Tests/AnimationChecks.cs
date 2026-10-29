using System.Reflection;
using Mono.Cecil;

internal static class AnimationChecks
{
    internal static void Run(Assembly assembly, Action<bool, string> check)
    {
        Type Type(string name) => assembly.GetType("SilverpineMods.TerrainConstruction." + name)!;
        object? Call(string type, string method, params object[] args) =>
            Type(type).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args);
        bool Candidate(string name) => (bool)Call("TerrainAnimationSync", "IsAnimatedTerrainName", name)!;
        string[] names = { "water", "water_lake", "water_swamp", "water_bathhouse", "water_shallow", "water_shallow_swamp", "icy_slip" };
        check(names.All(name => Candidate("prefab_tile_" + name)), "native river, lake, swamp, bathhouse, shallow water and slippery ice are synchronized");
        check(new[] { "water_bathhouse", "water_shallow", "water_shallow_swamp", "icy_slip" }
            .All(name => Candidate("prefab_tile_terrainconstruction_overlay_" + name)), "mod-placed overlays join the native animation clock");
        check(Candidate("terrainconstruction_snowy_prefab_tile_water_shallow") &&
            Candidate("PREFAB_TILE_WATER_BATHHOUSE"), "old save names and case variants retain clock eligibility");
        check(new[] { "prefab_furniture_firepit", "prefab_character_water", "prefab_effect_fakeao_water",
            "terrainconstruction_prefab_tile_water", "prefab_tile_water_custom", "prefab_tile_grass", "prefab_tile_icy_tiles" }.All(name => !Candidate(name)),
            "fire, characters, shore effects, proxies, unknown animations and static ground are excluded");

        object? Create(int frames, float delay, float pause, float step) => Call("TerrainAnimationTiming", "Create", frames, delay, pause, step);
        (int Frame, float Elapsed, float Pause) Sample(object timing, double clock)
        {
            object phase = timing.GetType().GetMethod("Sample", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(timing, new object[] { clock })!;
            object Field(string field) => phase.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(phase)!;
            return ((int)Field("Frame"), (float)Field("Elapsed"), (float)Field("PauseRemaining"));
        }
        void CompareNative(int frames, float delay, float pause, float step, string description)
        {
            object timing = Create(frames, delay, pause, step)!;
            int state = 0;
            float elapsed = 0f, remaining = 0f;
            for (int tick = 0; tick < 12000; tick++)
            {
                if (tick > 0)
                {
                    // Installed game's SpriteAnimator.FixedUpdate semantics.
                    if (remaining > 0f) remaining -= step;
                    else
                    {
                        elapsed += step;
                        if (elapsed > delay)
                        {
                            elapsed = 0f;
                            if (++state >= frames) { state = 0; remaining = pause; }
                        }
                    }
                }
                var phase = Sample(timing, tick * (double)step);
                if (phase.Frame != state || phase.Elapsed != elapsed || phase.Pause != Math.Max(0f, remaining))
                    throw new Exception(description + $" differs from native animation at tick {tick}: {phase} vs ({state}, {elapsed}, {remaining})");
            }
            check(true, description + " matches native frame and loop-pause timing across 12,000 ticks");
        }
        CompareNative(5, 1f, 0f, 0.02f, "water clock");
        CompareNative(10, 0.1f, 4f, 0.02f, "slippery ice clock");
        CompareNative(5, 1f, 0f, 0.01f, "changed physics step");
        CompareNative(3, 0.5f, 0.07f, 0.02f, "fractional loop pause");
        check(Create(1, 1f, 0f, 0.02f) == null && Create(5, 0f, 0f, 0.02f) == null &&
            Create(5, float.NaN, 0f, 0.02f) == null && Create(5, 1f, -1f, 0.02f) == null &&
            Create(5, 1f, float.PositiveInfinity, 0.02f) == null && Create(5, 1f, 0f, 0f) == null &&
            Create(5, float.MaxValue, 0f, 0.02f) == null, "invalid or unbounded timing settings fall back to native animation");
        object existing = Create(5, 1f, 0f, 0.02f)!;
        foreach (double now in new[] { 0d, 3.7d, 1024.123d, 8640000.5d })
        {
            Sample(existing, Math.Max(0d, now - 100));
            var expected = Sample(existing, now);
            check(Sample(Create(5, 1f, 0f, 0.02f)!, now) == expected && Sample(existing, now) == expected,
                "newly placed/loaded tiles join the same phase at clock " + now);
        }
        check(Sample(existing, 2.2d) == Sample(existing, 2.2d), "unchanged physics time does not advance the animation");

        using var module = ModuleDefinition.ReadModule(assembly.Location);
        TypeDefinition Definition(string name) => module.Types.Single(t => t.Name == name);
        var apply = Definition("TerrainAnimationSync").Methods.Single(m => m.Name == "Apply").Body.Instructions;
        var calls = apply.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        check(calls.Any(m => m.Name == "get_fixedTimeAsDouble") && calls.Any(m => m.Name == "get_isActiveAndEnabled") &&
            !calls.Any(m => m.Name is "set_enabled" or "SetActive"), "shared physics clock respects seasonal disabled state and never enables frozen water");
        check(!calls.Any(m => m.Name.Contains("FindObjects") || m.Name is "set_color" or "set_sortingOrder" or "Destroy" or "Instantiate"),
            "animation synchronization preserves opacity, sorting and terrain objects without world scans");
        var resetTargets = ((IEnumerable<MethodBase>)Call("TerrainAnimationResetPatch", "TargetMethods")!).ToArray();
        check(resetTargets.All(m => m.DeclaringType == typeof(SpriteAnimator)) &&
            resetTargets.Select(m => m.Name).ToHashSet().SetEquals(new[] { "Start", "SetStateToZero" }),
            "load/start and thaw/sprite reset rejoin the same clock");
        foreach (string patch in new[] { "TerrainAnimationTickPatch", "TerrainAnimationResetPatch" })
        {
            MethodInfo hook = Type(patch).GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
                .Single(m => m.Name is "Prefix" or "Postfix");
            check(hook.GetParameters().Where(p => p.Name!.StartsWith("___")).All(p =>
            {
                FieldInfo? native = typeof(SpriteAnimator).GetField(p.Name!.Substring(3), BindingFlags.Instance | BindingFlags.NonPublic);
                return native != null && native.FieldType == (p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType);
            }), patch + " private-field bindings match the installed game");
        }
        check(!Definition("TerrainAnimationSync").Methods.Any(m => m.Name is "OnDestroy" or "OnDisable") &&
            Definition("TerrainAnimationSync").Fields.Any(f => f.FieldType.FullName.Contains("ConditionalWeakTable")),
            "animation synchronization needs no plugin lifecycle host and does not retain unloaded tiles");
    }
}
