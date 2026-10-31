<div align="center">

<img src="docs/assets/logo.png" alt="Spotify Desktop Logo" width="96" height="96" />

# Spotify Desktop
### Native Windows Client • 100% Ad-Free • iPhone-Style Dynamic Island • Studio Dolby DSP

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/ArisaAkiyama/ytm-desktop)
[![Framework](https://img.shields.io/badge/.NET%208.0-WPF%20%2B%20WebView2-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![AdBlock](https://img.shields.io/badge/AdBlock-4--Layer%20Engine-1DB954?style=for-the-badge&logo=spotify&logoColor=white)](https://github.com/ArisaAkiyama/ytm-desktop)
[![UI](https://img.shields.io/badge/Dynamic%20Island-60%2F120%20FPS%20VSync-000000?style=for-the-badge&logo=apple&logoColor=white)](https://github.com/ArisaAkiyama/ytm-desktop)

<p align="center">
  A fast, lightweight, native Windows desktop application for <b>Spotify</b>.<br/>
  Preserves the <b>100% official Spotify Web UI (Zero AI Slop)</b> while adding a 4-layer ad-blocking engine, 7-stage Studio Dolby Spatial Audio DSP, and a hardware-accelerated <b>Dynamic Island</b> overlay on your Windows desktop.
</p>

</div>

---

## ✨ Key Features

### 🛡️ 4-Layer Ad-Block & Stream Guard Engine
| Layer | Mechanism | Description |
| :--- | :--- | :--- |
| **Layer 1** | **Native Network Filter** | Intercepts & neutralizes `ad-logic`, `ads/v1–v3`, `gabo-receiver`, `adeventtracker`, and DoubleClick requests at the C# `CoreWebView2.WebResourceRequested` level without breaking `spclient` music streams. |
| **Layer 2** | **Fetch / XHR / JSON Sanitizer** | Hooks `window.fetch`, `XMLHttpRequest`, and `JSON.parse` to strip `ad_slots`, `adBreak`, and `adPlacements` before Spotify's state machine processes them. |
| **Layer 3** | **Off-DOM Media Hook & Auto-Skip** | Intercepts `document.createElement('audio'|'video')`, `window.Audio`, and `HTMLMediaElement.prototype.play` at `document-start` to mute and fast-forward ad streams in real time. |
| **Layer 4** | **Surgical Cosmetic Cleaner** | Hides *Explore Premium*, *Install App*, and sponsored banners while keeping the official Spotify Web layout 100% intact. |

### 🎧 7-Stage Studio Dolby Spatial Audio DSP
- **Real-Time Web Audio API Graph**: Connects directly to Spotify's internal `HTMLMediaElement` with a 6-Band Parametric EQ, Psychoacoustic Sub-Bass Harmonic Exciter (`4x` oversampled), Bass-Safe Mid-Side Stereo Widener (`160Hz` crossover), Binaural `38ms` Early Reflections Convolver, Multi-Knee Compressor, and True-Peak Soft Clipper Limiter.
- **5 Acoustic Presets & Device Optimization**: Dedicated profiles for *Headphones / TWS* and *Laptop Speakers* with *Music (Balanced)*, *Cinema 3D*, *Bass Boost*, *Vocal Clarity*, and *Dynamic* presets.

### 🏝️ iPhone-Style Dynamic Island for Windows
- **True Shared-Element Morphing (`60/120 FPS VSync`)**: Built on `CompositionTarget.Rendering` critically-damped spring physics (`0.0 → 1.0` progress driver) so the capsule, album art (`24px → 54px`), typography, and Spotify green waveform (`#1DB954`) morph seamlessly without layout snapping.
- **Smart Desktop-Only Visibility (*"Show Desktop"*)**: Automatically detects window Z-Order (`IsDesktopCurrentlyShown`) so the Dynamic Island appears **only on your Windows Desktop** when minimized and **never blocks active applications**.

### 🖥️ Native Windows Integration
- **Immersive Dark Mode Title Bar**: Uses Win32 `DWMWA_USE_IMMERSIVE_DARK_MODE` (`#000000`) to blend seamlessly with Spotify Web's dark theme.
- **System Tray & Background Play**: Closing the main window minimizes the app smoothly to the Windows System Tray with a dark-themed context menu.
- **Adaptive Monitor Sizing**: Automatically scales to fit any screen resolution (`1366×768`, `1080p`, `1440p`, `4K`) with `PerMonitorV2` High-DPI awareness.

---

## 📂 Project Architecture

```text
Spotify_noads/
├── docs/assets/                 # Logo & visual documentation assets
├── src/SpotifyDesktop/
│   ├── Assets/                  # Official multi-resolution icon (spotify.ico, 16px–256px)
│   ├── Interop/                 # Win32 DWM dark mode & Z-Order desktop detection (NativeMethods.cs)
│   ├── Models/                  # Real-time media telemetry & settings models
│   ├── Scripts/                 # 4-layer JS ad-blocker, off-DOM media hook & Dolby DSP (spotify-adblock-engine.js)
│   ├── Services/                # Native network request blocker, settings & System Tray manager
│   ├── DynamicIslandWindow.xaml # VSync Shared-Element Morphing Dynamic Island UI
│   └── MainWindow.xaml          # Main WebView2 host window + Monochromatic Settings Overlay
├── Installer/
│   ├── Spotify.iss              # Inno Setup 6 installer configuration
│   └── build-installer.ps1      # Self-contained single-file publish & installer build script
└── SpotifyDesktop.sln
```

---

## 🚀 Installation & Building from Source

### Option 1: Universal Windows Installer (Recommended)
To compile `dist/Spotify_Setup_v1.0.0.exe` yourself:
```powershell
powershell -ExecutionPolicy Bypass -File .\Installer\build-installer.ps1
```

### Option 2: Run from Source (Development)
```powershell
dotnet run --project src\SpotifyDesktop\SpotifyDesktop.csproj -c Release
```
