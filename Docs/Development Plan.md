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
      WorkerBridge.cs                     (tray side: spawn/connect to elevated worker)
      CleanupWorker.cs                    (elevated worker side: pipe loop + runs RAMMap)
      PipeProtocol.cs                     (message DTOs + JSON context + length-prefixed framing)
```

### Elevation architecture (worker + named pipe)

```
Tray process (medium integrity, asInvoker)          Worker process (high integrity, runas)
──────────────────────────────────────────         ──────────────────────────────────────
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
- [ ] Manually verify against acceptance criteria (§28) — see checklist below.
- [ ] Commit.

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

Run `publish\win-x64\WindowsMemoryReaper.exe` (UAC prompt → approve) and tick off:

- [ ] Runs without installer; tray icon appears; no main window.
- [ ] Copy the publish folder to an arbitrary directory; runs from there.
- [ ] Creates `WindowsMemoryReaper.json` beside the EXE on first run, with
      `automaticCleaningEnabled: false` (no auto-clean on first launch).
- [ ] `Settings...` opens the dialog; Browse selects `RAMMap64.exe`; status shows "found".
- [ ] Enter a bogus path → status "not found"; Clean Now shows the clear error; icon amber.
- [ ] Configure a real RAMMap64 path, Save, enable Automatic Cleaning at 5 min → tray menu
      shows "Next cleaning: 5 min"; interval measured from completion.
- [ ] Clean Now runs `-Ew -Es -Em -Et -E0` sequentially; RAMMap windows stay hidden; green
      icon; completion notification appears.
- [ ] Double-click tray icon opens Settings.
- [ ] Exit stops the timer and the process leaves the tray.

## Notes / open questions

- AOT migration tracked as a follow-up (after WPF becomes AOT-compatible).
- Start-with-Windows shortcut is an optional future feature (§11), not in v1 scope.
- v1 ships self-contained single-file (includes .NET runtime). A single-EXE + JSON
  distribution is confirmed; the ~62 MB EXE is expected without Native AOT.
