# Windows Memory Reaper — Development Plan

Working, trackable step-by-step plan for building the application described in
[Tiny Windows Memory Reaper — Software Specification](Tiny%20Windows%20Memory%20Reaper%20—%20Software%20Specification.md).

## Status legend

- [ ] Pending
- [x] Done

---

## Stack decisions (confirmed with owner)

| Area | Decision |
| --- | --- |
| Language / framework | C# / .NET 10 (SDK 10.0.102 installed) |
| Platform target | `net10.0-windows`, Win32, x64, WPF |
| Tray implementation | `H.NotifyIcon.Wpf` (NativeAOT-friendly, dynamic icon generation) |
| Settings window | WPF window |
| Tray icon | Generated programmatically (no external .ico asset) |
| Config | `WindowsMemoryReaper.json` beside the EXE, via `System.Text.Json` source generator |
| AOT | Deferred. WPF does **not** support Native AOT yet. Distribution is self-contained trimmed for win-x64. Migration path kept open (source-gen JSON, no reflection) |
| Elevation | **Tray app runs asInvoker (medium integrity)** so the tray icon is visible. A once-only UAC (via `runas`) spawns a **persistent elevated worker** process that runs the RAMMap operations; the two processes communicate over a **named pipe**. This resolves the UIPI issue where an elevated tray app's icon is invisible in the non-elevated shell, while keeping the spec's "no repeated per-operation UAC prompts" and "no service / no scheduled task / no installer" constraints. |
| UAC behavior (per-machine) | On systems where `ConsentPromptBehaviorAdmin = 0` (silently elevate admins, the "Never notify" UAC setting), the worker elevation completes without a visible prompt. This is environment-specific; on default Windows 11 machines the UAC consent dialog appears as expected. |

## Project structure

```
Windows RAM Reaper/
  Docs/
    Tiny Windows Memory Reaper — Software Specification.md
    Development Plan.md                  (this file)
  src/WindowsMemoryReaper/
    WindowsMemoryReaper.csproj
    App.xaml / App.xaml.cs                (entry; branches tray vs --worker mode)
    SettingsWindow.xaml / .cs            (settings dialog)
    app.manifest                          (asInvoker + DPI awareness)
    Services/
      AppSettings.cs                      (model)
      SettingsStore.cs                    (load/save JSON beside EXE)
      RamMapService.cs                    (sequential 5-op cleanup engine)
      CleanupScheduler.cs                 (timer from completion)
      TrayIconController.cs               (tray icon, context menu, notifications)
      TrayIconFactory.cs                  (programmatic 32px icon generator)
      WorkerBridge.cs                     (tray side: spawn/connect to elevated worker)
      CleanupWorker.cs                    (elevated worker side: pipe loop + runs RAMMap)
      PipeProtocol.cs                     (message DTOs + JSON context + length-prefixed framing)
```

### Elevation architecture (worker + named pipe)

```
Tray process (medium integrity, asInvoker)          Worker process (high integrity, runas)
──────────────────────────────────────────          ──────────────────────────────────────
  CleanupScheduler / Clean Now                          loop until tray closes or PID dies
       │                                                     │
       │ WorkerBridge.RunCleanupAsync                        │
       │   │ if no worker:                                   │
       │   │   spawn self --worker --pipe=<name> ──UAC──►  ConnectAsync
       │   │   WaitForConnectionAsync ◄────── Connected ────
       │   │
       │   ──── Write CleanRequest (len+JSON) ──────────►   read request
       │   │                                               RamMapService (-Ew -Es -Em -Et -E0)
       │   ◄── CleanReply (len+JSON) ────────────────      write reply
       │   │
       │   map reply → CleanupResult → notification
```

- Tray icon always visible (tray never elevated).
- One UAC prompt at first worker spawn (app start, or first clean if declined then approved later).
- Worker exits when the tray closes its pipe or when the tray PID dies.
- Pipe messages are length-prefixed UTF-8 JSON via source-generated contexts.

---

## Steps

