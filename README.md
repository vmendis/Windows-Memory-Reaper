# Windows Memory Reaper

A lightweight Windows 11 system-tray utility that periodically reclaims RAM by invoking [RAMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/rammap) memory-cleaning operations.

---

## Features

- **System-tray resident** — runs quietly in the notification area with colour-coded status icons
- **Automatic cleaning** — configurable timer cleans RAM at a fixed interval (default 30 minutes)
- **Manual cleaning** — one-click "Clean now" from the tray menu
- **Five RAMMap operations** — executes the full suite of RAMMap memory-release commands for thorough cleaning
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

1. Download `WindowsMemoryReaper.exe` from the [Releases](https://github.com/flight-sim-mendis/Windows-Memory-Reaper/releases) page.
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
| **Clean now** | Runs a full five-operation RAMMap cleanup immediately |
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

When automatic cleaning is enabled, the application runs the following five RAMMap operations sequentially at the configured interval (default: every 30 minutes):

| # | RAMMap switch | Operation | Description |
|---|---|---|---|
| 1 | `-Ew` | Empty Working Sets | Flushes the working set of every process, releasing process-used pages |
| 2 | `-Es` | Empty System Working Set | Flushes the system working set (kernel, drivers, file-system cache) |
| 3 | `-Em` | Empty Modified Working Set | Writes modified pages to disk and flushes them from memory |
| 4 | `-Et` | Empty Standby List | Purges the standby list (cached pages not currently in use) |
| 5 | `-E0` | Empty Priority 0 Standby List | Purges only the lowest-priority standby pages |

The timer is measured from the **completion** of one cycle, not from its start. If a cleanup is already running when the timer fires, the new cycle is skipped.

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
