# Button Remapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Per-controller "Knoppen" tab that remaps physical inputs to other pad buttons, triggers, keyboard keys or nothing, plus a stick swap, saved in the profile.

**Architecture:** Remap data is a new `Remap` section on `InputTuning`, so it rides the existing tuning save/copy/export/generation pipeline. A pure, allocation-free `ButtonRemapper` in Core turns a tuned `CalibratedState` into the remapped state plus a bitmask of sources held as keys. `ControllerSession` applies it before `SubmitVirtual` and drives a small `KeyboardOutput` (`SendInput` scan codes) with press/release edges and release-all.

**Tech Stack:** .NET 8, C#, WPF, xUnit, ViGEm, Win32 `SendInput`.

**Spec:** `docs/superpowers/specs/2026-10-20-button-remap-design.md`

## Global Constraints

- UI copy is Dutch; every interactive control gets a tooltip constant in `src/DS4Handler.App/ViewModels/Tips.cs`.
- Styling only from `src/DS4Handler.App/Themes/Theme.xaml` (IBM Plex Sans, existing brushes/styles); no new dependencies.
- Read path stays allocation-free per report.
- Old profile files (no `Remap`) load as identity; a malformed `Remap` section loads as identity without losing the rest of the profile.
- Trigger-as-button threshold: `0.5f` on the tuned trigger value.
- Button-to-trigger value: `1f`.
- No key or button may stay pressed after release, disconnect, remap change, profile change, dispose or app exit.
- Build: `dotnet build DS4ControllerHandler.sln -c Debug`; tests: `dotnet test tests/DS4Handler.Core.Tests`.

## Review Focus

1. Remap edited while a remapped key is held: the key must be released (session diff against new map, not the old scan code lost). Test in Task 3 via `KeyEdgeTracker`.
2. Two sources onto one trigger (R2 physical + Cross→R2): value is max, not sum or last-wins. Test in Task 1.
3. A source remapped to a key while the same pad output is also produced by another source: pad output stays pressed from the other source only. Test in Task 1.
4. Hand-edited/garbage `Remap` JSON (unknown source names, out-of-range scan code, wrong types): ignored entry, rest kept. Test in Task 2.
5. Profile copy/export/import carries the remap; the save fingerprint changes when only the remap changes. Test in Task 2.

---

### Task 1: Core remap model and remapper  *(Opus senior agent)*

**Files:**
- Create: `src/DS4Handler.Core/Mapping/ButtonRemap.cs`
- Create: `src/DS4Handler.Core/Mapping/ButtonRemapper.cs`
- Test: `tests/DS4Handler.Core.Tests/ButtonRemapperTests.cs`

**Interfaces — Produces:**
```csharp
namespace DS4Handler.Core.Mapping;

// Order is persisted by name, not value. Used as both source and pad target. Mute is source-only.
public enum PadControl { Cross, Circle, Square, Triangle, L1, R1, L2, R2, Share, Options, L3, R3, PS, TouchpadClick, Mute, DpadUp, DpadDown, DpadLeft, DpadRight }

public enum RemapKind { Same, Pad, Key, None }

// Pad: Output is the target. Key: ScanCode is a Set-1 scan code, 0xE0xx for extended keys.
public readonly record struct RemapTarget(RemapKind Kind, PadControl Output = default, ushort ScanCode = 0)
{
    public static RemapTarget Same => default;
    public static RemapTarget Disabled => new(RemapKind.None);
    public static RemapTarget ToPad(PadControl c) => new(RemapKind.Pad, c);
    public static RemapTarget ToKey(ushort scanCode) => new(RemapKind.Key, default, scanCode);
}

// Immutable, value-equal (Equals/GetHashCode over all targets + SwapSticks).
public sealed class ButtonRemap : IEquatable<ButtonRemap>
{
    public static ButtonRemap Identity { get; }
    public const int Count = 19;                     // number of PadControl values
    public bool SwapSticks { get; }
    public bool IsIdentity { get; }                  // all Same (or Pad to itself) and no swap
    public RemapTarget this[PadControl source] { get; }
    public ButtonRemap With(PadControl source, RemapTarget target);   // Pad->itself normalizes to Same; Pad->Mute rejected (ArgumentException)
    public ButtonRemap WithSwapSticks(bool swap);
    public static Buttons ToButtons(PadControl c);   // L2->L2Digital, R2->R2Digital, others 1:1
}

public static class ButtonRemapper
{
    public const float TriggerPressThreshold = 0.5f;
    // heldKeySources: bit (int)source set when that source is pressed and mapped to a key.
    public static CalibratedState Apply(in CalibratedState s, ButtonRemap remap, out uint heldKeySources);
}
```

