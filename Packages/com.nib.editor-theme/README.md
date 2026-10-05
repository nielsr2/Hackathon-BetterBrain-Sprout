# Nib Editor Theme

Shared UI Toolkit theme for Nib editor tools. Replaces the per-package copies of
`Variables.uss` / `Controls.uss` with one token source.

## Usage

Add the dependency to your package.json:

```json
"dependencies": { "com.nib.editor-theme": "0.1.0" }
```

In `CreateGUI()` (reference `Nib.EditorTheme.Editor` in your editor asmdef):

```csharp
Nib.EditorTheme.NibTheme.Apply(rootVisualElement, NibFamily.Botanical);
```

This attaches the token + control stylesheets and the family accent class. Loading is
fail-soft: a consumer still renders (unthemed) if this package is absent.

## Contents

- `Editor/Styles/Variables.uss` — design tokens: per-family accent (`--nib-accent`),
  semantic status colors, raised/inset surfaces. Dark defaults + light overrides.
- `Editor/Styles/Controls.uss` — shared classes: `.nib-section-header`, `.nib-card`,
  `.nib-pill` (+ level modifiers), `.nib-accent-button`, `.nib-row`, `.nib-grow`,
  and the TabView fill fix.
- `StatusPill` — `[UxmlElement]` status chip with a `Level` attribute.

## Rules

One accent per window (pick the family that fits). Base surfaces, borders, and text always
come from `--unity-colors-*`; never hard-code hex in a consumer. See `EDITOR_UI_DESIGN_SPEC.md`
at the UnityPackages root for the full normative spec.
