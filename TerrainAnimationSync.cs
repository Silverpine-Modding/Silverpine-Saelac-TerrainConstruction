#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SilverpineMods.TerrainConstruction;

internal readonly struct TerrainAnimationPhase
{
    internal TerrainAnimationPhase(int frame, float elapsed, float pauseRemaining)
    { Frame = frame; Elapsed = elapsed; PauseRemaining = pauseRemaining; }
    internal readonly int Frame;
    internal readonly float Elapsed;
    internal readonly float PauseRemaining;
}

// Pure timing calculation, shared by animations with the same native settings.
// Native FixedUpdate discards excess frame time and advances only when time >
// delay, so preserve that cadence rather than speeding up the animation slightly.
internal sealed class TerrainAnimationTiming
{
    private const int MaximumTicks = 10000;
    private readonly int frameCount, frameTicks, pauseTicks;
    private readonly float[] frameTimes, pauseTimes;
    private readonly float step;
    private double lastClock = double.NaN;
    private TerrainAnimationPhase lastPhase;

    private TerrainAnimationTiming(int frames, float[] frameTimes, float[] pauseTimes, float step)
    {
        frameCount = frames; this.frameTimes = frameTimes; this.pauseTimes = pauseTimes;
        frameTicks = frameTimes.Length; pauseTicks = pauseTimes.Length; this.step = step;
    }

    internal static TerrainAnimationTiming? Create(int frames, float delay, float loopDelay, float step)
    {
        if (frames < 2 || !Finite(delay) || delay <= 0f || !Finite(loopDelay) || loopDelay < 0f ||
            !Finite(step) || step <= 0f) return null;
        var frameTimes = new List<float>();
        var pauseTimes = new List<float>();
        float elapsed = 0f, remaining = loopDelay;
        while (elapsed <= delay && frameTimes.Count < MaximumTicks)
        { frameTimes.Add(elapsed); elapsed += step; }
        while (remaining > 0f && pauseTimes.Count < MaximumTicks)
        { pauseTimes.Add(remaining); remaining -= step; }
        if (elapsed <= delay || remaining > 0f) return null;
        return new TerrainAnimationTiming(frames, frameTimes.ToArray(), pauseTimes.ToArray(), step);
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    internal TerrainAnimationPhase Sample(double clock)
    {
        if (clock == lastClock) return lastPhase;
        if (double.IsNaN(clock) || double.IsInfinity(clock) || clock < 0d)
            throw new ArgumentOutOfRangeException(nameof(clock));
        long activeTicks = (long)frameCount * frameTicks;
        long cycleTicks = activeTicks + pauseTicks;
        // Double precision avoids long-session float clock drift. The small
        // tolerance only corrects division rounding at an exact physics tick.
        double ticks = Math.Floor(clock / step + 0.0000001d);
        long tick = (long)(ticks % cycleTicks);
        lastPhase = tick < activeTicks
            ? new TerrainAnimationPhase((int)(tick / frameTicks), frameTimes[(int)(tick % frameTicks)], 0f)
            : new TerrainAnimationPhase(0, 0f, pauseTimes[(int)(tick - activeTicks)]);
        lastClock = clock;
        return lastPhase;
    }
}

internal static class TerrainAnimationSync
{
    private sealed class Candidate { internal bool Eligible; }
    private static readonly ConditionalWeakTable<SpriteAnimator, Candidate> candidates = new();
    private static readonly Dictionary<(int Frames, float Delay, float Pause, float Step), TerrainAnimationTiming?> timings = new();

    internal static bool IsAnimatedTerrainName(string name)
    {
        switch (TerrainLayers.GetNativeName(name).ToLowerInvariant())
        {
            case "prefab_tile_water":
            case "prefab_tile_water_lake":
            case "prefab_tile_water_swamp":
            case "prefab_tile_water_bathhouse":
            case "prefab_tile_water_shallow":
            case "prefab_tile_water_shallow_swamp":
            case "prefab_tile_icy_slip":
                return true;
            default:
                return false;
        }
    }

