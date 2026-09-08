# Tiny Windows Memory Reaper

**Working name:** Windows Memory Reaper  
**Platform:** Windows 11 x64  
**Purpose:** Provide a tiny portable system-tray utility that periodically invokes selected Microsoft Sysinternals RAMMap memory-cleaning operations, without requiring MSFS to be running.

## 1. Overview

Windows Memory Reaper is a lightweight Windows 11 tray application intended to provide convenient, repeatable access to selected RAMMap memory-cleaning functions.

The application is based on the memory-cleaning sequence currently being investigated in the Microsoft Flight Simulator community:

- Empty Working Sets
- Empty System Working Set
- Empty Modified Page List
- Empty Standby List
- Empty Priority 0 Standby List

The utility is **not an independent memory-management implementation**. It uses the user's existing `RAMMap64.exe` installation to perform the operations.

The application must remain useful for ordinary Windows use and must not depend on Microsoft Flight Simulator, SimConnect, or any particular game/application.

## 2. Design objectives

The application shall be:

- Extremely small and lightweight.
- Fully portable.
- No installer.
- No Windows service.
- No registry installation.
- No scheduled task installation.
- No dependency on MSFS.
- No requirement for an internet connection.
- No logging.
- No telemetry.
- No background data collection.
- Configured entirely from the tray interface.
- Able to run directly from any user-selected directory.

The executable should simply be copied to a directory and run.

## 3. External dependency

The application requires:

`RAMMap64.exe`

The application shall **not redistribute or embed RAMMap64.exe**, because Microsoft does not grant redistribution rights for Sysinternals utilities.

The user is responsible for obtaining RAMMap from Microsoft's official Sysinternals distribution.

Current Microsoft documentation lists RAMMap 1.63 and confirms that it runs on Windows Vista and later.

## 4. User-configurable settings

The initial version deliberately has only two required settings.

### 4.1 RAMMap64 location

Setting:

`RAMMap64.exe Location`

The user selects the location of:

`RAMMap64.exe`

The program shall:

1. Store the complete path.
2. Verify that the file exists.
3. Verify that the selected executable can be launched.
4. Display a clear error if it becomes unavailable.

Example:

`C:\Tools\Sysinternals\RAMMap64.exe`

The program should provide a **Browse...** button rather than requiring the user to type the path.

### 4.2 Automatic cleaning interval

Setting:

`Automatic Cleaning`

with:

`Enabled / Disabled`

When enabled, the user selects the cleaning interval.

Suggested initial choices:

- 5 minutes
- 10 minutes
- 15 minutes
- 20 minutes
- 30 minutes
- 45 minutes
- 60 minutes
- Custom

The interval should be measured from completion of one cleaning operation rather than from the start of the previous operation.

The default state should be:

**Automatic cleaning disabled**

This is preferable because memory cleaning is a system-wide operation and the MSFS investigation has identified at least one potentially sensitive operation immediately after cleaning: SimBrief Import Route in the MSFS 2024 EFB.

## 5. Cleaning operation

Each automatic or manual cleaning cycle shall execute the following RAMMap operations:

### Operation 1

`RAMMap64.exe -Ew`

**Empty Working Sets**

### Operation 2

`RAMMap64.exe -Es`

**Empty System Working Set**

### Operation 3

`RAMMap64.exe -Em`

**Empty Modified Page List**

### Operation 4

`RAMMap64.exe -Et`

**Empty Standby List**

### Operation 5

`RAMMap64.exe -E0`

**Empty Priority 0 Standby List**

The first four operations correspond to the operations discussed in the forum thread, with Empty System Working Set added at the user's request. The forum's currently reported experimental sequence uses `-Ew`, `-Em`, `-Et`, and `-E0`.

`-Es` corresponds to **Empty System Working Set** and is an established RAMMap operation.

## 6. Execution model

The utility should execute the five operations **sequentially**.

Conceptually:

```text
RAMMap64.exe -Ew
wait for completion

RAMMap64.exe -Es
wait for completion

RAMMap64.exe -Em
wait for completion

RAMMap64.exe -Et
wait for completion

RAMMap64.exe -E0
wait for completion
```

This avoids depending on RAMMap accepting multiple independent cleaning commands in a single invocation.

