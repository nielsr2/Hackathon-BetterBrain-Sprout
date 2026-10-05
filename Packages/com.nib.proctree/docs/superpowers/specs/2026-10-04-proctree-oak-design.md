# Proc Tree — Oak: design spec

**Date:** 2026-10-04
**Status:** design approved in brainstorming (sections 1–5); awaiting written-spec review
**Package id:** `com.nib.proctree` · **Display name:** Proc Tree · **Folder:** `UnityPackages/com.nib.proctree/` (own git repo)
**Namespaces:** `Nib.ProcTree`, `Nib.ProcTree.Editor` · **Asmdefs:** `Nib.ProcTree.Runtime`, `Nib.ProcTree.Editor`, `Nib.ProcTree.Tests.Editor`, `Nib.ProcTree.Tests.Runtime`
**Menus:** `Window/Nib/Proc Tree/…`, `GameObject/Nib/Proc Tree/…`, `Nib/Proc Tree/…` (Add Component, Create Asset)

## 1. Intent

A procedurally generated **English parkland oak** (*Quercus robur*, open-grown: short massive trunk,
huge low near-horizontal limbs, crown wider than tall) for live visuals in HDRP. One float,
**`growth` ∈ [0,1]**, drives the tree from **nothing** (0) through a **seedling** (just above 0) to a
**fully mature oak with leaves** (1).

### What the user decided

| Question | Decision |
|---|---|
| How is `growth` driven? | **Live, every frame** (MIDI/OSC, Timeline, scripts) at 60 fps in Play mode |
| Framing | **One hero tree, close-up**; camera can approach bark and leaves |
| Trees in scene | **Exactly one** — no LODs, no multi-tree batching or pooling |
| Bottom of the slider | **0 = nothing renders**; just above 0 a seedling sprouts |
| Mature form | **Parkland / solitary oak** |
| Shape control | **Purely parametric** (seed + `TreeProfile`); no hand-edited limbs |
| Textures | Bark kept **simple** (code-generated tiling albedo + normal); leaves included |
| Generation approach | **History replay** (§3) — simulate years, reveal on GPU |
| Packaging | **Standalone** package; copy the few generic ProcFoliage pieces, no dependency |

### Success criteria

- Scrubbing `growth` shows one specific oak growing up believably: seedling → whip-thin sapling →
  narrow young tree → broad, gnarled parkland crown. A young stage is a genuinely young oak
  (taller-than-wide, narrow), **not a scaled-down mature tree**.
- Growth is **continuous**: no popping, no reshuffling of branches between nearby `growth` values.
  The only discrete events are biological — a shoot appearing, a leaf unfolding, a pruned branch
  shrinking away — and each of those is itself animated over a short window.
- Driving `growth` every frame costs ≈ nothing on the CPU (§9 budgets).
- Same profile + seed ⇒ byte-identical tree.

## 2. Reuse from ProcFoliage (`ferns/`, `com.nib.procfoliage`)

Copied (not referenced) and renamed into `Nib.ProcTree`:

| From ProcFoliage | Use here |
|---|---|
| `Runtime/Generation/SeededRng.cs` | Verbatim (xorshift32 + `Fork(salt)`); one stream per bud |
| `Runtime/Generation/MeshBuffers.cs` | Core reused; vertex channels redefined for bark (§5.1) |
| `Shaders/Include/FrondLighting.hlsl` (+ property/data scaffolding of `FrondForward.shader`) | Lighting for both shaders: wrap diffuse, GGX, ambient SH, point/spot culled lights, punctual shadows, thickness transmission (leaf only) |
| `Shaders/Include/FrondWind.hlsl`, `Runtime/Components/FrondWind.cs` | Pattern for `TreeWind` (globals, WindZone follow, disabled ⇒ still air); math rewritten hierarchical (§6.3) |
| `Runtime/Materials/MaterialFactory.cs`, `Data/MaterialProfile.cs` | Pattern: materials built in code, no manual wiring |
| `Editor/PlaceholderAtlasGenerator.cs` | Pattern: deterministic code-generated textures — new **lobed oak-leaf** silhouette + **bark** albedo/normal |
| `Editor/Setup/{PresetFactory,PrefabFactory,ProjectSetup}.cs`, UI Toolkit inspectors, tests layout | Patterns only |