- [ ] **Step 1: Write failing tests** in `ButtonRemapperTests.cs`:
  - `Identity_ReturnsInputUnchanged` — random states, `Apply(s, ButtonRemap.Identity, out k)` equals `s`, `k == 0`.
  - `ButtonToButton_MovesBit` — L1→Cross: input `Buttons.L1` gives `Buttons.Cross` only.
  - `SwapCrossAndCircle` — both remapped; pressing Cross gives Circle, pressing Circle gives Cross.
  - `ButtonToTrigger_FullWhileHeld` — Cross→R2: input Cross, R2=0 gives R2 == 1f and no Cross bit.
  - `TriggerToButton_Threshold` — R2→Square: R2=0.49 gives no Square, R2=0.5 gives Square; output R2 == 0.
  - `TriggerSwap_KeepsAnalog` — L2→R2, R2→L2: (L2=0.3,R2=0.8) gives (L2=0.8,R2=0.3).
  - `TwoSourcesOneTrigger_TakesMax` — Cross→R2 with physical R2=0.4 and Cross held gives R2 == 1f; Cross released gives 0.4.
  - `KeyTarget_SetsBit_RemovesPadOutput` — Square→Key(0x39): Square held gives `k == 1u << (int)PadControl.Square`, no Square bit.
  - `KeyTarget_OtherSourceStillProducesPad` — Square→Key, Circle→Square: both held gives Square bit set, key bit set.
  - `Disabled_ProducesNothing` — PS→None: PS held gives no PS bit.
  - `SwapSticks_SwapsAxes` — (LX,LY,RX,RY)=(0.1,0.2,0.3,0.4) gives (0.3,0.4,0.1,0.2).
  - `DigitalTriggerBits_FollowRemappedTriggers` — output `L2Digital`/`R2Digital` set iff output L2/R2 > 0 (input digital bits are ignored as sources).
  - `With_PadToSelf_IsSame`, `With_ToMute_Throws`, `Equality_ByValue` (two separately built equal remaps are `Equals` and same hash).
  - `Apply_DoesNotAllocate` — `GC.GetAllocatedBytesForCurrentThread()` delta 0 over 10 000 calls with a non-identity remap.

- [ ] **Step 2: Run** `dotnet test tests/DS4Handler.Core.Tests --filter ButtonRemapperTests` — Expected: build FAIL (types missing).
- [ ] **Step 3: Implement** both files. Store targets in a private `RemapTarget[]` of length `Count`; `Identity` short-circuits `Apply` (return `s`, `k=0`). Precompute nothing mutable; class is immutable.
- [ ] **Step 4: Run** same filter, then full `dotnet test tests/DS4Handler.Core.Tests` — Expected: all PASS.
- [ ] **Step 5: Commit** `feat(core): button remap model and remapper`.

### Task 2: Persist remap in tuning/profile  *(agy)*

**Files:**
- Modify: `src/DS4Handler.Core/Tuning/InputTuning.cs` (add `public ButtonRemap Remap { get; init; } = ButtonRemap.Identity;`)
- Modify: `src/DS4Handler.Core/Tuning/InputShaper.cs:252-264` (`Sanitize` keeps `Remap`, null → `Identity`)
- Modify: `src/DS4Handler.Core/Tuning/InputTuningJsonConverter.cs` (read/write `Remap`)
- Modify: `src/DS4Handler.App/Engine/ControllerSession.cs` `Fingerprint.AddTuning` (add `h.Add(t.Remap)`)
- Test: `tests/DS4Handler.Core.Tests/RemapPersistenceTests.cs`

**Interfaces — Consumes:** Task 1 `ButtonRemap`, `RemapTarget`, `PadControl`, `RemapKind`.
**Produces:** `InputTuning.Remap`. JSON shape (only non-`Same` entries written):
```json
"Remap": { "SwapSticks": true, "Map": { "L1": { "Pad": "Cross" }, "Square": { "Key": 57 }, "PS": "None" } }
```

- [ ] **Step 1: Failing tests:**
  - `RoundTrip_PreservesRemap` — profile with L1→Pad Cross, Square→Key 57, PS→None, SwapSticks; save/load via `ProfileStore` gives an `Equals` remap.
  - `OldProfile_WithoutRemap_LoadsIdentity`.
  - `GarbageRemap_IgnoredEntriesKeepRest` — `"Map": {"Bogus": {"Pad":"Cross"}, "L1": {"Pad":"Mute"}, "R1": {"Key": 70000}, "Cross": {"Pad":"Circle"}}` loads with only Cross→Circle; `"Remap": 5` loads identity; rest of tuning intact.
  - `Transfer_CarriesRemap` — `ProfileTransfer` export/import keeps the remap.
  - `Sanitize_NullRemap_IsIdentity`.
- [ ] **Step 2: Run** `--filter RemapPersistenceTests` — Expected FAIL.
- [ ] **Step 3: Implement.** Key scan code valid range `1..0xE0FF`; reject others.
- [ ] **Step 4: Run** full test project + `dotnet build DS4ControllerHandler.sln` — Expected PASS.
- [ ] **Step 5: Commit** `feat: persist button remap in profile tuning`.

### Task 3: Apply remap in session + keyboard output  *(Opus senior agent)*

**Files:**
- Create: `src/DS4Handler.Core/Mapping/KeyEdgeTracker.cs` (pure, testable diff logic)
- Create: `src/DS4Handler.App/Engine/KeyboardOutput.cs` (`SendInput` P/Invoke)
- Modify: `src/DS4Handler.App/Engine/ControllerSession.cs` (read loop ~line 703-717, profile/tuning change paths, `Dispose`)
- Test: `tests/DS4Handler.Core.Tests/KeyEdgeTrackerTests.cs`

