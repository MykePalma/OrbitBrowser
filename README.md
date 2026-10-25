# Orbit

<img src="Assets/orbit.svg" width="72" align="right" alt="Orbit logo">

A lightweight Windows browser built on Chromium (through [Microsoft Edge WebView2](https://developer.microsoft.com/microsoft-edge/webview2/)), with a custom title bar, split view, bookmark folders, Chrome extensions and a dark interface.

### [⬇ Download Orbit for Windows](https://github.com/MykePalma/OrbitBrowser/releases/latest/download/Orbit-win-x64.zip)

Unzip anywhere and run `Orbit.exe`. No installer, and no .NET install needed (the .NET runtime is included). Windows 10 or 11, 64-bit. The download is about 75 MB: the Chromium engine comes from the Microsoft Edge WebView2 Runtime that Windows already includes (Orbit offers to open its download page if a computer doesn't have it).

![Orbit new tab page](docs/screenshot.png)

## Features

- **Real Chromium engine**: GPU-accelerated rendering, the same web compatibility as Chrome, and H.264 / AAC video. The engine updates itself through Windows.
- **Extensions**: load Chrome extensions (Manifest V2 and V3) and install **uBlock Origin** in one click from Settings → Extensions. See the limitations below.
- **Custom title bar**: tabs sit in the window frame to give pages as much room as possible.
- **Tabs**: drag to reorder, drag a tab out to open it in its own window (or drop it into another Orbit window), pin, mute, duplicate, reopen closed tabs, session restore.
- **Split view**: show two tabs side by side with a draggable divider.
- **Bookmarks**: a bookmarks bar, nested folders, site icons, a bookmark manager with drag-and-drop, and HTML import/export that other browsers can read.
- **Address bar**: search or type a URL, with suggestions from your bookmarks and history.
- **Find in page, zoom, print, save as PDF, view source, DevTools, full screen.**
- **Private windows and tabs** that leave no history or cookies behind.
- **Downloads manager** with progress, pause, cancel and open-folder.
- **Ad and tracker blocking** built on the standard EasyList and EasyPrivacy filter lists (over 100,000 rules, downloaded from easylist.to and refreshed automatically). It stops ads and trackers on web pages, hides the empty boxes they leave behind, shows a count in a shield button and lets you pause it per site. It can't remove ads streamed inside videos (YouTube's, for example); for that, install uBlock Origin from Settings → Extensions (Orbit then turns its own blocker off).
- **Automatic updates** (optional): Orbit checks GitHub for a newer release when it starts, downloads and verifies it in the background, and installs it the next time you open the browser.
- **Settings**: search engine, home page, startup behaviour, download folder, forced dark mode for web pages.

See the **[Wiki](https://github.com/MykePalma/OrbitBrowser/wiki)** for the full guide and keyboard shortcuts.

## Limitations

- Extensions work, but WebView2 gives Orbit no extension toolbar, pop-ups or Chrome Web Store. Extensions are added from an unpacked folder or a .zip (or uBlock Origin's one-click install) and managed in Settings → Extensions; extension pop-up windows are not available yet.
- Needs the Microsoft Edge WebView2 Runtime (already part of Windows 11 and current Windows 10).
- No account sync and no built-in password manager.
- Windows only.

## Build from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (or newer) and the WebView2 Runtime on Windows.

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

## Updates

Settings → **Keep Orbit up to date automatically** (on by default). At start-up Orbit asks the GitHub Releases API for the newest release. If it is newer, Orbit downloads `Orbit-win-x64.zip`, checks it against the SHA-256 checksum GitHub publishes for that file, and unpacks it into a staging folder. A banner offers **Restart to update**; if you ignore it, the update is installed automatically the next time you open Orbit. Nothing is installed unless the checksum matches, and Orbit only contacts `api.github.com` and the release download for updates (and easylist.to for the ad-blocking filter lists, and GitHub again if you ask it to install uBlock Origin). Turn the setting off to disable the start-up check; **Check for updates** in Settings still works on demand.

## Filter lists

The ad blocker uses [EasyList](https://easylist.to/) and EasyPrivacy. Orbit downloads them at run time and does not redistribute them; they are the work of their authors and licensed separately (CC BY-SA 3.0 / GPLv3+).

## Your data

Bookmarks, history, settings, extensions and the browser cache live in `%APPDATA%\Orbit` (set the `ORBIT_DATA_DIR` environment variable to keep them somewhere else).

## Status

Early release. Bug reports and ideas are welcome in [Issues](https://github.com/MykePalma/OrbitBrowser/issues).