A Microsoft Q&A discussion also demonstrates executing the RAMMap commands individually rather than treating them as a single multi-operation invocation.

The implementation should wait for each RAMMap process to terminate before starting the next operation.

RAMMap's console windows should preferably remain invisible to the user.

## 7. Manual cleaning

Although automatic cleaning is a core feature, the tray interface should include:

**Clean Now**

This performs exactly the same five-operation sequence immediately.

This makes the program useful even when automatic cleaning is disabled.

The tray menu could therefore be:

```text
Windows Memory Reaper

Clean Now
Automatic Cleaning   ✓
Next cleaning: 12 min

Settings...

Exit
```

When automatic cleaning is disabled:

```text
Windows Memory Reaper

Clean Now
Automatic Cleaning

Settings...

Exit
```

## 8. System tray behaviour

The application shall run primarily as a notification-area application.

There should be no conventional main window after startup.

Launching the executable should:

1. Start the tray application.
2. Load saved settings.
3. Validate the RAMMap64 path.
4. Start the automatic-cleaning timer if enabled.
5. Remain resident in the tray.

Double-clicking the tray icon should open the settings window.

Right-clicking the tray icon should open the context menu.

## 9. Settings storage

The application must remain portable.

Settings should therefore be stored beside the executable, for example:

```text
WindowsMemoryReaper.exe
WindowsMemoryReaper.json
```

Example:

```json
{
  "ramMapPath": "C:\\Tools\\Sysinternals\\RAMMap64.exe",
  "automaticCleaningEnabled": false,
  "cleaningIntervalMinutes": 30
}
```

No settings should be placed in:

```text
%APPDATA%
%LOCALAPPDATA%
Windows Registry
ProgramData
```

The program should not require administrator access merely to read or write its configuration.

If the executable directory is read-only, the application should show a clear settings-save error rather than silently storing configuration elsewhere, because doing so would compromise the application's portable behaviour.

## 10. Administrator privileges

The memory-cleaning operations may require elevated privileges.

The application should detect failure to execute RAMMap correctly and provide a clear explanation.

Recommended initial design:

**Run the tray application elevated.**

The executable should request administrator privileges through its Windows application manifest.

This gives the cleaning operation a predictable security context and avoids repeatedly launching UAC prompts for individual cleanup operations.

The application should not attempt to bypass or suppress Windows security mechanisms.

## 11. Startup

Optional but recommended:

`Start with Windows`

This should be implemented as a **user-level Startup shortcut**, rather than installing a Windows service or scheduled task.

However, this should initially be considered an optional feature rather than a core requirement.

The portable philosophy should remain:

```text
Copy EXE → Run EXE
```

No installation should ever be required.

## 12. No MSFS detection

The application shall **not** detect whether Microsoft Flight Simulator is running.

It shall not:

- search for FlightSimulator.exe;
- use SimConnect;
- inspect MSFS processes;
- change behaviour based on the active application;
- contain MSFS-specific logic.

The timer simply performs the configured system-wide operation.

This makes the application suitable for:

- MSFS 2020/2024
- other games
- video editing
- 3D applications
- long-running desktop sessions
- general Windows use

## 13. No logging

The application shall not create log files.

No:

```text
*.log
*.txt
event database
telemetry
usage history
```

shall be generated by normal operation.

Errors may be reported interactively through a notification or small error dialog.

## 14. Cleaning notifications

A very small amount of user feedback is desirable without introducing logging.

For example, after a manual cleaning:

```text
Memory Reaper

Memory cleanup completed.
```

For automatic cleaning, a tray notification should preferably be optional or disabled by default.

The utility should not continually generate Windows notifications every time the timer runs.

## 15. Failure handling

The program must handle these cases gracefully:

### RAMMap64.exe not found

Display:

```text
RAMMap64.exe could not be found.

Please check the RAMMap location in Settings.
```

Automatic cleaning should be disabled until the problem is corrected.

### RAMMap operation fails

The application should stop the current sequence and report:

```text
Memory cleanup could not be completed.
```

It should not repeatedly hammer RAMMap because of a persistent failure.

### RAMMap takes unusually long

The application should have a reasonable timeout for each individual operation.