**Interfaces — Consumes:** `ButtonRemapper.Apply`, `InputTuning.Remap`.
**Produces:**
```csharp
namespace DS4Handler.Core.Mapping;
// Remembers the scan code each held key was pressed with, so a remap change releases the right key.
public sealed class KeyEdgeTracker
{
    // Calls press/release for each change. Allocation-free.
    public void Update(uint heldKeySources, ButtonRemap remap, Action<ushort> press, Action<ushort> release);
    public void ReleaseAll(Action<ushort> release);
}
namespace DS4Handler.App.Engine;
internal static class KeyboardOutput { public static void Press(ushort scanCode); public static void Release(ushort scanCode); }
```

- [ ] **Step 1: Failing tests** `KeyEdgeTrackerTests`:
  - `PressThenRelease_OneEachEdge`.
  - `HeldAcrossReports_NoRepeat`.
  - `RemapChangedWhileHeld_ReleasesOldScanCodePressesNew` — Square→Key 0x39 held, then remap Square→Key 0x1E still held: release(0x39), press(0x1E).
  - `RemapToPadWhileHeld_ReleasesKey`.
  - `ReleaseAll_ReleasesEveryHeldKey_ThenNothing`.
  - `TwoSourcesSameKey_ReleaseOnlyWhenBothUp` — key ref-counted by scan code.
- [ ] **Step 2: Run** `--filter KeyEdgeTrackerTests` — Expected FAIL.
- [ ] **Step 3: Implement** tracker; `KeyboardOutput` uses `SendInput` with `KEYEVENTF_SCANCODE` (+`KEYEVENTF_EXTENDEDKEY` when high byte is `0xE0`) and `KEYEVENTF_KEYUP` on release. In the session: after `calibration.Process`, under the same lock read `calibration.Tuning.Remap`, call `ButtonRemapper.Apply`; store the remapped state as `_output`; call the tracker outside `_sync` using cached static delegates (no per-report allocation). Release all keys on: read-loop exit (`finally`), profile switch/reset paths that replace `_calibration`, and `Dispose` (before `DisconnectVirtualPad`). Key events run only on the read thread except `Dispose`/loop exit, which happen after the loop stopped.
- [ ] **Step 4: Run** full tests + solution build — Expected PASS.
- [ ] **Step 5: Commit** `feat: apply button remap and keyboard output in session`.

### Task 4: Knoppen tab UI  *(Claude, with design skill)*

**Files:**
- Create: `src/DS4Handler.App/ViewModels/RemapViewModel.cs`
- Create: `src/DS4Handler.App/Views/RemapPanel.xaml` (+ `.xaml.cs`)
- Modify: `src/DS4Handler.App/Views/ControllerPage.xaml:393-397` (new `TabItem Header="Knoppen"` after Afstellen)
- Modify: `src/DS4Handler.App/ViewModels/ControllerViewModel.cs` (expose `Remap` view model, feed live snapshot)
- Modify: `src/DS4Handler.App/ViewModels/Tips.cs`, `README.md` (new section "Knoppen"), `CHANGELOG.md` (Unreleased entry)

**Interfaces — Consumes:** `ControllerSession.Tuning`, `TuningGeneration`, `ApplyTuning(InputTuning, int)`, `CanSaveTuning`; `InputTuning.Remap with`; `ButtonRemap.With/WithSwapSticks/Identity`; snapshot raw buttons for press-to-find; output mode (Xbox / DS4) for target names.

Behaviour (from spec): one row per `PadControl` source (Mute row only for DualSense); dropdown grouped Controllerknoppen / Triggers / Toetsenbord… / Uit; changed rows marked + per-row *Standaard*; *Sticks wisselen* toggle; *Alles standaard* with existing confirm dialog; press physical button → highlight + scroll to its row; *Toetsenbord…* captures next key (Esc cancels) via `MapVirtualKey(vk, MAPVK_VK_TO_VSC_EX)`, label via `GetKeyNameText`; target names Xbox-style in Xbox mode, PlayStation glyphs in DS4 mode; applies live via `ApplyTuning` with the generation guard, reload on mismatch (same as `TuningViewModel`).

- [ ] **Step 1:** Load the design skill, read `TuningPanel.xaml`, `TuningViewModel.cs`, `Theme.xaml`, `ControllerView.cs`.
- [ ] **Step 2:** Build view model + panel + tab.
- [ ] **Step 3: Run** `dotnet build DS4ControllerHandler.sln` (0 warnings new) and full tests — Expected PASS.
- [ ] **Step 4:** Launch app, screenshot the Knoppen tab (light/dark if theme supports), fix layout issues.
- [ ] **Step 5: Commit** `feat(ui): Knoppen tab for button remapping`.

### Task 5: Whole-branch review  *(Opus reviewer)*

- [ ] Review `git diff main...HEAD` against spec + Review Focus; fix findings; full build + tests green; commit.