    private static Candidate Classify(SpriteAnimator animator)
    {
        GameObject tile = animator.gameObject;
        return new Candidate
        {
            // Exact known terrain roots only: never furniture, particles,
            // children/shore effects, menu proxies, or character animations.
            Eligible = IsAnimatedTerrainName(SerializationManager.GetPrefabName(tile)) &&
                tile.GetComponent<TurfRegistrar>() != null &&
                (tile.GetComponent<DeepTileAOHandler>() != null || tile.GetComponent<IceTile>() != null)
        };
    }

    internal static bool Apply(SpriteAnimator animator, Sprite[] sprites, SpriteRenderer renderer,
        float delay, float loopDelay, bool loop, bool wind,
        ref int state, ref float elapsed, ref float pauseRemaining, bool forceSprite)
    {
        // Do not re-enable seasonal ice or replace its winter sprite. Unsupported
        // animation settings fall through to the game's original implementation.
        if (!loop || wind || animator == null || !animator.isActiveAndEnabled ||
            renderer == null || sprites == null || sprites.Length < 2 ||
            !candidates.GetValue(animator, Classify).Eligible) return false;

        var key = (Frames: sprites.Length, Delay: delay, Pause: loopDelay, Step: Time.fixedDeltaTime);
        if (!timings.TryGetValue(key, out TerrainAnimationTiming? timing))
        {
            // Bound the cache if another mod repeatedly changes animation speed.
            if (timings.Count >= 64) timings.Clear();
            timing = TerrainAnimationTiming.Create(key.Frames, key.Delay, key.Pause, key.Step);
            timings.Add(key, timing);
        }
        if (timing == null) return false;

        TerrainAnimationPhase phase = timing.Sample(Time.fixedTimeAsDouble);
        bool changed = state != phase.Frame;
        state = phase.Frame;
        elapsed = phase.Elapsed;
        pauseRemaining = phase.PauseRemaining;
        // Also update off-screen tiles on frame changes, so newly visible tiles
        // already have the correct sprite. No world scans or per-tile clocks.
        if (changed || forceSprite) renderer.sprite = sprites[state];
        return true;
    }
}

[HarmonyPatch(typeof(SpriteAnimator), "FixedUpdate")]
internal static class TerrainAnimationTickPatch
{
    private static bool Prefix(SpriteAnimator __instance, Sprite[] ___sprites,
        SpriteRenderer ___spriteRenderer, float ___delay, float ___loopDelay,
        bool ___loop, bool ___affectedByWindSpeed,
        ref int ___state, ref float ___time, ref float ___loopDelayTime) =>
        !TerrainAnimationSync.Apply(__instance, ___sprites, ___spriteRenderer,
            ___delay, ___loopDelay, ___loop, ___affectedByWindSpeed,
            ref ___state, ref ___time, ref ___loopDelayTime, forceSprite: false);
}

[HarmonyPatch]
internal static class TerrainAnimationResetPatch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        yield return typeof(SpriteAnimator).GetMethod("Start", flags)!;
        // SeasonWater calls this when thawing; SetSprites also goes through it.
        yield return typeof(SpriteAnimator).GetMethod(nameof(SpriteAnimator.SetStateToZero), flags)!;
    }

    private static void Postfix(SpriteAnimator __instance, Sprite[] ___sprites,
        SpriteRenderer ___spriteRenderer, float ___delay, float ___loopDelay,
        bool ___loop, bool ___affectedByWindSpeed,
        ref int ___state, ref float ___time, ref float ___loopDelayTime) =>
        TerrainAnimationSync.Apply(__instance, ___sprites, ___spriteRenderer,
            ___delay, ___loopDelay, ___loop, ___affectedByWindSpeed,
            ref ___state, ref ___time, ref ___loopDelayTime, forceSprite: true);
}
