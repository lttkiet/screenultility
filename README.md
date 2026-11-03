# Screen Utility

A lightweight Windows system tray utility for controlling display brightness, contrast, and orientation with a Windows 11 Fluent design.

## Features

- **Hardware Control**: Direct DDC/CI (external monitors) and WMI (internal laptop displays).
- **Friendly Display Names**: Auto-detects models and panels (e.g. `"Built-in display"`, `"Acer K222HQL"`).
- **Display Orientation**: Quickly toggle Landscape, Portrait, and Inverted modes.
- **Fluent UI**: Windows 11 dark/light theme, rounded corners, and native backdrop shadow.
- **Tray Scroll Control**: Hover over the tray icon and roll the mouse wheel to adjust brightness with an OSD overlay.
- **Sync & Grouping**: Linked brightness mode across all connected monitors.

## Controls

| Action | Result |
| :--- | :--- |
| **Left-Click Tray** | Toggle main Displays flyout |
| **Right-Click Tray** | Open Fluent context menu (Settings, toggles, quit) |
| **Scroll on Tray** | Adjust primary screen brightness up/down with OSD feedback |
| **Click Outside** | Auto-dismisses open flyout or context menu |

## Getting Started

### Requirements
- Windows 10 (2004+) or Windows 11
- [.NET 8 Runtime / SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build & Run

```powershell
# Build in Release mode
dotnet build ScreenUtility.sln -c Release

# Run the app
.\src\ScreenUtility\bin\Release\net8.0-windows10.0.19041.0\ScreenUtility.exe
```

## License

[GNU Affero General Public License v3.0 (AGPLv3)](LICENSE)