A future implementation may expose this timeout as an advanced setting, but it should not be necessary for version 1.

### RAMMap process unexpectedly remains running

The utility should avoid launching another RAMMap instance for the same operation.

A cleanup cycle should never overlap another cleanup cycle.

## 16. Timer behaviour

Only one cleaning operation may execute at a time.

For example:

```text
Timer fires
    ↓
Start cleanup
    ↓
-Ew
    ↓
-Es
    ↓
-Em
    ↓
-Et
    ↓
-E0
    ↓
Cleanup complete
    ↓
Start interval countdown
```

If the system is shutting down, restarting, or the application is exiting, an active cleanup should not cause the application to remain open indefinitely.

## 17. Security

The application executes an external executable supplied by the user.

Therefore:

- The configured path must be treated as an executable path, not shell text.
- Do not invoke `cmd.exe /c`.
- Do not construct shell command strings.
- Do not allow shell metacharacters to affect execution.
- Use a direct process-launch API.
- Pass each RAMMap argument individually.
- Do not download RAMMap automatically.

This avoids unnecessary command-injection and path-parsing problems.

## 18. User interface

The UI should be deliberately small.

### Settings window

Suggested layout:

```text
┌─────────────────────────────────────────────┐
│ Windows Memory Reaper                    X  │
├─────────────────────────────────────────────┤
│                                             │
│ RAMMap64.exe                                │
│ [ C:\Tools\Sysinternals\RAMMap64.exe ] [...]│
│                                             │
│ Automatic cleaning                           │
│ [✓] Enable automatic cleaning               │
│                                             │
│ Cleaning interval                            │
│ [ 30 minutes                         ▼ ]    │
│                                             │
│                                             │
│ Status: RAMMap64.exe found                  │
│                                             │
│                 [Save] [Cancel]             │
└─────────────────────────────────────────────┘
```

A **Clean Now** button could also be placed here.

## 19. Application status

The tray icon could communicate basic state:

- Normal = configured and waiting.
- Warning = RAMMap unavailable.
- Cleaning = cleanup currently executing.
- Error = previous cleanup failed.

No numeric RAM monitoring is required.

The program does not need to constantly poll memory usage.

## 20. RAM measurement

Version 1 should **not** measure or display RAM usage.

Although the motivating forum research includes measurements of physical memory, working sets, standby memory and shared GPU memory, collecting those values would add complexity without being necessary for the core utility. The forum itself recommends before/after measurements when performing controlled experiments.

A future diagnostic version could optionally display:

```text
Before cleanup
After cleanup
Memory reclaimed
```

but this should not be part of the initial minimal design.

## 21. Technology recommendation

Recommended implementation:

**C# / .NET 10 / Windows desktop / native Win32 APIs**

.NET 10 is currently an active Long Term Support release through November 14, 2028.

The application should use a lightweight native Windows tray implementation rather than Electron, WPF-heavy UI infrastructure, or a browser-based UI.

For the final release, consider .NET Native AOT.

Microsoft documents Native AOT as producing self-contained native applications that do not require the .NET runtime and generally provide faster startup and a smaller memory footprint. Windows x64 is supported.

The ideal distribution would consequently be approximately:

```text
WindowsMemoryReaper.exe
WindowsMemoryReaper.json
```

with the JSON file created automatically on first run.

No .NET installation should be required.

## 22. Target architecture

Primary target:

`Windows 11 x64`

Future consideration:

`Windows ARM64`

There is no need to support 32-bit Windows.

## 23. Application lifecycle

Normal lifecycle:

```text
Windows starts
       ↓
Memory Reaper starts
       ↓
Loads configuration
       ↓
Checks RAMMap64.exe
       ↓
Creates tray icon
       ↓
Waits
       ↓
Timer expires
       ↓
Perform cleanup
       ↓
Wait
       ↓
Repeat
```

On exit:

```text
Stop timer
Wait for active cleanup to finish
Dispose tray icon
Save configuration if required
Exit
```

## 24. Scope deliberately excluded from version 1

The following should **not** be implemented initially:

