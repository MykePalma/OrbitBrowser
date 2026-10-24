# Orbit

<img src="Assets/orbit.svg" width="72" align="right" alt="Orbit logo">

A lightweight Windows browser built on real Chromium (via [CefSharp](https://github.com/cefsharp/CefSharp)), with a custom title bar, split view, bookmark folders and a dark interface.

### [⬇ Download Orbit for Windows](https://github.com/MykePalma/OrbitBrowser/releases/latest/download/Orbit-win-x64.zip)

Unzip anywhere and run `Orbit.exe`. No installer, and no .NET install needed (the .NET runtime is included). Windows 10 or 11, 64-bit. The download is about 250 MB because it carries the whole Chromium engine.

![Orbit new tab page](docs/screenshot.png)

## Features

- **Real Chromium engine**: GPU-accelerated rendering and the same web compatibility as Chrome.
- **Custom title bar**: tabs sit in the window frame to give pages as much room as possible.
- **Tabs**: drag to reorder, drag a tab out to open it in its own window (or drop it into another Orbit window), pin, mute, duplicate, reopen closed tabs, session restore.
- **Split view**: show two tabs side by side with a draggable divider.
- **Bookmarks**: a bookmarks bar, nested folders, site icons, a bookmark manager with drag-and-drop, and HTML import/export that other browsers can read.
- **Address bar**: search or type a URL, with suggestions from your bookmarks and history.
- **Find in page, zoom, print, save as PDF, view source, DevTools, full screen.**
- **Private windows and tabs** that leave no history or cookies behind.
- **Downloads manager** with progress, pause, cancel and open-folder.
- **Ad and tracker blocking** (a built-in list of common ad and tracking domains; can be turned off).
- **Settings**: search engine, home page, startup behaviour, download folder, forced dark mode for web pages.

See the **[Wiki](https://github.com/MykePalma/OrbitBrowser/wiki)** for the full guide and keyboard shortcuts.

## Limitations

- No browser extensions.
- No H.264 / AAC video: the stock Chromium Embedded Framework build leaves out those proprietary codecs, so sites that offer only them won't play video. Sites that also offer VP9/AV1/Opus (such as YouTube) work.
- No account sync and no built-in password manager.
- Windows only.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer) on Windows.

```bash
git clone https://github.com/MykePalma/OrbitBrowser.git
cd OrbitBrowser
dotnet run -c Release
```

To produce the downloadable build:

```bash
dotnet publish Lite.csproj -c Release -r win-x64 --self-contained true -o publish/Orbit
```

### Automated self-test

`Orbit.exe --selftest` runs a scripted end-to-end check of tabs, navigation, bookmarks, split view, downloads and more against a local test server. It uses a temporary profile, never your own, and writes `selftest.log` to `%TEMP%\Orbit-selftest`.

## Your data

Bookmarks, history, settings and the browser cache live in `%APPDATA%\Orbit`.

## Status

Early release (v1.0.0). Bug reports and ideas are welcome in [Issues](https://github.com/MykePalma/OrbitBrowser/issues).
