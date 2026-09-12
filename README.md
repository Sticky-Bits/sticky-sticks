# Sticky Sticks

A Windows controller visualizer and thumbstick analyzer, with a settings preview for **Rivals of Aether 2**. Version **0.1**.

## Run

Download the `StickySticks-vX.Y-win-x64.zip` asset from **Releases** on GitHub, extract it, then run `StickySticks.exe`.

The repository contains source code, not a ready-to-run application. If no release is available, or you downloaded **Code → Download ZIP**, follow **Build and test** below, then run `publish/StickySticks.exe`.
Keep `SDL3.dll`, `gamecontrollerdb.txt`, the app DLL/JSON files, and licence files beside the executable (preserving subfolders).

Requires Windows x64 and the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The SDK is only needed to build. If the runtime is missing, the launcher offers a download link. Rivals and Steam are not required. Downloads are unsigned and may trigger Windows reputation warnings.

## Input sources

- **SDL 3:** preferred automatically when available; uses official upstream SDL, not a game-supplied DLL.
- **XInput:** Xbox-compatible and Steam-emulated controllers.
- **Direct USB:** Nintendo Switch Pro controllers with stored calibration.
- **Windows joystick:** legacy WinMM devices, with configurable axes.

Select a device at the top. Refresh preserves the current device if it remains available. Settings contains axis mappings, inversions, display rounding, and the optional octagonal reference gate. SDL Y axes are inverted by default for positive-up diagrams. WinMM defaults to X/Y and R/U; some Switch Pro drivers expose malformed legacy axes, so SDL is preferable for those devices.

To compare Steam Input, add the app as a non-Steam game and launch it through Steam with matching controller settings. Steam may pause emulated input when the application loses focus. The connection indicator reports focus, not proof of continued input delivery.

## Build and test

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then run in PowerShell:

```powershell
./build.ps1
```

Output goes to `publish/`. Close a running copy before rebuilding. Native dependencies and their licences are included in this repository; no game installation or NuGet packages are needed. `NuGet.Config` intentionally clears external feeds. The build is framework-dependent, not self-contained.

```powershell
$test = Start-Process ./publish/StickySticks.exe -ArgumentList '--self-test' -Wait -PassThru
if ($test.ExitCode -ne 0) { throw 'Tests failed' }
```

## Licence

Sticky Sticks is [MIT licensed](LICENSE). SDL and SDL GameControllerDB retain their [zlib licences](THIRD_PARTY_NOTICES.md). This independent project is not affiliated with Aether Studios, Nintendo, Microsoft, or Valve.