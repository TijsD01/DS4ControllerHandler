# Audit Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.

**Goal:** Fix the four findings of the 2026-10-20 ponytail audit.

**Architecture:** Four small, independent changes. No new dependencies, no new layers.

**Tech Stack:** .NET 8, C#, WPF, xUnit.

**Spec:** none; the audit report findings 1–4 (quoted per task) are the requirements.

## Global Constraints

- Smallest complete change. No new dependencies, interfaces, config or options beyond what a task names.
- Existing comments stay unless the task changes what they describe.
- Read path (`ControllerSession.ReadLoop` / `SubmitVirtual`) stays allocation-free per report.
- Tests must never touch real drivers (HidHide, ViGEm) or send real input.
- Build: `dotnet build DS4ControllerHandler.sln -c Debug`; tests: `dotnet test tests/DS4Handler.Core.Tests`.
- Code comments in English, UI text Dutch (no UI text changes expected).

---

### Task 1: Tests must not call the real HidHide driver

**Finding:** `HidHideUninstall.Run` always does `new HidHideClient().CleanUp(...)`. Tests in
`tests/DS4Handler.Core.Tests/AppLinked/SettingsStoreTests.cs` (`NoSettingsFile_IsNotCreated`,
`DriverMissing_WithOwnedIds_IsIncomplete_AndKeepsTheList`, `AllUsers_WithoutAProfileRegistry_DoesNotThrow`) call
`Run` and assume HidHide is absent. On a PC with HidHide they fail (`Expected: NotInstalled / Actual: Done`) and
they really change the driver state (unhide, allow list, global switch with `activatedByApp: true`).

**Files:**
- Modify: `src/DS4Handler.App/Services/HidHideUninstall.cs`
- Modify: `tests/DS4Handler.Core.Tests/AppLinked/SettingsStoreTests.cs`

**Change:**
- Give `Run` one optional last parameter:
  `Func<IEnumerable<string>, string?, bool, HidHideCleanupResult>? cleanUp = null`.
  Default (null) means `(ids, exe, act) => new HidHideClient().CleanUp(ids, exe, act)`.
  Pass it through to the private `CleanUp(owned, activatedByApp, cleanUp)` which calls it instead of `new HidHideClient().CleanUp(...)`.
  All existing exception handling (Busy retry, NotInstalled, Failed) stays exactly as is.
- The app caller `src/DS4Handler.App/App.xaml.cs:148` stays unchanged (uses the default).
- Every test in the test project that calls `HidHideUninstall.Run` passes a fake that throws
  `new HidHideException(HidHideErrorKind.NotInstalled, "test")`. Put it in one private static field/method in the test class, e.g. `NotInstalled`.
- Grep the whole test project for any other call of `HidHideUninstall.Run` or `new HidHideClient(` and route them the same way (a test that needs a real driver must not exist).

**Verify:** `dotnet test tests/DS4Handler.Core.Tests` — all tests pass on this PC (HidHide IS installed here; before the fix 3 fail).

---

### Task 2: Virtual pad retries again after a successful reconnect

**Finding:** In `src/DS4Handler.App/Engine/ControllerSession.cs`, `_reconnectAttempted` (field ~L91) is set true in the
`SubmitVirtual` catch (~L809-814) and cleared only in `RequestOutputMode`. After a successful automatic reconnect it
stays true, so a second driver failure in the same session leaves the pad dead.

**Files:**
- Modify: `src/DS4Handler.App/Engine/ControllerSession.cs`

**Change:**
- In `SubmitVirtual`, right after a successful `SubmitReport()` in BOTH branches (Xbox and DS4), clear the flag
  without a write per report: `if (_reconnectAttempted) _reconnectAttempted = false;`.
  Clearing it after a working submit (not on connect) means a target that fails at once can still not loop every 2 s.
- Update the field comment to say: one automatic retry per failure, re-armed after a submit works again.
- No test: `ControllerSession` needs HidSharp + ViGEm; the change is one guarded assignment. Build must pass.

**Verify:** `dotnet build DS4ControllerHandler.sln -c Debug` succeeds; `dotnet test tests/DS4Handler.Core.Tests` still passes.

---

