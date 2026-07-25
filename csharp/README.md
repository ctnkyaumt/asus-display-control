# ASUS Display Control — C# (WinForms) edition

A native .NET 8 / WinForms app for controlling ASUS monitors via the bundled `dwc.exe`
CLI. Low memory, fast, flicker-free.

## Features

- Four pages, picked from the sidebar: **Splendid** (picture), **System Setup**,
  **GamePlus & OSD**, and **Per-App Tweak**.
- **Splendid presets** seeded with the app's own defaults per preset, then per-preset
  memory — tweak a preset and it's restored next time you return to it, with minimal
  switch flash.
- **Single instance** (named mutex): two copies fight over the DDC bus and over each
  other's preset switches.
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

## Preset defaults

`MainForm.PresetDefaults` holds the app's own picture for each preset (see the table in
the [root README](../README.md#-preset-defaults)). `SeedDefaults` writes them into
`dwc_presets.json` the first time the user switches to a preset, or when they press
**Preset Defaults**; after that the preset's own memory wins. Notes for reading the code:

- Only properties in `_supportedProps` for that monitor are written, so a panel without
  Shadow Boost or saturation just gets the parts it understands.
- Colour temperature is a *target Kelvin*. `SeedDefaults` picks the nearest code the
  monitor advertises if it is within 750 K, otherwise it uses the User slot and warm RGB
  gains — many panels stop at 6500 K, and calling that "warm" would make Reading and
  Night View identical to Standard.
- The monitor's own factory values (VA24EHF: Standard 90/6500 K, Reading 25/7500 K,
  Darkroom 0, Scenery and sRGB 100, contrast 80 throughout, gains 100/100/100) are still
  reachable through `reset-mode` / `reset-color` / `reset-all`; `reset-mode` restores the
  mode but not colour.

### The blue light filter locks Splendid

With `BlueLightFilter > 0` an ASUS monitor **silently ignores** `set Splendid` — the write
succeeds and nothing changes, which jams preset switching and the per-app watcher. So the
filter is 0 in every default, `SetPreset` clears it before changing mode, and
`ApplyPresetSettingsParallel` writes it last, after the rest of the picture.

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
