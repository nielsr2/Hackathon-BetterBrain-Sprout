# Proc Tree — Oak: implementation plan

**Spec:** `docs/superpowers/specs/2026-10-04-proctree-oak-design.md` (approved 2026-10-04)
**Goal:** a procedurally generated parkland oak whose single `growth` float (0 → 1) replays the
tree's life live on the GPU, from nothing to seedling to mature crown with leaves.
**Execution:** inline (executing-plans), TDD. Commit after each task in this repo only.

## Plan-level decisions (refinements of the spec, not reversals)

1. **`Nib.ProcTree.Core` assembly, `noEngineReferences: true`.** Simulation + meshing arrays live
   in a Unity-free assembly using `System.Numerics` (`Vector3`, `Quaternion`). This *enforces* the
   spec's thread-safety rule (§12) at compile time, and lets the Core be compiled and tested
   **offline with `dotnet`** while the Unity bridge is down. Runtime converts to
   `UnityEngine.Vector3` at the Mesh/GraphicsBuffer boundary.
2. **Offline harness `Tests~/CoreHarness/`** (ignored by Unity because of the `~`): a net10 console
   project that compiles `Core/**/*.cs` + `Tests/Editor/Core/**/*.cs` against Unity's bundled
   `nunit.framework.dll`, runs every `[Test]` by reflection, and can write PNG silhouettes of the
   tree at chosen `growth` values for visual tuning. The same test files run in Unity's Test Runner.
3. **Radius keys: 16 keys on one shared quadratic year grid** (`year_k = years · (k/15)²`) instead of
   8 per-segment keys. Dense early (saplings thicken visibly), one grid for every segment, simpler
   shader. Radius is a running max over keys (wood never shrinks, even after pruning reduces load).
4. **Shader receives `_TreeYear` / `_PrevTreeYear`** (CPU applies `growthCurve` once) instead of
   `_Growth` + a curve lookup on the material. Same behaviour, simpler shader.
5. **Ordering vs. the spike.** The spec puts the HDRP instancing spike first. It gates *rendering*;
   the Core (Phases 1–2) does not depend on it and can be built and verified offline now. The spike
   is the first task of Phase 3 and nothing in Phase 3+ is built before it passes.

## Phase 0 — Scaffold

### T0.1 Package skeleton
Files: `package.json` (0.1.0, `unity` = `6000.3`, HDRP dep, sample entry), `CHANGELOG.md`,
`README.md`, asmdefs:
- `Core/Nib.ProcTree.Core.asmdef` (noEngineReferences, no refs)
- `Runtime/Nib.ProcTree.Runtime.asmdef` (→ Core, HDRP runtime)
- `Editor/Nib.ProcTree.Editor.asmdef` (→ Runtime, Core; Editor only)
- `Tests/Editor/Nib.ProcTree.Tests.Editor.asmdef` (→ Core, Runtime, Editor; test assemblies)
- `Tests/Runtime/Nib.ProcTree.Tests.Runtime.asmdef` (→ Runtime, Core; test assemblies)

Commit: `chore: package skeleton`.

### T0.2 Offline harness
`Tests~/CoreHarness/CoreHarness.csproj` + `Runner.cs` (reflection runner: `[Test]`, `[TestCase]`
basic support, `[SetUp]`; prints pass/fail, exit code = failures) + `Png.cs` (minimal PNG writer
via `ZLibStream`). One trivial test proves the loop end-to-end.
Verify: `dotnet run -c Release --project Tests~/CoreHarness` → `1 passed`.
Commit: `test: offline Core harness`.

## Phase 1 — Simulation (Core, offline-verified)

### T1.1 Foundations
- `Core/SeededRng.cs`: copied from ProcFoliage (namespace `Nib.ProcTree.Core`); add
  `UnitVector()`, `InsideUnitSphere()`.
