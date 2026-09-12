# Sticky Sticks

A Windows controller visualizer and thumbstick analyzer, with a settings preview for **Rivals of Aether 2**. Version **0.1**.

## Run

Extract the entire application ZIP into a folder, then run `StickySticks.exe`.
Keep `SDL3.dll`, `gamecontrollerdb.txt`, the app DLL/JSON files, and licence files beside the executable (preserving subfolders).

Requires Windows x64 and the [.NET 10 Desktop Runtime (x64)](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). The SDK is only needed to build. If the runtime is missing, the launcher offers a download link. Rivals and Steam are not required. Downloads are unsigned and may trigger Windows reputation warnings.

## Input sources

- **SDL 3:** preferred automatically when available; uses official upstream SDL, not a game-supplied DLL.
- **XInput:** Xbox-compatible and Steam-emulated controllers.
- **Direct USB:** Nintendo Switch Pro controllers with stored calibration.
- **Windows joystick:** legacy WinMM devices, with configurable axes.

Select a device at the top. Refresh preserves the current device if it remains available. Settings contains axis mappings, inversions, display rounding, and the optional octagonal reference gate. SDL Y axes are inverted by default for positive-up diagrams. WinMM defaults to X/Y and R/U; some Switch Pro drivers expose malformed legacy axes, so SDL is preferable for those devices.

To compare Steam Input, add the app as a non-Steam game and launch it through Steam with matching controller settings. Steam may pause emulated input when the application loses focus. The connection indicator reports focus, not proof of continued input delivery.

## Rivals of Aether 2

The red dot is normalized input; the blue dot is the processed output. Left-stick processing applies an independent-axis rescaled deadzone followed by sensitivity. Right-stick processing uses a fixed 20% deadzone and 100% sensitivity. Orange guides and detectors illustrate hard-press and right-stick thresholds. Rounding affects display only. The square marks ±1; the circle/octagon is a visual reference. Display targets 60 FPS, with an actual FPS counter.

As of version 1.7.1, the character select settings menu and full settings menu can show different percentage values for the same underlying setting. For more precise settings, read and set the values from the full settings menu.

This tool approximates game behaviour; it is not an official game component and does not read or modify game saves.

## Thumbstick Analyzer

Start with **Start Test**, or press RT after releasing it. Tests progress automatically; each stick completes its own tasks independently. Stop Test cancels the run. Leaving the page stops capture. Input capture runs independently of display FPS.

1. **Circular Sweeps:** follow the clockwise then counter-clockwise prompts. Thirty-six 10° slices retain their maximum radius. Filling all buckets earns 5% progress; 25 qualifying 0.1-second intervals in each direction earn the other 95%. An interval requires movement between buckets, a reading above radius 0.9, and no new maximum. Quadrant order determines direction; wrong/unknown direction neither records maxima nor advances time. Both directions share buckets.
2. **Perimeter Releases:** hold each marked target beyond radius 0.9 for one second, then release and do not touch the stick during the one-second recording. Targets are top, bottom, left, right, top left, bottom left, top right, bottom right, NNW, NNE, ENE, ESE, SSE, SSW, WSW, WNW. Acceptance is ±11.25°. Snapback is the largest opposite-hemisphere distance. Jitter uses X/Y ranges from the final half-second, averaged across releases. Centering uses the worst radius of a release's averaged settled position.
3. **Fast Movement:** waits until each stick has exceeded radius 0.8, then counts changed readings for eight seconds. The reported Hz is changes divided by elapsed seconds; direct USB counts actual reports. This estimates the rate visible to the application, not guaranteed internal hardware sampling frequency.

Results show perimeter slices, snapback lines for peaks above 0.1, and a purple dot marking the worst settled center. Snapback colors run from green at 0.1 to red at 0.5. Perimeter colors: <0.9 red; 0.9–0.99 orange/yellow; 0.99–1.01 green; 1.01–1.1 cyan/blue; >1.1 purple.

The app-defined score weights perimeter 40%, snapback 40%, jitter 10%, centering 10%. Combined scores average both sticks equally. Only overall scores receive letters: A+ ≥95, A ≥90, B ≥80, C ≥70, D ≥60, otherwise F. These grades are a comparison aid, not a hardware certification. Jitter scores use the worse averaged axis; 0.0031 scores 75/100. Full scoring thresholds are in `AnalyzerScore.cs`.

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

On an interactive Windows desktop, `--analyzer-smoke-test <absolute-report-path>` runs synthetic input through the complete analyzer and writes a PASS/FAIL report plus a results PNG. `--analyzer-capture-check <absolute-report-path>` checks connected SDL devices. Diagnostics may contain device names/paths; review before sharing. GitHub Actions builds and runs self-tests on Windows, then uploads the publish folder as an artifact.

## Repository and releases

Commit source, `native/`, licences, and documentation. Generated `bin/`, `obj/`, `publish/`, caches, diagnostics, and private `analysis/` notes are ignored. Do not upload the entire working directory manually: use Git so these exclusions apply.

For a binary release, ZIP the contents of `publish/`, including `LICENSE`, `THIRD_PARTY_NOTICES.md`, and `licenses/`. PDB debugging files are optional. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for pinned dependency sources and checksums.

### Tag-driven releases

Push a version tag such as `v0.1` or `v0.1.1` on a commit containing `.github/workflows/release.yml`. The Release workflow builds that tagged commit on Windows, stamps the executable version from the tag, runs self-tests, and creates a GitHub Release with `StickySticks-v0.1-win-x64.zip` and its SHA-256 checksum. The ZIP includes dependencies and licence notices, excludes PDB files, and still requires .NET 10 Desktop Runtime x64. The workflow uses GitHub's built-in token; no personal token is needed.

Only tags shaped `vMAJOR.MINOR` or `vMAJOR.MINOR.PATCH` are accepted. Branch pushes and pull requests only build/test. Tagging does not modify the version in the source project. Each release needs a new tag; the workflow does not overwrite an existing release.

## Licence

Sticky Sticks is [MIT licensed](LICENSE). SDL and SDL GameControllerDB retain their [zlib licences](THIRD_PARTY_NOTICES.md). This independent project is not affiliated with Aether Studios, Nintendo, Microsoft, or Valve.