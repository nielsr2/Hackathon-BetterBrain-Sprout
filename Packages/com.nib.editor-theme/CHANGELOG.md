# Changelog

## [0.1.0] - 2026-08-11

### Added
- Design tokens (`Variables.uss`): per-family accent (botanical / light / signal),
  semantic status colors, raised/inset surfaces; dark defaults with light-skin overrides.
- Shared control styles (`Controls.uss`): section headers, cards, pills, accent button,
  row/grow helpers, TabView fill fix.
- `NibTheme.Apply(root, family)` fail-soft entry point.
- `StatusPill` `[UxmlElement]` with `Level` attribute (Off / Ok / Warn / Error).