Not reused: `FrondGenerator`, `LeafletBuilder`, `RachisBuilder` (code), the spline spine and its
tools, `SpineRigBuilder`/`SkinWeightBaker`, Frond Studio — all built on ProcFoliage's single-spine
constraint (C5), which a tree violates by definition.

Carried-over **principles**: no Shader Graph (hand-written HLSL, matched to the *installed* HDRP —
never from memory); fully reproducible from code (presets/materials/textures/prefab/sample built by
a setup menu, runnable in `-batchmode`); deterministic.

## 3. Architecture overview

```
TreeProfile + seed
   │  (background thread, pure C#, no Unity API)
   ▼
GrowthSimulator.Run  ──►  TreeHistory { Segment[], Leaf[], Axis[] }      (§4)
   │
   ▼  (background thread)
BarkMeshBuilder / LeafInstanceBuilder ──► TreeBakeData (arrays)          (§5)
   │
   ▼  (main thread)
TreeBake asset (Mesh + segment buffer data + leaf instance data + leaf Mesh)
   │
   ▼
ProceduralTree component ── sets per-renderer _Growth/_PrevGrowth ──► GPU reveals (§6)
```

**Key property:** wood never moves once it forms (real trees extend only at tips; old wood only
thickens). Every bark vertex therefore has a fixed final centerline position; the shader only
decides *whether it exists yet* and *how thick it is now*. Changing `growth` requires **no CPU
mesh work and no per-branch transforms** — only a shader float (plus leaf compaction, §5.3).

## 4. Growth simulation (`GrowthSimulator`)

Bud-driven self-organizing tree model (Palubicki et al. 2009, *Self-organizing tree models for
image synthesis*), stepped in discrete **years** (`TreeProfile.years`, default 80).

### 4.1 Per-year step

1. **Space / light.** A fixed, seeded cloud of space markers fills the **mature crown envelope**
   (parkland: a wide flattened half-dome above `crownBaseHeight`). The *active* envelope grows with
   age (`envelopeByAge` curve: narrow tall cone when young → full dome when mature); only markers
   inside it are visible that year. Each bud has a perception cone (`perceptionAngle`,
   `perceptionRadius`); markers within `occupancyRadius` of any internode are consumed. A bud's
   light `Q` = markers it is closest to within its cone.
2. **Vigor.** `Q` is accumulated basipetally (tip → root). The root's total is redistributed
   acropetally (Borchert–Honda): at each fork the main axis receives a share weighted by
   `apicalDominance` (λ), laterals the rest. A bud's vigor `v` sets the integer number of internodes
   it extends this year (`floor(v)`, capped by `maxInternodesPerYear`), each of length
   `internodeLength` (scaled by `internodeLengthByAge`).
3. **Direction.** New shoot direction = normalized blend of: previous heading (`straightness`),
   optimal-space direction from its markers (`tropismSpace`), gravitropism (`tropismGravity`;
   negative = upward early, a `gravityByAge` curve lets heavy mature limbs droop and spread), and
   seeded random wobble (`gnarliness`).
4. **Bud placement.** Alternate phyllotaxis (`phyllotaxisAngle`, ~144°) with lateral buds at
   `branchingAngle`. **Oak terminal bud cluster**: the last `terminalClusterSize` nodes of each
   year's shoot carry buds, producing the twiggy zig-zag texture; lateral buds lower down stay
   dormant unless light-rich.