- `Core/SampledCurve.cs`: `float[] samples` over [0,1], `Evaluate(t)` linear, clamped;
  `FromFunc(Func<float,float>, n)`; `Linear`, `Constant(v)`.
- `Core/TreeParams.cs`: plain class, every simulation parameter (spec §4) with Oak (Parkland)
  defaults, `SampledCurve` fields for by-age curves, `Clamp()`, `ComputeHash()` (stable FNV over
  fields — used by the bake to detect staleness).
Tests: RNG determinism + seed divergence; curve endpoints/interp/clamp; params clamp fixes NaN,
negatives, inverted ranges; hash stable and changes when a field changes.

### T1.2 Envelope + space markers
- `Core/Simulation/CrownEnvelope.cs`: `Contains(p, age01)`; ellipsoid whose height/width scale by
  `heightByAge` / `widthByAge` curves; bottom at `crownBaseFraction · height(age)`.
- `Core/Simulation/MarkerCloud.cs`: N markers, each with `appearAge01` drawn from `[0,1]` and a
  point drawn inside `Envelope(appearAge01)` (rejection sampling from RNG) → young envelopes get
  dense markers, mature crown sparse. `Visible(i, age01)` = not consumed and `appearAge01 ≤ age`.
- `Core/Simulation/SpatialHash.cs`: uniform grid over ints, reusable buffers.
Tests: deterministic; all markers inside the union; young-age markers exist close to the ground;
spatial hash query == brute force.

### T1.3 Tree graph types
`Core/TreeHistory.cs`: `Segment`, `Axis`, `Leaf` (spec §4.6, `System.Numerics`), `TreeHistory`
(lists, `years`, `Bounds` min/max, `KeyYears[16]`). `Bud` internal to the simulator.
Tests: none beyond compile (pure data).

### T1.4 Year step: light, vigor, extension
`Core/Simulation/GrowthSimulator.cs` `Run(TreeParams, int seed) → TreeHistory`:
1. Year 0: root bud at origin, direction up.
2. Each year y=1..Y (age = y/Y):
   - build spatial hash of buds; each visible marker → closest bud within its perception cone
     (`perceptionAngle`, `perceptionRadius`); bud Q = count, optimal dir = normalized Σ(marker−bud).
     Non-cluster lateral buds' Q × `lateralLightFactor`. Trunk terminal bud gets `+apicalBaseLight`
     while below the active envelope top (keeps the seedling/sapling growing).
   - basipetal Q per segment (reverse creation order, children before parents).
   - acropetal Borchert–Honda: v_root = `vigorScale` · Q_root; at each segment split v between main
     continuation and laterals with `apicalDominance` λ.
   - each bud with v ≥ 1 grows n = min(floor(v), `maxInternodesPerYear`) internodes. Segment k of n:
     `birth = (y−1) + flush·k/n`, `growDuration = flush/n` (flush ≈ 0.4 of the year — continuous
     in-year extension). Direction per internode = normalize(prev·`straightness` +
     optimal·`tropismSpace` + up·`tropismGravity(age)` + rng.UnitVector()·`gnarliness`).
     Length = `internodeLength · internodeLengthByAge(age)`.
   - lateral bud at each new internode end, rotated `phyllotaxisAngle`, tilted `branchingAngle`;
     marked cluster if within last `terminalClusterSize` internodes of the shoot; terminal bud at tip.
   - consume markers within `occupancyRadius` of each new internode end.
   - buds without Q for `budDeathYears` die.
Tests: deterministic (hash of history); seed changes tree; year-0 history (growth 0) empty;
after year 1 there is a single upright seedling shoot; height monotonic with years; mature
bounds within envelope (+ tolerance); **young tree (age 0.25) taller-than-wide, mature wider than
tall** (the parkland silhouette check); every segment's start == parent's end; birth ≥ parent end
time (a child never appears before its parent finishes).

