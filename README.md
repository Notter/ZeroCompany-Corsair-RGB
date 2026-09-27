# Corsair RGB Keyboard Integration — Star Wars Zero Company

Source for the 1.0.0 release, provided for inspection and Nexus Mods review.

## Build the executable

Requirements: Windows x64, .NET SDK (the submitted release used 10.0.400), and the Windows .NET Framework 4.x assemblies under `%WINDIR%/Microsoft.NET/Framework64/v4.0.30319`.

From PowerShell in this repository:

```powershell
dotnet --version
.\build.ps1
```

Output: `dist/ZeroCompanyRGB/ZeroCompanyRGB.exe`.

This invokes the SDK's C# compiler directly. No NuGet packages, game installation, game assemblies, API keys or network connection are needed to compile. The standalone build script locates the installed SDK instead of relying on the original workspace path. The application source and compilation options are the release source/options. Binary hashes may differ across rebuilds/toolchains; this is not a claim of a bit-for-bit reproducible build.

## Corsair runtime dependency

The mod also ships `iCUESDK.x64_2019.dll`, an unmodified third-party binary from [Corsair's official iCUE SDK 4.0.84 release](https://github.com/CorsairOfficial/cue-sdk/releases/tag/v4.0.84). It is not built from this project's C# source. Its own terms apply; see THIRD-PARTY-NOTICES.txt and the original SDK documentation supplied with the mod.

For a runnable build, download and extract Corsair's SDK, then run:

```powershell
.\build.ps1 -SdkPath 'C:\path\to\iCUESDK'
```

`SdkPath` is the directory containing `redist/x64/iCUESDK.x64_2019.dll`. The script copies that runtime alongside the helper. No download happens automatically.

Install the game-specific UE4SS loader separately, then place `dist/ZeroCompanyRGB` in the game's `SWZeroCompany/Binaries/Win64/ue4ss/Mods` folder. iCUE must be running with SDK device control enabled. The helper is for use with the game; if the game is not running it exits without starting keyboard control.

## Tests

```powershell
.\test.ps1
```

This runs 48 C# checks without loading iCUE or launching the game. The test script uses the standard `C:\Program Files\dotnet` installation path.

Optional Lua reader checks require Python and the development-only Lupa package:

```powershell
python -m pip install --target tests/vendor lupa==2.8
python tests/test_reader.py
```

These 35 checks mock game objects, file writes and process launching. They do not control the game or keyboard.

## What the mod does

- `mod/Scripts/main.lua`: UE4SS reads reflected game state on the game thread four times per second. It writes `state.txt` in this mod's own folder and starts the fixed bundled helper via PowerShell `Start-Process -WindowStyle Hidden`.
- `src/Program.cs`: a windowless process reads local snapshots and settings, renders at up to 20 Hz, and communicates with the local Corsair SDK. It checks for `SWZeroCompany` by process name and exits when the game closes.
- `src/Native.cs`: P/Invoke declarations for the bundled Corsair SDK, including its session callback and LED functions.
- `src/State.cs`: snapshot validation, settings and lighting-event detection.
- `src/Renderer.cs`: keyboard colors, resource displays and animations.

The helper uses a named mutex to avoid duplicate instances. It writes `bridge.log` locally and releases its SDK lighting layer on stale input, disable, errors and orderly exit. A large prior log is renamed locally on startup.

The project code does not implement internet requests, telemetry, downloads, credential access, registry changes, scheduled tasks, services, game-memory injection, save edits or gameplay actions. Corsair's DLL is a third-party runtime communicating with iCUE; these source files do not expose its implementation. The helper does not request elevation. The PowerShell launcher runs only while the UE4SS mod is loading.

The source bundle excludes game assets, local logs, captures, compiled files and third-party test runtimes. No new redistribution license is assigned to the author's source by this review bundle.