5. **Self-pruning.** An axis whose light per internode stays below `pruneLightThreshold` ×
   *the tree's average* light per internode for `pruneYears` consecutive years dies: all its
   segments and leaves get `deathYear`. Optionally a short stub (`pruneStubLength`) survives. Dead
   wood is removed from the light/vigor computation. Limbs holding more than
   `pruneProtectFraction` of the tree are never shed. *(Revised during T1.4 tuning: an absolute
   threshold made the whole crown die at once when a filled-out crown's light dropped everywhere.)*
6. **Leaves.** Each new internode carries leaves, alternate, denser toward shoot tips. Each leaf:
   `birthYear` = when its internode finishes extending, unfold over `leafFlushYears`. It lives while
   its internode is within `leafZoneInternodes` of its axis tip (at least `leafMinLifeYears`), and
   drops when the axis grows past it or the limb dies. Result: bare interior, foliage on the crown
   shell, and a mature crown that keeps its foliage even when it no longer expands. *(Revised
   during T1.4 tuning: "only the last two years of shoots" left a mature, slow-growing crown
   almost leafless.)*
7. **Leader dominance.** The trunk leader gets extra light (`apicalBaseLight`) only until
   `leaderDominanceAge`. After that the crown turns decurrent, as oaks do, and limbs take over.

### 4.2 Seedling (year 0 → 1)

Year 0 has no geometry. During year 0→1 a single hypocotyl shoot rises (`seedlingHeight`) with
`seedlingLeafCount` (2–4) leaves. `growth = 0` ⇒ `year = 0` ⇒ nothing renders.

### 4.3 Growth → year mapping

`year = years × growthCurve.Evaluate(growth)`. Default curve is front-loaded in slider travel
(sapling stage occupies a meaningful portion of the slider, not the first 2%). Monotonic and clamped;
`growthCurve(0) = 0`, `growthCurve(1) = 1`.

### 4.4 Thickness — pipe model

For each segment at a given year, `r = minTwigRadius × n^(1/pipeExponent)`, where `n` = number of
living shoot tips it supports at that year and `pipeExponent` defaults to 2 (so r² ∝ n, the
classic pipe model). Computed after simulation from descendants'
birth/death years and sampled into **8 keys** across `[birthYear, years]` (monotonic
non-decreasing while alive). Trunk base gets an additional `rootFlare` term.

### 4.5 Determinism

One `SeededRng` per bud, forked from its parent bud's id (`Fork(budId)`); buds visited in a fixed
order (creation order) each year. Marker cloud seeded from the root RNG. No `UnityEngine.Random`,
no hash-ordered collections, no wall-clock reads. Same profile + seed ⇒ byte-identical
`TreeHistory`.

### 4.6 Output (`TreeHistory`)

- `Segment { start, end (mature positions, tree-local), axisId, parentSegment, birthYear,
  growDuration, deathYear (∞ if alive), radiusKeys[8] }`
- `Axis { firstSegment, segmentCount, parentAxis, order }`
- `Leaf { position, rotation, size, birthYear, deathYear, parentSegment, tint, phase }`

Mature Oak preset targets: height ~15 m, spread ~18 m, trunk Ø ~1.2 m, 20–40k segments,
~25k leaves alive at `growth = 1`, ~80k leaves over the whole history.

## 5. Meshes and GPU growth data

Two draws: **bark** (one mesh) and **leaves** (instanced). Depth, ShadowCaster, Forward and
MotionVectors passes share the same vertex function per shader ⇒ shadows/silhouettes match at
every `growth`.

### 5.1 Bark mesh (`BarkMeshBuilder`)

- One continuous swept tube per **axis** (consecutive segments of one branch from base to tip).
  Ring sides scale with mature radius (`ringSidesMax` 24 on trunk → `ringSidesMin` 3 on twigs).
  Ring frames by parallel transport (same technique as ProcFoliage `SpineSampler`).
- Child axes start inside the parent with a short flared **branch collar**; no boolean welding.
  Trunk base gets a buttressed **root flare**.
- Vertex channels:
  - `POSITION` = centerline point (final, tree-local)
  - `NORMAL` = radial direction
  - `TANGENT` = centerline direction
  - `UV0` = (angle01 around ring, fraction along segment)
  - `UV1` = (segmentId as float, arc length from axis base, axis phase, axis order)
  - `COLOR` = bark tint jitter
  - Index format UInt32.
- `StructuredBuffer<SegmentGpu>`: start, end, birthYear, growDuration, deathYear, radiusKeys[8].

### 5.2 Bark vertex logic (`TreeGrowth.hlsl`)

Given `year` (from `_Growth` via the growth curve, which is baked into a small lookup on the
material):

- `progress = saturate((year - birth) / growDuration)`. A vertex at `fractionAlong > progress`
  collapses onto the growth-front point `lerp(start, end, progress)`; the front tapers to a tip.
  Collapsed triangles have zero area and rasterize nothing. `year < birth` ⇒ fully collapsed.
- `radius = EvalKeys(radiusKeys, year)`; dead segments ease radius → 0 over `deathFadeYears`.
- Final position = centerline + radial × radius. Normal = radial (+ bark normal map).

### 5.3 Leaves (`LeafInstanceBuilder`, `LeafRenderer`)

- One low-poly oak-leaf mesh (~16 verts, slight midrib fold), double-sided; lobed outline via
  alpha-cut texture.
- Instance data: position, rotation, size, birthYear, deathYear, parentSegment, tint, phase.
- Shader scales each leaf 0→1 over its flush and 1→0 at death.
- **Drawn as a slot mesh, not instanced** *(revised 2026-10-04 before the spike, replacing
  `Graphics.RenderMeshIndirect`)*: a regular `MeshRenderer` whose mesh holds `maxLiveLeaves`
  copies of the leaf blade ("slots"); every slot vertex carries its slot index in UV1. When
  `growth` changes, the CPU rebuilds a compact list of live leaves (≈0.2 ms, only on frames where
  `growth` changed, 0 GC), uploads it to `_LiveLeaves`, and shrinks the submesh index count to
  `live × indicesPerLeaf`, so the GPU only processes live leaves. The vertex shader places slot
  `i` as leaf `_LiveLeaves[i]`. Because it is an ordinary renderer, every HDRP pass (depth,
  shadows, motion vectors, forward) works exactly as for ProcFoliage's verified fronds — the
  procedural-instancing risk is gone. The slot mesh is built at runtime from the blade template
  (`HideFlags.DontSave`), never stored in the bake, so changing its draw range never dirties an asset.
- **Motion vectors:** both bark and leaves compute their previous position with
  `_PrevTreeYear` when HDRP's motion-vector pass calls `ApplyMeshModification` with
  `_LastTimeParameters`; the slot→leaf mapping is the *current* one in both evaluations, so a
  re-compaction never produces bogus motion.

### 5.4 Bounds

Renderer bounds are always the **mature** bounds — stable culling while growing.

## 6. Shaders and wind

### 6.1 Shaders

- **`ProcTree/Bark`** — opaque, lit (copied lighting: diffuse, GGX, ambient SH, point/spot,
  shadows). No transmission, no alpha test.
- **`ProcTree/Leaf`** — frond-shader lineage + growth + procedural instancing: thickness
  transmission (backlit glow), alpha-cut lobes, double-sided, per-leaf hue variation. COLOR `age`
  channel reserved (future autumn), unused in v1.
- Shared includes: `TreeGrowth.hlsl` (§5.2), `TreeWind.hlsl` (§6.3), copied lighting include.
- Verified against the **installed** HDRP version (pass names, light modes, include paths,
  instancing macros) — never from memory.

### 6.2 Growth uniforms

Per renderer via `MaterialPropertyBlock`: `_Growth`, `_PrevGrowth` (previous frame's value, so
MotionVectors are correct while scrubbing — prevents TAA smearing of growing tips). One tree in the
scene; per-renderer is kept because it costs nothing.

### 6.3 Hierarchical wind (`TreeWind.hlsl`)

All terms computed from stored centerline data ⇒ identical in every pass.

1. **Trunk sway** — whole tree, amplitude ∝ height².
2. **Branch sway** — each segment sways about its axis base; amplitude ∝ 1 / *current* radius
   (thin sapling whips, mature limb barely moves — no separate stiffness knob); per-axis phase.
3. **Leaf flutter** — high frequency, per-leaf phase; each leaf first receives its
   `parentSegment`'s branch sway so it stays attached to its twig.

### 6.4 `TreeWind` component

`[ExecuteAlways]`, `Nib/Proc Tree/Tree Wind`. Pushes `_TreeWind*` globals (direction, strength,
speed, flutter, flutter freq); optional follow of a directional `WindZone`; disabled ⇒ zero
amplitudes (still air). Separate globals from `FrondWind`; no cross-package dependency.

## 7. Components, data, editor

### 7.1 Data

- **`TreeProfile`** (`ScriptableObject`, `[CreateAssetMenu("Nib/Proc Tree/Tree Profile")]`): all
  §4 parameters (years, envelope, crown base height, apical dominance, branching/phyllotaxis
  angles, tropisms + by-age curves, gnarliness, terminal cluster size, pruning threshold/years/stub,
  leaf size/density/life/flush, ring sides min/max, root flare, `growthCurve`), bark/leaf colors.
  `ClampToValidRanges()` on every use. Ships the **Oak (Parkland)** preset.
- ~~**`TreeBake`** asset shipped inside prefabs/scenes~~ — *dropped 2026-10-04*: the mature bark
  mesh is 276k vertices, which serializes to ~40–50 MB of YAML per prefab **and again inside every
  scene** holding the tree. The tree is deterministic, so **the seed is the asset**: the bake
  (`TreeBakeData` + Unity-typed arrays) lives in memory only and is regenerated on enable on a
  background thread (~0.45 s for the mature Oak; growth 0 renders nothing anyway, so a tree that
  appears half a second after load is invisible in practice). Only the GPU upload (~tens of ms)
  runs on the main thread. A compact binary cache (`.bytes`) can be added later if load time ever
  matters.

### 7.2 `ProceduralTree` component

`[ExecuteAlways]`, `[AddComponentMenu("Nib/Proc Tree/Procedural Tree")]`.

- Fields: `profile`, `seed`, `[Range(0,1)] public float growth` (+ `Growth` property). Plain field
  so Timeline, Animator and the Nib MIDI/OSC packages can drive it. Zero per-frame cost when
  unchanged.
- **Regenerate** when profile hash or seed differs from the bake: simulation + mesh/instance array
  building on a **background thread** (pure C#); the existing tree stays visible; swap-in
  (Mesh/GraphicsBuffer upload) on the main thread. Never a frame with nothing drawn. A newer
  request cancels/supersedes an in-flight one.
- Owns the leaf `RenderMeshIndirect` draw, GPU buffers and compaction; releases buffers in
  `OnDisable`/`OnDestroy` (no leaks across domain reload).

### 7.3 Editor (UI Toolkit, per `PACKAGE_STANDARDS.md`)

- **`ProceduralTreeInspector`**: large growth slider with "year 34 / 80" readout; Regenerate,
  🎲 random seed, progress bar while simulating; stats (segments, leaves alive/total, sim ms,
  bark verts).
- **`TreeProfileInspector`**: grouped sections; edits debounce into a background regenerate of
  trees using that profile.
- `GameObject/Nib/Proc Tree/Oak` drops the prefab. `Window/Nib/Proc Tree/Setup Project` builds
  presets, materials, textures, prefab and sample from code (batchmode-safe; fails loudly if HDRP
  is not active).
- **Sample** `Samples~/GrowthDemo`: one oak, rotating sun, `GrowthAnimator` (ping-pongs `growth`).

### 7.4 Package skeleton

```
com.nib.proctree/
  package.json  CHANGELOG.md  README.md
  Runtime/  (Nib.ProcTree.Runtime.asmdef)
    Data/ Simulation/ Meshing/ Rendering/ Components/ Prefabs/
  Editor/   (Nib.ProcTree.Editor.asmdef)
    Inspectors/ Setup/ Textures/
  Shaders/  Bark.shader  Leaf.shader  Include/
  Samples~/GrowthDemo/
  Tests/Editor/  (Nib.ProcTree.Tests.Editor.asmdef)
  Tests/Runtime/ (Nib.ProcTree.Tests.Runtime.asmdef)
  docs/superpowers/
```

`package.json`: `unity` pinned to the installed editor's major.minor at implementation time;
dependency `com.unity.render-pipelines.high-definition` at the installed version (ProcFoliage
pins 17.4.0). Add to the root `CLAUDE.md` table, `index.html` `PKGS` array and DocFX csproj when
first shipped.

## 8. Testing

**EditMode (pure C#):**
- Determinism: same profile + seed ⇒ identical hash over segments, leaves, mesh arrays; different
  seed ⇒ different hash.
- `growth = 0` ⇒ zero visible segments and leaves.
- Continuity: living segment radius keys non-decreasing; nothing visible before birth; between
  close `growth` values the visible set changes only via births/deaths/pruning.
- Biology: pipe model (parent r² ≈ Σ children r², tolerance); leaves only on wood younger than
  `leafLifeYears`; deathYear ≥ birthYear; leaves die with their axis; mature crown within envelope.
- Mesh: no NaN, unit normals, valid segment ids, bounds == mature bounds.
- `TreeProfile.ClampToValidRanges` handles bad values.
- Compaction: live-leaf list equals brute-force filter; 0 GC allocations after warm-up.

**PlayMode:** prefab renders; sweeping `growth` 0→1 throws nothing and leaks no `GraphicsBuffer`;
`TreeWind` disabled ⇒ still air; background regenerate swaps without an empty frame.

## 9. Budgets (single hero tree, mature Oak preset)

| | Target |
|---|---|
| Simulation + bake | < 500 ms, background thread |
| Bark | ≤ 400k vertices, 1 draw (+ shadow) |
| Leaves alive | ≤ 30k, 1 instanced draw (+ shadow) |
| CPU per frame, `growth` changing | ≤ 0.3 ms |
| CPU per frame, `growth` static | ≈ 0 |
| GC allocations, steady state | 0 B/frame |

## 10. Verification reality

The UnityMCP bridge was unreachable when this spec was written (`ECONNREFUSED`). Code may be
authored and statically reviewed offline, but nothing is claimed to compile, render or pass until
driven in-editor. The implementation plan's **first task is the HDRP instancing spike** (§5.3),
run in the installed HDRP, gating everything built on it.

## 11. Out of scope (v1)

Hand-guided limbs, seasons/autumn colour, roots, acorns, other species, multiple trees, LODs,
runtime player-side editing, non-HDRP pipelines.

## 12. Risks

- HDRP procedural instancing + shadows + motion vectors in a hand-written shader → spike first;
  chunked-mesh fallback (§5.3).
- Simulation tuning: getting a convincing *parkland* silhouette from Palubicki parameters may take
  iteration; envelope + by-age curves are the main levers.
- Bark vertex count at hero twig density may exceed 400k → raise `minTwigRadius` cut-off for what
  becomes geometry vs. ends as leaf-bearing stub.
- Background-thread simulation must not touch Unity API (enforced by keeping `Simulation/` and
  `Meshing/` free of `UnityEngine.Object` usage; math types only). `AnimationCurve` is not
  thread-safe, so the main thread snapshots every `TreeProfile` curve into a sampled `float[]`
  (`SampledCurve`, 64 samples) before dispatch; the simulation only ever reads the snapshot.
