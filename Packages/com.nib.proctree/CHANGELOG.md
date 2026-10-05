# Changelog

All notable changes to this package are documented here. Format: [Keep a Changelog](https://keepachangelog.com), versioning: [SemVer](https://semver.org).

## [0.1.0] - 2026-10-04

### Added
- `Nib.ProcTree.Core` (no Unity references): year-by-year bud-driven growth simulation
  (space markers, Borchert–Honda vigor, tropisms, relative self-pruning, tip-zone leaves,
  pipe-model thickness) and a GPU bake (bark tubes, leaf slots, live-leaf compactor).
- HDRP shaders `ProcTree/Bark` and `ProcTree/Leaf`: growth replayed in the vertex stage from GPU
  buffers in every pass; hierarchical wind; leaf backlight translucency.
- `ProceduralTree` component (one `growth` float, 0 = nothing → 1 = mature oak), `TreeWind`,
  `GrowthAnimator`, `TreeProfile` asset (Oak (Parkland) preset).
- UI Toolkit inspectors, code-generated bark + oak-leaf textures, **Tools ▸ Proc Tree ▸ Setup
  Project**, `GameObject ▸ Nib ▸ Proc Tree ▸ Oak`, Growth Demo sample.
- Offline harnesses: `Tests~/CoreHarness` (runs the Core test suite with `dotnet`) and
  `Tests~/UnityCompile` (compiles Core/Runtime/Editor against Unity's module DLLs).
