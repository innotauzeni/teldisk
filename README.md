# TelDiskCleaner

Free up space and speed up your Windows PC.

![TelDiskCleaner logo](src/TelDiskCleaner.App/Assets/logo-512.png)

## Download

**[Download TelDiskCleaner v1.1.0 for Windows (64-bit)](https://github.com/innotauzeni/teldisk/raw/main/release/TelDiskCleaner-v1.1.0-win-x64.exe)**

- Works on Windows 10 and 11 (64-bit).
- No installation: download the `.exe` and double-click it.
- The app isn't code-signed yet, so Windows may show "Windows protected your PC". Click **More info**, then **Run anyway**.

## What it does

- **Disk Analyzer**: scan any drive or folder and see every folder sorted by size, biggest first.
- **Dev Project Cleaner**: finds `node_modules`, `dist`, .NET `bin`/`obj`, Python `venv` and Flutter `build`/`.dart_tool` folders in projects you haven't touched for 60+ days (by last git commit, or file dates when there's no git). Reinstall them any time with `npm install`, `flutter pub get` and so on.
- **Junk Cleanup**: temp files, Recycle Bin, Windows Update downloads, crash dumps, error reports, browser caches (Chrome, Edge, Brave, Firefox) and developer caches (npm, Yarn, NuGet, pip, Flutter pub, Gradle, Maven, Go, Composer). Shows the size before anything is deleted.
- **Startup Apps**: turn off programs that slow down sign-in. Works like Task Manager's Startup tab, so apps can be turned back on any time.
- **Processes**: see which apps use the most memory and CPU and end them. Core Windows processes are protected, and leftover developer tools (Node.js, .NET, Java, Android emulator and others) are labelled.

Everything that deletes or ends something asks you to confirm first. Some locations (Windows temp, Windows Update files, all-users startup apps) need **Restart as administrator** from the sidebar.

## Build from source

Requires the .NET 8 SDK.

```
dotnet test src/TelDiskCleaner.Core.Tests
dotnet run --project src/TelDiskCleaner.App
```

Publish a single-file exe:

```
dotnet publish src/TelDiskCleaner.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
```

## Contact

+263 77 491 4150 · info@daraerp.co.zw
