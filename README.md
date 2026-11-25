# ToadIQ

![ToadIQ mouse logo](src/Moveiq.App/Assets/ToadIQ.png)

ToadIQ is a Windows mouse control app built around the Portronics Toad One. It uses standard Windows input events and does not require a vendor driver. The interface uses a transparent, mouse-shaped ToadIQ icon.

## Features

- Remap the Back, Forward, and scroll wheel click buttons to common actions or launch a selected application.
- Configure five two-button combinations.
- View button and wheel event counts and a live chart of pointer-move intervals reported by Windows.
- Click the mouse diagram to choose a button mapping.

The movement chart shows the intervals between Windows cursor-move events. It does not measure the mouse's wireless transmission timing, sensor polling rate, or hardware latency. ToadIQ does not change the mouse's DPI stages, lighting, firmware, or battery.

Mouse events are observed locally while ToadIQ is running. Custom button remapping only intercepts the selected buttons while **Turn on custom buttons** is enabled.

## Requirements

- Windows 10 or Windows 11
- .NET 8 SDK to build from source

## Build and run

```powershell
dotnet build Moveiq.sln --configuration Release
dotnet run --project src/Moveiq.App/Moveiq.App.csproj
```

To publish a self-contained Windows x64 executable:

```powershell
dotnet publish src/Moveiq.App/Moveiq.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false --output artifacts/ToadIQ-win-x64
```

The same publish command runs in the GitHub Actions workflow. Its build artifact contains `ToadIQ.exe`.

## Get the Windows executable from GitHub Actions

After a push or pull request build finishes, open the workflow run under **Actions** and download the `ToadIQ-win-x64` artifact. It contains a self-contained Windows x64 executable.
