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
| Elevation | `app.manifest` with `requireAdministrator` |

## Project structure

```
Windows RAM Reaper/
  Docs/
    Tiny Windows Memory Reaper — Software Specification.md
    Development Plan.md                  (this file)
  src/WindowsMemoryReaper/
    WindowsMemoryReaper.csproj
    Program.cs
    App.xaml / App.xaml.cs
    MainWindow.xaml / .cs                (hidden host window: single-instance + tray lifetime)
    SettingsWindow.xaml / .cs            (settings dialog)
    app.manifest                          (requireAdministrator)
    Services/
      AppSettings.cs                      (model)
      SettingsStore.cs                    (load/save JSON beside EXE)
      RamMapService.cs                    (sequential 5-op cleanup engine)
      CleanupScheduler.cs                 (timer from completion)
      TrayIconController.cs               (tray icon, context menu, notifications)
```

---

## Steps

### Step 1 — Project scaffolding
- [ ] Create `WindowsMemoryReaper.csproj` (net10.0-windows, WPF, `UseWPF`, win-x64 publish profile, self-contained trimmed).
- [ ] Add `H.NotifyIcon.Wpf` NuGet package.
- [ ] Add `app.manifest` with `requireAdministrator` (`requestedExecutionLevel`).
- [ ] Add stub `Program.cs` + hidden `MainWindow` host.
- [ ] Verify `dotnet build` succeeds.
- [ ] Commit.

### Step 2 — Config model + settings store
- [ ] Define `AppSettings` (per spec §9): `ramMapPath`, `automaticCleaningEnabled`, `cleaningIntervalMinutes`.
- [ ] `SettingsStore`: path = `AppContext.BaseDirectory\WindowsMemoryReaper.json`; load with defaults if missing; save; surface clear error if directory not writable (§9).
- [ ] Source-generated `System.Text.Json` context (AOT-friendly).
- [ ] Commit.

### Step 3 — RamMapService (core engine)
- [ ] `RunCleanupAsync()`: sequentially launch `RAMMap64.exe` with `-Ew`, `-Es`, `-Em`, `-Et`, `-E0`, waiting for each exit before next (§6).
- [ ] Launch via `Process.Start` with `CreateNoWindow=true`, `UseShellExecute=false`, args passed individually — never shell strings (§17).
- [ ] No-overlap guard (§15/§16).
- [ ] Per-operation timeout (default ~60 s) → abort sequence (§15).
- [ ] Return outcome (completed / failed / timed-out).
- [ ] Commit.

### Step 4 — CleanupScheduler
- [ ] Timer fires → run cleanup → **on completion** start interval countdown (§16).
- [ ] Disabled when automatic cleaning is off or RAMMap path invalid.
- [ ] Never auto-cleans on first launch (§26).
- [ ] Commit.

### Step 5 — Tray + AppController
- [ ] `TrayIconController`: hidden host + H.NotifyIcon; context menu per §7 (Clean Now, Automatic Cleaning toggle, Next cleaning, Settings…, Exit).
- [ ] Double-click tray → open Settings (§8).
- [ ] Icon/tooltip states: Normal, Warning (RAMMap missing), Cleaning, Error (§19).
- [ ] Commit.

### Step 6 — Settings window (WPF)
- [ ] Layout per §18: RAMMap path + Browse…, Enable automatic cleaning checkbox, interval ComboBox (5/10/15/20/30/45/60/Custom), Clean Now, status line, Save/Cancel.
- [ ] Validation: path exists + file exists → status; save then close.
- [ ] Commit.

### Step 7 — Clean Now + notifications
- [ ] "Clean Now" from tray + settings triggers `RunCleanupAsync()` directly.
- [ ] Manual completion notification ("Memory cleanup completed") (§14). Automatic notifications off by default.
- [ ] Commit.

### Step 8 — Failure handling
- [ ] RAMMap missing → Warning state, auto-clean disabled, clear error on clean attempt (§15).
- [ ] Operation failure/timeout → stop sequence, report "Memory cleanup could not be completed.", Error state, no repeated hammering (§15).
- [ ] Commit.

### Step 9 — Exit lifecycle
- [ ] On Exit: stop timer, wait (bounded) for active cleanup, save config, dispose tray, exit (§23).
- [ ] Commit.

### Step 10 — Publish + verification against acceptance criteria
- [ ] `dotnet publish -c Release -r win-x64 --self-contained` → portable folder.
- [ ] Manually verify against acceptance criteria (§28).
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

## Notes / open questions

- AOT migration tracked as a follow-up (after WPF becomes AOT-compatible).
- Start-with-Windows shortcut is an optional future feature (§11), not in v1 scope.