- SimConnect
- MSFS detection
- Process-specific cleaning
- Memory-threshold triggers
- GPU monitoring
- VRAM monitoring
- RAM graphs
- Logging
- Telemetry
- Cloud services
- Automatic RAMMap downloads
- Windows services
- Scheduled Tasks
- Installer
- Automatic updating
- Multiple profiles
- Complex optimisation algorithms

The objective is to make the first version extremely simple and predictable.

## 25. Safety philosophy

This utility performs system-wide memory-management operations.

These operations should therefore not be described as:

> "Freeing useless RAM"

or:

> "Optimising Windows memory"

They should instead be described accurately as:

> "Requesting Windows to release/trim selected memory lists and working sets using Microsoft RAMMap."

Microsoft describes a process working set as pages resident in physical RAM, and documents that an application's working set can be emptied.

Cleaning can consequently cause data to be brought back into memory later, potentially producing additional paging or I/O. The MSFS forum's own author explicitly describes this as an ongoing investigation and warns that different systems may respond differently.

## 26. Initial defaults

Recommended first-run defaults:

```text
RAMMap64.exe:
Not configured

Automatic cleaning:
OFF

Cleaning interval:
30 minutes
```

The application should never begin automatic system-wide memory cleaning immediately on first launch.

## 27. Future enhancements

Potential future features should be considered only after version 1 is proven reliable.

### A. Configurable individual operations

Allow the user to enable/disable:

```text
[x] Empty Working Sets
[x] Empty System Working Set
[x] Empty Modified Page List
[x] Empty Standby List
[x] Empty Priority 0 Standby List
```

This would be particularly valuable because the forum's testing is explicitly attempting to determine which individual operations provide the benefit and which combinations cause side effects.

### B. Memory-pressure trigger

Instead of simply:

> Clean every 30 minutes

allow:

> Clean when available RAM falls below X GB.

This could make the utility less intrusive during ordinary Windows use.

### C. Minimum-spacing protection

A future memory-pressure trigger should have a minimum time between cleanups so a pathological condition cannot cause continual cleaning.

### D. "Pause automatic cleaning"

A convenient tray command:

```text
Pause for 1 hour
Pause until tomorrow
Resume
```

This would be especially useful during sensitive applications without requiring the user to open Settings.

### E. Quiet mode

No notifications unless an error occurs.

### F. Before/after memory display

A manual-clean operation could optionally report:

```text
RAM before: 23.8 GB
RAM after: 17.1 GB
Difference: 6.7 GB
```

This would turn the utility into a useful personal experiment tool while retaining the no-log philosophy.

### G. Cleanup presets

For example:

```text
Full cleanup
Working Sets only
Standby cleanup
Custom
```

This would allow controlled testing of the hypothesis rather than permanently assuming that the five operations are optimal.

## 28. Acceptance criteria

Version 1 is considered complete when:

1. The program runs on a clean Windows 11 x64 system without an installer.
2. It can be copied to an arbitrary directory.
3. It does not require a .NET runtime when distributed as Native AOT.
4. It creates no registry installation.
5. It creates no Windows service.
6. It creates no scheduled task.
7. It does not require MSFS.
8. It stores configuration beside the executable.
9. The user can select RAMMap64.exe.
10. The program correctly detects an invalid RAMMap path.
11. Automatic cleaning can be enabled/disabled.
12. The interval can be configured.
13. `-Ew`, `-Es`, `-Em`, `-Et`, and `-E0` are executed sequentially.
14. RAMMap output windows are not unnecessarily shown.
15. Cleanup operations cannot overlap.
16. The user can perform **Clean Now**.
17. The application remains resident in the Windows notification area.
18. Exiting the tray application stops its timer.
19. No log files are created.
20. RAMMap64.exe is not redistributed with the application.
21. Failure of RAMMap does not crash the tray application.

## 29. Important disclaimer

This application is a convenience wrapper around RAMMap's memory-management operations.

It must not claim that periodic memory cleaning is universally beneficial.

The current MSFS investigation reports substantial improvements under the author's testing conditions, but also explicitly describes the work as experimental. In particular, the author has observed a potential MSFS 2024/SimBrief Import Route interaction following cleaning.

For general Windows use, the application should similarly make no assumption that high reported RAM usage represents a problem. Windows intentionally uses available memory for caching and other purposes.