### Step 1 — Project scaffolding
- [x] Create `WindowsMemoryReaper.csproj` (net10.0-windows, WPF, `UseWPF`, win-x64 publish profile, self-contained trimmed).
- [x] Add `H.NotifyIcon.Wpf` NuGet package.
- [x] Add `app.manifest` with `requireAdministrator` (`requestedExecutionLevel`).
- [x] Add stub `Program.cs` + hidden `MainWindow` host.
- [x] Verify `dotnet build` succeeds.
- [x] Commit.

### Step 2 — Config model + settings store
- [x] Define `AppSettings` (per spec §9): `ramMapPath`, `automaticCleaningEnabled`, `cleaningIntervalMinutes`.
- [x] `SettingsStore`: path = `AppContext.BaseDirectory\WindowsMemoryReaper.json`; load with defaults if missing; save; surface clear error if directory not writable (§9).
- [x] Source-generated `System.Text.Json` context (AOT-friendly).
- [x] Commit.

### Step 3 — RamMapService (core engine)
- [x] `RunCleanupAsync()`: sequentially launch `RAMMap64.exe` with `-Ew`, `-Es`, `-Em`, `-Et`, `-E0`, waiting for each exit before next (§6).
- [x] Launch via `Process.Start` with `CreateNoWindow=true`, `UseShellExecute=false`, args passed individually — never shell strings (§17).
- [x] No-overlap guard (§15/§16).
- [x] Per-operation timeout (default ~60 s) → abort sequence (§15).
- [x] Return outcome (completed / failed / timed-out).
- [x] Commit.

### Step 4 — CleanupScheduler
- [x] Timer fires → run cleanup → **on completion** start interval countdown (§16).
- [x] Disabled when automatic cleaning is off or RAMMap path invalid.
- [x] Never auto-cleans on first launch (§26).
- [x] Commit.

### Step 5 — Tray + AppController
- [x] `TrayIconController`: hidden host + H.NotifyIcon; context menu per §7 (Clean Now, Automatic Cleaning toggle, Next cleaning, Settings…, Exit).
- [x] Double-click tray → open Settings (§8).
- [x] Icon/tooltip states: Normal, Warning (RAMMap missing), Cleaning, Error (§19).
- [x] Commit.

### Step 6 — Settings window (WPF)
- [x] Layout per §18: RAMMap path + Browse…, Enable automatic cleaning checkbox, interval ComboBox (5/10/15/20/30/45/60/Custom), Clean Now, status line, Save/Cancel.
- [x] Validation: path exists + file exists → status; save then close.
- [x] Commit.

### Step 7 — Clean Now + notifications
- [x] "Clean Now" from tray + settings triggers `RunCleanupAsync()` directly.
- [x] Manual completion notification ("Memory cleanup completed") (§14). Automatic notifications off by default.
- [x] Commit.

### Step 8 — Failure handling
- [x] RAMMap missing → Warning state, auto-clean disabled, clear error on clean attempt (§15).
- [x] Operation failure/timeout → stop sequence, report "Memory cleanup could not be completed.", Error state, no repeated hammering (§15).
- [x] Commit.

### Step 9 — Exit lifecycle
- [x] On Exit: stop timer, wait (bounded) for active cleanup, save config, dispose tray, exit (§23).
- [x] Commit.

### Step 10 — Publish + verification against acceptance criteria
- [x] `dotnet publish -c Release -r win-x64 --self-contained` → portable folder
      (`publish\win-x64\WindowsMemoryReaper.exe`, single-file, self-contained).
- [x] Manually verified against acceptance criteria (§28) — see checklist below.
- [x] Commit.

---

## Scope guardrails (do NOT implement in v1)

SimConnect, MSFS detection, process-specific cleaning, memory-threshold triggers, GPU/VRAM
monitoring, RAM graphs, logging, telemetry, cloud services, automatic RAMMap downloads,
Windows services, scheduled tasks, installer, automatic updating, multiple profiles,
optimisation algorithms, RAM measurement/display. Per spec §24.

