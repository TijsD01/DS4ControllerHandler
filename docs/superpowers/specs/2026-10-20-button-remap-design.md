# Button remapping — design

## Why

Rocket League and many other PC games accept a controller but have no (or poor) button remapping.
The app already sits between the physical pad and the game (virtual Xbox 360 / DualShock 4 via ViGEm),
so it can change which physical input produces which output before the game sees it.

## Goals

A per-controller "Knoppen" screen where every physical input can be pointed at a different output:

1. **Button to button.** Any physical button to any virtual pad button (e.g. L1 to the button the game uses for boost).
2. **Button to trigger.** A button presses a virtual L2/R2 fully (100%) while held.
3. **Trigger to button.** A physical L2/R2 presses a virtual button once past a threshold.
4. **Trigger to trigger.** Swap L2 and R2 (analog value kept).
5. **Swap sticks.** Left and right stick swap.
6. **Keyboard keys.** Any physical button or trigger can press a keyboard key instead.
7. **Disable.** Any input can be set to "Uit" (does nothing).

Remaps are saved per profile (same as Afstellen), so they survive restarts and travel with copy/export/import.

## Non-goals (skipped on purpose)

- Per-game automatic profile switching.
- Combos, macros, turbo, hold-vs-tap, one input to several outputs.
- Stick to d-pad / d-pad to stick, mouse output, gyro, touchpad as stick.
- Remapping the outputs of other tools.

## Anti-goals

- Adding latency or allocations to the read path. The read path today allocates nothing per report; keep it that way.
- A stuck output: a key or button that stays pressed after release, unplug, profile change or app exit.
- Breaking existing profiles. Old profile files without remap data load as "no remaps".

## Behaviour rules

- **Default = identity.** An input with no remap behaves exactly as today.
- **Sources:** all buttons in `Buttons` except `L2Digital`/`R2Digital` (those follow the analog triggers), plus analog `L2` and `R2`.
  `Mute` (DualSense only) and `TouchpadClick` are sources too.
- **Targets:** each virtual pad button (Cross, Circle, Square, Triangle, L1, R1, L3, R3, Share, Options, PS,
  TouchpadClick, d-pad 4×), `L2`, `R2`, a keyboard key, or `Uit`.
- **One target per source.** Several sources may share one target: buttons OR together, triggers take the max.
- A source that is remapped away no longer produces its original output (unless another source maps to it).
- **Trigger to button / key:** pressed while the calibrated, tuned trigger value is ≥ 0.5. *(Claude's call.)*
- **Button to trigger:** trigger value 1.0 while held.
- **Remap runs after calibration and tuning.** Calibration and Afstellen still act on the physical stick/trigger,
  so learned trigger endpoints keep working on a swapped trigger.
- **Swap sticks** swaps the final left/right stick values (after tuning).
- **Keyboard output** uses Windows `SendInput` with scan codes (games often ignore virtual-key-only input).
  Key down on press edge, key up on release edge. All held keys are released when the controller disconnects,
  the remap or profile changes, the session is disposed, or the app exits.
- When a key is the target, the game still gets the pad report without that source's button.

## Screen

New tab **Knoppen** on the controller page, next to Live / Kalibratie / Afstellen. Dutch text, tooltips via `Tips.cs`,
styling from the existing `Theme.xaml` (IBM Plex Sans, existing brushes, cards, buttons).

- Left: the existing controller picture (`ControllerView`), lighting up what is pressed, so the user sees which row is which.
- Right: one row per source: source name, an arrow, and a dropdown with the target grouped as
  *Controllerknoppen*, *Triggers*, *Toetsenbord…*, *Uit*. Rows that differ from default get an accent marker
  and a small *Standaard* button.
- **Press to find:** pressing a physical button while on this tab highlights and scrolls to its row.
- **Picking a key:** choosing *Toetsenbord…* asks "Druk op een toets" and captures the next keyboard key (Esc cancels).
- Target names follow the output type: Xbox names (A, B, X, Y, LB, RB, Back, Start…) in Xbox mode,
  PlayStation names (✕, ○, □, △, L1, R1…) in DualShock 4 mode.
- A **Sticks wisselen** toggle above the list, and **Alles standaard** (with confirmation, like *Kalibratie resetten*).
- Changes apply live and are saved with the profile, exactly as Afstellen works today (same generation guard).

## Where it fits in the code (for the builder)

- Data lives in the profile next to `InputTuning` (e.g. a `ButtonRemap` property on `InputTuning`, or its own property on
  `CalibrationProfile`). Persist only non-default entries; `InputTuningJsonConverter` / profile JSON must read old files.
- Pure remap logic in `DS4Handler.Core` (new `Mapping/` folder): `CalibratedState` in, remapped `CalibratedState` out,
  plus the set of keys that should be held. No allocations per call. Unit tests in `DS4Handler.Core.Tests`.
- `ControllerSession` applies it in the read loop before `SubmitVirtual`, and owns key-down/up edges plus release-all.
- `XboxMapper` / `Ds4Mapper` stay unchanged: remapping happens in PlayStation button space before them.
- UI: `ViewModels/RemapViewModel.cs` + `Views/RemapPanel.xaml`, hosted in the new tab, following `TuningViewModel` / `TuningPanel`.

## Left to the builder

Exact type shapes, JSON field names, which scan-code table to use, row ordering, control templates.