### T1.5 Self-pruning + leaves
- Each year: for each non-trunk axis, light ratio = Q(axis base) / segments(axis subtree); below
  `pruneLightThreshold` for `pruneYears` consecutive years ⇒ axis subtree dies: `deathYear = y`
  (optionally keep first `pruneStubLength` m of the axis alive as a stub). Dead wood excluded from
  Q, vigor, and bud growth.
- Leaves: when an internode finishes extending, emit leaves (`leavesPerInternode`, weighted toward
  shoot tips) at its end: `birth = seg.birth + seg.growDuration`, `death = min(birth +
  leafLifeYears, axisDeath)`, rotation around stem at phyllotaxis angle, blade tilted outward/up
  with jitter, `size` jitter, tint, phase.
Tests: deathYear ≥ birthYear everywhere; leaves die no later than their axis; at every sampled year
alive leaves sit only on wood younger than `leafLifeYears` (+flush); some pruning happens on the
Oak preset (low young limbs die); leaf count alive at age 1 in [10k, 40k].

### T1.6 Pipe-model radius keys
`Core/Simulation/PipeModel.cs`: indicator(axis, key) = axis alive at `KeyYears[k]` and its first
segment born ≤ it; counts propagated tip→root in reverse creation order; `r = minTwigRadius ·
count^(1/pipeExponent)`; running max over keys; trunk-base `rootFlare` term on the first
`rootFlareHeight` m.
Tests: parent r² ≈ Σ children r² at mature key (tolerance 5%, before running-max effects);
keys non-decreasing; trunk base Ø at age 1 within 0.8–1.6 m.

### T1.7 Tuning pass + perf
Harness `--render` writes PNG side/top silhouettes at growth 0.05, 0.15, 0.3, 0.5, 0.75, 1.0.
Tune Oak defaults until: seedling ≠ empty at 0.02, sapling is a whip with a few laterals, young tree
narrow, mature broad parkland dome with low spreading limbs. Perf test: mature Oak Run() < 500 ms
Release in harness (assert < 1500 ms in the test to stay robust on slow machines; log actual).

## Phase 2 — Meshing + reveal (Core, offline-verified)

### T2.1 Bark arrays
`Core/Meshing/BarkMeshBuilder.cs` → `BarkMeshArrays { Vector3[] centerline, radial, tangent;
Vector2[] uv0; Vector4[] uv1; Vector4[] color; int[] indices }` and `SegmentGpu[]`
(start, end, birth, growDuration, death, 16 radius keys — blittable struct, 4·(3+3+3+16)=100 B).
One tube per axis; ring sides from mature radius; parallel-transport ring frames; branch collar
ring (radius × `collarFlare` slightly inside parent); tip cap vertex.
Tests: no NaN; unit radial/tangent; indices in range; every vertex's segmentId valid; vertex count
under 400k for Oak.

### T2.2 Leaf arrays + compactor
`Core/Meshing/LeafMeshBuilder.cs` (16-vert folded oak leaf, UV0 for the atlas),
`LeafGpu` blittable struct, `Core/Meshing/LiveLeafCompactor.cs` (leaves sorted by birth; output
`uint[] liveIndices`, `int count` for a year; preallocated; no allocation per call).
Tests: compactor == brute force at many years; growth 0 ⇒ 0; zero GC allocations after warm-up
(`GC.GetAllocatedBytesForCurrentThread`).

### T2.3 CPU reference of the GPU reveal
`Core/Meshing/GrowthReveal.cs`: C# mirror of `TreeGrowth.hlsl` (vertex position for a year;
leaf scale for a year). The HLSL is written to match it line-for-line.
Tests: year 0 ⇒ all vertices collapsed (zero-area triangles everywhere); visible bark area
non-decreasing over years until first pruning; revealed vertices lie within mature bounds; a
vertex never moves except along its own segment's growth front and radially.

## Phase 3 — HDRP rendering (needs Unity; spike first)