### Task 3: Test the Bluetooth output reports and CRC

**Finding:** `src/DS4Handler.App/Engine/OutputReports.cs` and `src/DS4Handler.App/Engine/Crc32.cs` build the
rumble / light bar / poll-rate reports. No test covers them; a wrong CRC makes a Bluetooth pad ignore every report silently.

**Files:**
- Modify: `tests/DS4Handler.Core.Tests/DS4Handler.Core.Tests.csproj` (link both files, same style as the existing `KeyboardOutput.cs` link: `Link="AppLinked\Source\<name>.cs"`)
- Create: `tests/DS4Handler.Core.Tests/AppLinked/OutputReportsTests.cs`

**Tests (exact values):**
1. `~Crc32.Compute("123456789"u8)` equals `0xCBF43926u` (standard CRC-32 check value).
2. DS4 Bluetooth: write into a 78-byte buffer with `OutputReports.Write(buf, ControllerModel.DualShock4, ConnectionType.Bluetooth, small: 0x11, large: 0x22, red: 0x00, green: 0x30, blue: 0xFF, sequence: 0, btPollIntervalMs: 4)`.
   Assert `buf[0] == 0x11`, `buf[1] == 0xC4`, `buf[3] == 0x07`, `buf[6] == 0x11`, `buf[7] == 0x22`, `buf[8..11]` == `00 30 FF`,
   and `BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(74))` equals the CRC computed independently in the test:
   `~Crc32.Compute(buf.AsSpan(0, 74), Crc32.Compute(new byte[] { 0xA2 }))`.
3. DualSense Bluetooth: same call with `ControllerModel.DualSense`, `sequence: 5`. Assert `buf[0] == 0x31`, `buf[1] == 0x50`, `buf[2] == 0x10`, `buf[5] == 0x11`, `buf[6] == 0x22`, `buf[41] == 0x04`, and the CRC at 74..77 as in test 2.
- If linking needs a stub (it should not: both files only use Core types and `System.Buffers.Binary`), add it under `AppLinked/` like the existing stubs.

**Verify:** `dotnet test tests/DS4Handler.Core.Tests` passes, new tests included.

---

### Task 4: One rule for which scan codes can be sent

**Finding:** `KeyNames.ScanCode` in `src/DS4Handler.App/ViewModels/RemapViewModel.cs` only allows codes whose low byte
is non-zero and high byte is `0x00` or `0xE0`. `ReadRemap` in `src/DS4Handler.Core/Tuning/InputTuningJsonConverter.cs`
accepts any `1..0xE0FF`. A profile with `"Key": 256` loads, shows "Toets 0x100" and sends scan code 0.

**Files:**
- Modify: `src/DS4Handler.Core/Mapping/ButtonRemap.cs` (add to `RemapTarget`)
- Modify: `src/DS4Handler.Core/Tuning/InputTuningJsonConverter.cs`
- Modify: `src/DS4Handler.App/ViewModels/RemapViewModel.cs`
- Test: `tests/DS4Handler.Core.Tests/RemapPersistenceTests.cs` (add cases)

**Change:**
- Add `public static bool IsSendableScanCode(int code) => (code & 0xFF) != 0 && (code >> 8) is 0 or 0xE0;` on `RemapTarget`
  (covers negatives/huge too: for those `code >> 8` is neither 0 nor 0xE0... verify; if a negative int passes, add `code is >= 1 and <= 0xE0FF &&`).
- `ReadRemap`: replace `scanCode is >= 1 and <= 0xE0FF` with `RemapTarget.IsSendableScanCode(scanCode)`.
- `KeyNames.ScanCode`: use `RemapTarget.IsSendableScanCode((int)scan)` instead of its own expression (keep the existing comment about Pause).
- Tests: a profile JSON with `"Key": 256`, `"Key": 57344` (0xE000) and `"Key": -1` each loads that source as `Same`; `"Key": 57` (0x39) and `"Key": 57437` (0xE05D) load as keys. Follow the style of existing tests in that file.

**Verify:** `dotnet test tests/DS4Handler.Core.Tests` passes; `dotnet build DS4ControllerHandler.sln -c Debug` succeeds.
