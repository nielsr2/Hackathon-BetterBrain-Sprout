# Proc Tree (`com.nib.proctree`)

A procedurally simulated **parkland oak** for HDRP. One float — `ProceduralTree.growth`, 0 → 1 —
replays the tree's whole life: nothing at 0, a seedling just above it, then sapling, young tree and
finally a broad mature crown with leaves.

The tree is simulated once (year by year, from seed + `TreeProfile`) and every piece of wood and
every leaf remembers when it was born and when it died. Wood never moves once formed, so the GPU
reveals the tree for any `growth` value with no CPU mesh work — `growth` can be driven every frame
from MIDI/OSC, Timeline or scripts.

Design: `docs/superpowers/specs/2026-10-04-proctree-oak-design.md`.
Plan: `docs/superpowers/plans/2026-10-04-proctree-oak.md`.

## Quick start (HDRP project)

1. Add `com.nib.proctree` (and its dependency `com.nib.editor-theme`) as local packages.
2. **Tools ▸ Proc Tree ▸ Setup Project** — generates the Oak profile, textures, materials, the
   Oak prefab and the Growth Demo sample.
3. Hierarchy right-click **Nib ▸ Proc Tree ▸ Oak**, then drag the inspector's **Growth** slider.

Drive `ProceduralTree.growth` (0..1) from anything — Timeline, Animator, the Nib MIDI/OSC packages,
or a script. Only one tree per scene is the design target.

The tree is regenerated in memory (background thread, ~0.5 s for the mature Oak) whenever the
profile or seed changes and on load; nothing heavy is serialized into prefabs or scenes —
the seed is the asset.

## Assemblies

| Assembly | Contents |
|---|---|
| `Nib.ProcTree.Core` | Simulation + mesh arrays. **No Unity references** (`System.Numerics`) — thread-safe, testable offline. |
| `Nib.ProcTree.Runtime` | Components, assets, GPU upload. |
| `Nib.ProcTree.Editor` | Inspectors, setup, generated textures. |

## Offline tests

`Tests~/CoreHarness` compiles the Core and its tests outside Unity:

```bash
dotnet run -c Release --project Tests~/CoreHarness
```
