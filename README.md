# Windows Memory Reaper

A lightweight Windows 11 system-tray utility that periodically reclaims RAM by invoking [RAMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) memory-cleaning operations.

---

## Features

- **System-tray resident** — runs quietly in the notification area with colour-coded status icons
- **Automatic cleaning** — configurable timer cleans RAM at a fixed interval (default 30 minutes)
- **Manual cleaning** — one-click "Clean now" from the tray menu
- **Five RAMMap operations** — executes the full suite of RAMMap memory-release commands for thorough cleaning, with each operation individually configurable in Settings
- **Elevated worker architecture** — only one UAC prompt for the entire session; the worker persists until the tray exits
- **Portable** — single self-contained EXE (~65 MB), no installation required; place it anywhere and run
- **Settings UI** — configure the RAMMap path, cleaning interval, and enable/disable automatic cleaning
- **Clean exit** — waits for an in-progress cleanup to finish (up to 10 s) before shutting down

---

## Requirements

| Requirement | Details |
|---|---|
| **Operating system** | Windows 11 |
| **RAMMap** | [RAMMap64.exe](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) from Sysinternals — required for memory-cleaning operations |
| **.NET runtime** | None — the EXE is self-contained |

---

## Installation

1. Download `WindowsMemoryReaper.exe` from the [Releases](https://github.com/vmendis/Windows-Memory-Reaper/releases) page.
2. Place the EXE in any folder you like (it is fully portable).
3. Download [RAMMap64.exe](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) from Sysinternals and note its location.
4. Double-click `WindowsMemoryReaper.exe` to start.
5. On first launch, open **Settings** from the tray menu and set the path to `RAMMap64.exe`.

> The application creates a `WindowsMemoryReaper.json` settings file next to the EXE on first run.

---

## Usage

The application lives entirely in the **system tray** (notification area). Right-click the tray icon to access the menu:

| Menu item | Action |
|---|---|
| **Clean now** | Runs the user-enabled set of RAMMap operations immediately |
| **Automatic cleaning** | Toggles automatic cleaning on/off; displays the current interval and next scheduled time |
| **Settings** | Opens the settings window to configure the RAMMap path and cleaning interval |
| **Exit** | Shuts down the application (waits for any in-progress cleanup to finish) |

The tray icon changes colour to reflect the current state:

| Colour | Meaning |
|---|---|
| Normal | Idle — ready for the next cleanup |
| Cleaning | A cleanup cycle is in progress |
| Warning | RAMMap64.exe is not configured or cannot be found |
| Error | The last cleanup failed |

---

## How Automatic Cleaning Works

When automatic cleaning is enabled, the application runs the user-enabled subset of the following five RAMMap operations sequentially at the configured interval (default: every 30 minutes). Each operation can be individually enabled or disabled in **Settings**:

| # | RAMMap switch | Operation | Description |
|---|---|---|---|
| 1 | `-Ew` | Empty Working Sets | Flushes the working set of every process, releasing process-used pages |
| 2 | `-Es` | Empty System Working Set | Flushes the system working set (kernel, drivers, file-system cache) |
| 3 | `-Em` | Empty Modified Working Set | Writes modified pages to disk and flushes them from memory |
| 4 | `-Et` | Empty Standby List | Purges the standby list (cached pages not currently in use) |
| 5 | `-E0` | Empty Priority 0 Standby List | Purges only the lowest-priority standby pages |

The timer is measured from the **completion** of one cycle, not from its start. If a cleanup is already running when the timer fires, the new cycle is skipped.

For a brief explanation of what each of these five things actually is and how Windows manages them, see [Memory-management fundamentals](#memory-management-fundamentals).

---

## Memory-Management Fundamentals

Windows is a *demand-paged, virtual-memory* operating system. Programs address a large virtual address space, and the **memory manager** brings pages into physical RAM only when they are actually touched (faulted in). The memory manager also keeps bookkeeping for every physical page of RAM (the *page-frame database*) and assigns each page to one of several state lists: free, zeroed, modified, standby, transition, or bad. The five operations this tool runs act on five of those concepts:

### Working Set

A process's **working set** is the subset of its virtual address space currently resident in physical memory — the pages the process can access without needing to read them back from disk. Windows grows a working set on demand and *trims* it when physical memory is in demand; this trimming is done periodically by the kernel's **balance-set manager**. Emptying working sets removes those resident pages: clean pages move to the standby list, while modified pages are written to disk first.

### System Working Set

Kernel-mode code and data — the kernel itself, device drivers, and the file-system cache — is also memory-managed, and its resident physical pages form the **system working set** (the working set of the system process, PID 4). Emptying the system working set trims those kernel-mode pages in the same way process working sets are trimmed.

### Modified Working Set / Modified Page List

Pages whose contents changed since they were last written to disk are *modified* (dirty). The memory manager keeps them on the **modified page list** rather than freeing or reusing them, so the data is never lost. A background system thread, the **modified page writer**, lazily writes these pages to disk over time; once written, a page moves to the standby list and can be reused. Emptying the modified page list forces that flush immediately, so dirty pages are written to disk and their memory becomes reusable.

### Standby List

Pages whose contents are no longer needed but are still intact in RAM belong to the **standby list** — the operating system's cache. This is exactly what Task Manager reports as *"available / cached"* memory. Standby pages are kept precisely because they can be reused cheaply: if a process needs one of them again, Windows reactivates it instantly, with **no disk I/O**. Emptying the standby list hands those pages back to the free list early.

### Priority 0 Standby List

Standby pages are organised into up to eight priority buckets (0–7) so the memory manager can decide *which* cached pages to recycle first. **Priority 0** is the lowest — these are the pages that Windows considers least worth keeping and reclaims first when RAM is needed for something new. Emptying the priority 0 standby list decommits exactly those pages to the free list, without touching higher-priority cache.

### Key takeaways

- **Low "free" memory is not low memory.** Windows deliberately uses available RAM for caching; most of that shows up as standby pages.
- **Standby pages are available.** Any device or process can take them back the instant it needs them — Windows does not hold them hostage.
- **Cleaning is a temporary effect.** The memory manager will reuse those pages for caching again, because that is what it is designed to do. Periodic forced cleaning is not universally beneficial — see the [Disclaimer](#disclaimer).

---

## Architecture

```
┌──────────────────────────────────────────────────┐
│                   Tray Process                    │
│              (medium integrity, WPF)              │
│                                                   │
│  AppController                                    │
│    ├── TrayIconController  (H.NotifyIcon.Wpf)    │
│    ├── CleanupScheduler    (timer management)     │
│    ├── SettingsStore       (JSON persistence)     │
│    └── WorkerBridge        (named pipe client)    │
└────────────────────┬─────────────────────────────┘
                     │ Named pipe (once-only UAC)
                     ▼
┌──────────────────────────────────────────────────┐
│              Elevated Worker Process              │
│            (high integrity, --worker)             │
│                                                   │
│  CleanupWorker                                    │
│    └── RamMapService  (launches RAMMap64.exe)     │
└──────────────────────────────────────────────────┘
```

The **tray process** runs at medium integrity so its icon is always visible in the non-elevated shell. Memory-cleaning operations require administrator privileges, so they are delegated to a separate **elevated worker process** that is launched on demand via the Windows `runas` verb. The worker persists for the lifetime of the tray process, avoiding repeated UAC prompts.

---

## Building from Source

**Clone the repository:**

```bash
git clone https://github.com/vmendis/Windows-Memory-Reaper.git
cd Windows-Memory-Reaper
```

**Prerequisites:**

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (or later)
- Windows 11

**Build:**

```bash
dotnet build src/WindowsMemoryReaper/WindowsMemoryReaper.csproj -c Release
```

**Publish (self-contained single-file EXE):**

```bash
dotnet publish src/WindowsMemoryReaper/WindowsMemoryReaper.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o publish/win-x64
```

The output is a single `WindowsMemoryReaper.exe` (~65 MB) in `publish/win-x64/`.

---

## Project Structure

```
Windows-Memory-Reaper/
├── src/
│   └── WindowsMemoryReaper/
│       ├── App.xaml / App.xaml.cs          # Application entry point
│       ├── AssemblyInfo.cs                 # Assembly metadata
│       ├── app.manifest                    # UAC and compatibility manifest
│       ├── WindowsMemoryReaper.csproj      # Project file (.NET 10, WPF)
│       └── Services/
│           ├── AppController.cs            # Main coordinator
│           ├── AppSettings.cs              # Settings model
│           ├── SettingsStore.cs            # JSON persistence
│           ├── SettingsWindow.xaml/.cs     # Settings UI
│           ├── TrayIconController.cs       # Tray icon management
│           ├── TrayIconFactory.cs          # Icon rendering
│           ├── CleanupScheduler.cs         # Timer logic
│           ├── CleanupResult.cs            # Result types
│           ├── CleanupWorker.cs            # Elevated worker entry point
│           ├── WorkerBridge.cs             # Named-pipe client (tray side)
│           ├── PipeProtocol.cs             # Pipe serialization
│           └── RamMapService.cs            # RAMMap process launcher
├── publish/                                # Build output (git-ignored)
├── Docs/                                   # Design documents
├── .gitignore
├── LICENSE
└── README.md
```

---

## Contributing

Contributions are welcome! Please open an issue or pull request on GitHub.

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/my-change`)
3. Commit your changes
4. Push to the branch and open a Pull Request

---

## License

This project is licensed under the [MIT License](LICENSE).

---

## Disclaimer

This application uses [RAMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) from Microsoft Sysinternals. RAMMap is a Microsoft tool and is not affiliated with or endorsed by this project. Use at your own risk. The authors are not responsible for any damage or data loss resulting from the use of this software.
