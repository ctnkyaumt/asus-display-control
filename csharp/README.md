# ASUS Display Control — C# (WinForms) edition

A native .NET 8 / WinForms app for controlling ASUS monitors via the bundled `dwc.exe`
CLI. Low memory, fast, flicker-free.

## Features

- Four pages, picked from the sidebar: **Splendid** (picture), **System Setup**,
  **GamePlus & OSD**, and **Per-App Tweak**.
- **Splendid presets** with per-preset memory — tweak a preset's brightness/contrast/
  gains and it's restored next time you return to it, with minimal switch flash.
- A **User** preset that the hardware does not have: it applies a base Splendid mode and
  then the values you tuned into it (`BaseSplendid` in `dwc_presets.json`).
- **Per-App Tweak** — a foreground-window watcher (1 s tick) maps a process name to a
  preset and restores the previous preset when no rule matches. Rules live in
  `dwc_apptweaks.json`.
- **Dark / light theme**, toggled in the sidebar. Controls capture palette colours when
  they are built, so switching rebuilds the control tree and pushes the cached values back.
- Live **Brightness, Contrast, Trace Free, Sharpness, Saturation, Hue, RGB gains,
  RGB offsets**, **Shadow Boost**, **Blue Light Filter**, **ASCR**, and capability-aware
  **Color Temp** (only shows codes the monitor supports).
- **System Setup**: input source, auto input detect, OSD language/transparency/timeout,
  power saving, power indicator, key locks, volume/mute, monitor information (model,
  serial, firmware, usage hours), and the three CLI resets (mode / color / all).
- **GamePlus & OSD**: FPS counter, timer, crosshair, display alignment — plus an **OSD
  remote** (EZOSD d-pad) that drives the monitor's own menu, which is the only way to
  reach settings the CLI has no property for (Rest Reminder, Color Augmentation, Aspect
  Control, Motion Sync, Adaptive-Sync, QuickFit).
- Anything the monitor does not answer shows as *Unsupported* and is greyed out; system
  properties are probed lazily (once per monitor) because rejected DDC reads cost ~1.3 s each.
- **Compare** (press-and-hold), **Reset**, **Import/Export** profiles.
- **System tray** icon, close-to-tray, and **Start with Windows**.
- **Scheduled preset switching** (`Schedule…`):
  - *Fixed times* — e.g. 09:00 → Standard, 19:00 → Darkroom (wraps past midnight).
  - *By daylight* — enter your latitude/longitude; switches between a day preset and a
    night preset at sunrise/sunset (computed with the NOAA solar algorithm, in UTC).
  - Switches only when the scheduled target changes, so it never fights a manual choice.
  - Runs while the app is open — pair with *Start with Windows* + tray for set-and-forget.

Settings, presets, schedule and per-app rules live in `%APPDATA%\ASUSDisplayControl\`
(`dwc_settings.json`, `dwc_presets.json`, `dwc_schedule.json`, `dwc_apptweaks.json`).

## Preset memory vs. factory defaults

There is no built-in table of factory defaults — the app snapshots a preset's current
values the first time it sees that preset and writes them back on every later switch. Two
consequences worth knowing when reading the code:

- Whatever the monitor happened to hold at first sight becomes that preset's baseline, so
  a preset the user had already tuned is remembered tuned, not factory.
- Which properties are genuinely per-preset is the panel's business. A VA24EHF keeps
  brightness and colour temperature per Splendid mode (Standard 90/6500K, Reading 25/7500K,
  Darkroom 0, Scenery and sRGB 100, contrast 80 throughout) but has a single global set of
  RGB gains (factory 100/100/100); re-writing them per preset is what makes them feel
  per-preset. `reset-mode` restores the mode but not colour — `reset-color` does that.

## Run / develop

```powershell
dotnet run --project csharp/AsusDisplayControl.csproj
```

Requires the .NET 8 SDK. The bundled CLI is copied from `cli/windows/dwc/`; if that
folder is empty, unzip `cli/windows/dwc_win.zip` into it first.

## Build a distributable

```powershell
powershell -ExecutionPolicy Bypass -File csharp/build.ps1                # tiny (default)
powershell -ExecutionPolicy Bypass -File csharp/build.ps1 -SelfContained # no .NET needed
```

Output goes to `csharp/publish/`. Installer: install
[Inno Setup](https://jrsoftware.org/isdl.php) and run `iscc csharp/installer.iss`
(the CI workflow does this on a `v*` tag).

## Size & memory

| | Install size | Working set | Private bytes |
|---|---|---|---|
| ASUS DisplayWidgetCenter (original) | ~107 MB | — | ~60 MB |
| C# framework-dependent (default) | **~1.3 MB** | ~50 MB | **~14 MB** |
| C# self-contained (`-SelfContained`) | ~145 MB | ~50 MB | ~14 MB |

Default is **framework-dependent**: a few MB, but the PC needs the
[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (Windows will
prompt to install it if missing). `-SelfContained` bundles the runtime so nothing else
is needed, at the cost of size. Either way the *running* footprint is the same — private
RAM is ~14 MB, roughly a third of the original.

The build is one-folder on purpose (not single-file): loose DLLs stay memory-mapped and
shared. A compressed single-file build decompresses the whole runtime into RAM, nearly
doubling the working set.