### S1 Spike — instanced leaves in hand-written HDRP shader
Minimal `ProcTree/Leaf` (unlit-colour + growth scale) drawn with `Graphics.RenderMeshIndirect`
from a `GraphicsBuffer` of leaves + live-index buffer; verify in the installed HDRP: renders,
casts and receives shadows, motion vectors OK while `_TreeYear` animates. **Decide: instanced or
chunked-mesh fallback** (spec §5.3); record in `CHANGELOG.md`/README.

### T3.1 Lighting include + Bark shader
Copy `FrondLighting.hlsl`, property/data scaffolding from ProcFoliage into `Shaders/Include/`
(namespaced `TREE_`), add `TreeGrowth.hlsl` (from T2.3), `ProcTree/Bark` (Depth/Shadow/Forward/
Motion; `StructuredBuffer<SegmentGpu>`; bark UV.u from actual circumference).

### T3.2 Leaf shader (full)
Translucency, alpha-cut, double-sided, hue variation, growth scale, `_PrevTreeYear` motion vectors.

### T3.3 Wind
`TreeWind.hlsl` (trunk/branch/leaf layers, spec §6.3), `Runtime/Components/TreeWind.cs`.

## Phase 4 — Runtime components

### T4.1 Data assets
`Runtime/Data/TreeProfile.cs` (SO wrapping `TreeParams` + `AnimationCurve`s; `ToParams()`
snapshots curves into `SampledCurve` on the main thread), `Runtime/Data/TreeBake.cs` (SO: bark
Mesh, `SegmentGpu[]`, `LeafGpu[]`, leaf Mesh, params hash, seed, years, bounds).

### T4.2 `ProceduralTree`
Applies a bake: MeshFilter/MeshRenderer for bark, segment `GraphicsBuffer`, leaf buffers +
`RenderMeshIndirect` each frame, `_TreeYear`/`_PrevTreeYear` via MPB, compaction only when year
changes, background regenerate (`Task.Run` Core work → main-thread apply; newest request wins),
buffers released on disable/destroy.

## Phase 5 — Editor + ship

### T5.1 Generated textures + materials
Bark albedo/normal (vertical fissures, colour noise) and oak-leaf atlas (lobed alpha, veins,
thickness) generated in code; `MaterialFactory` for both shaders.

### T5.2 Inspectors (UI Toolkit)
`ProceduralTreeInspector` (growth slider + year readout, regenerate, 🎲, progress, stats),
`TreeProfileInspector` (grouped).

### T5.3 Setup, prefab, sample, menus
`Window/Nib/Proc Tree/Setup Project`, `GameObject/Nib/Proc Tree/Oak`, `Oak.prefab` with baked
`TreeBake`, `Samples~/GrowthDemo` with `GrowthAnimator` + rotating sun.

### T5.4 PlayMode tests + docs
Spec §8 PlayMode list; README usage; CHANGELOG 0.1.0. (Root `CLAUDE.md` table, `index.html`
`PKGS`, DocFX csproj live outside this repo — ask before editing.)

## Verification log
Each task records here whether it was verified offline (harness), in Unity, or not yet.

| Task | Status | Evidence |
|---|---|---|
| T0.1 skeleton | done, not yet imported in Unity | asmdefs authored |
| T0.2 harness | verified offline | `dotnet run` runs Core tests |
| T1.1 foundations | verified offline | 8 tests pass |
| T1.2 envelope/markers | verified offline | 7 tests pass |
| T1.3–T1.6 simulator, pruning, leaves, pipe model | verified offline | 14 tests pass; silhouettes seeds 1 + 7 inspected (whip → narrow young → wide dome) |
| T1.7 tuning + perf | verified offline | mature Oak 319 ms warm (60.8k segments ever, ~20k alive at 1.0; ~24k leaves alive at 1.0) |
| T2.1–T2.3 bark arrays, leaf arrays + compactor, CPU reveal | verified offline | 10 tests pass; 276k bark verts (linear ring sides, no seam on ≤4-sided twigs); simulate+bake 442 ms; compactor 0 B/call |