## Distribution model

```
WindowsMemoryReaper.exe
WindowsMemoryReaper.json   (auto-created on first run)
```
No installer, no registry, no service, no scheduled task, no MSFS dependency, no internet
requirement. RAMMap64.exe is NOT redistributed (spec §3).

## Manual verification checklist (acceptance criteria §28)

- [x] Runs without installer; tray icon appears; no main window.
- [x] Copy the publish folder to an arbitrary directory; runs from there.
- [x] Creates `WindowsMemoryReaper.json` beside the EXE on first run, with
      `automaticCleaningEnabled: false` (no auto-clean on first launch).
- [x] `Settings...` opens the dialog; Browse selects `RAMMap64.exe`; status shows "found".
- [x] Enter a bogus path → status "not found"; Clean Now shows the clear error; icon amber/red.
- [x] Configure a real RAMMap64 path, Save, enable Automatic Cleaning at 5 min → tray menu
      shows "Automatic Cleaning: on (every 5 min)" and "Next cleaning: 5 min"; interval
      measured from completion. Scheduled clean runs the elevated worker after the interval.
- [x] Clean Now runs `-Ew -Es -Em -Et -E0` sequentially; RAMMap windows stay hidden; green
      icon; completion notification appears.
- [x] Double-click tray icon opens Settings.
- [x] Exit stops the timer, terminates the tray and the worker, and the process leaves
      the tray.

---

## Known issues / platform quirks discovered during testing

1. **`TaskbarIcon.ForceCreate()` required for code-only windowless usage.**  
   H.NotifyIcon.Wpf is lazy — the `TaskbarIcon` does not call `Shell_NotifyIcon(NIM_ADD)`
   until either the control is placed into a WPF visual tree or `ForceCreate()` is called
   explicitly. Without it the tray icon never appears. (Tested: 3 consecutive launches
   with no icon; ForceCreate() fixed it immediately.)

2. **`IsChecked = true` on a never-opened WPF checkable MenuItem crashes.**  
   Setting `_automaticMenuItem.IsChecked = true` (where `IsCheckable = true`) in code
   *before* the `ContextMenu` has ever been opened causes a recursive call chain that
   reaches a `0xc00000fd` (stack overflow) inside `USER32.dll` / `SHELL32.dll` within
   seconds of startup. This was isolated to:
   - `auto=true` (with any RAMMap path) → crash every time.
   - `auto=false` → stable indefinitely.
   - Disabling only the `IsChecked` assignment (keeping header text change) → stable.
   - Enabling only the `IsChecked` assignment → crash.
   This is a bug in the interaction between WPF's `MenuItem` glyph rendering and the
   non-visual-tray popup lifecycle on Windows 11.

3. **Fix adopted: text-toggle + deferred menu state.**  
   The checkable item was replaced with a plain (non-checkable) menu item whose `Header`
   reflects the state ("Automatic Cleaning: on/off"). State writes (`IsCheckable` is
   gone entirely) are deferred to `ContextMenu.Opened` — the standard tray pattern.
   This completely eliminates the crash and the visual toggle flakiness that a checkable
   item exhibited inside H.NotifyIcon's custom popup menu. The trade-off is a text header
   instead of a graphical checkmark, which is arguably clearer on a tray menu.

4. **Custom UAC prompt behavior on developer's machine.**  
   `ConsentPromptBehaviorAdmin = 0` → elevation is silent for admins. The worker
   elevates without any visible UAC prompt on this machine. On a standard Windows 11
   configuration (`ConsentPromptBehaviorAdmin = 5`) the consent dialog appears as
   expected. This is not a bug in the application.

---

## Notes / open questions

- AOT migration tracked as a follow-up (after WPF becomes AOT-compatible).
- Start-with-Windows shortcut is an optional future feature (§11), not in v1 scope.
- v1 ships self-contained single-file (includes .NET runtime). A single-EXE + JSON
  distribution is confirmed; the ~62 MB EXE is expected without Native AOT.
