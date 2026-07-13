# ASUS Display Control — C# (WinForms) edition

A native .NET 8 / WinForms app for controlling ASUS monitors via the bundled `dwc.exe`
CLI. Low memory, fast, flicker-free.

## Features

- **Splendid presets** with per-preset memory — tweak a preset's brightness/contrast/
  gains and it's restored next time you return to it, with minimal switch flash.
- Live **Brightness, Contrast, Trace Free, Saturation, Hue, RGB gains**, **Shadow Boost**,
  **ASCR**, and capability-aware **Color Temp** (only shows codes the monitor supports).
- **Compare** (press-and-hold), **Reset**, **Import/Export** profiles.
- **System tray** icon, close-to-tray, and **Start with Windows**.
- **Scheduled preset switching** (`Schedule…`):
  - *Fixed times* — e.g. 09:00 → Standard, 19:00 → Darkroom (wraps past midnight).
  - *By daylight* — enter your latitude/longitude; switches between a day preset and a
    night preset at sunrise/sunset (computed with the NOAA solar algorithm, in UTC).
  - Switches only when the scheduled target changes, so it never fights a manual choice.
  - Runs while the app is open — pair with *Start with Windows* + tray for set-and-forget.

Presets/settings/schedule live in `%APPDATA%\ASUSDisplayControl\`.

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
