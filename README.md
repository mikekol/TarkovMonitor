# TarkovMonitor

[![build-dev](https://github.com/the-hideout/TarkovMonitor/actions/workflows/build-dev.yml/badge.svg)](https://github.com/the-hideout/TarkovMonitor/actions/workflows/build-dev.yml)

[![Discord](https://img.shields.io/discord/956236955815907388?color=7388DA&label=Discord)](https://discord.gg/XPAsKGHSzH)

![image](https://github.com/the-hideout/TarkovMonitor/assets/1557581/99602d29-98c8-4738-8757-0fa763d54e9a)

TarkovMonitor is an Escape from Tarkov companion application that provides useful audio notifications, can automatically update your task progress on Tarkov Tracker, and includes other helpful features.

## Features

- Audio notifications
    - Match found
    - Raid starting
    - Restart failed tasks
    - Runthrough time elapsed
    - Turn air filter on/off
    - Scav cooldown
    - Quest Items Reminder (does not check for actual quest items, just a friendly reminder to allow you to back out of matching)
    - Customizable sounds for all of the above
- Goon tracking
    - Submit reports when you see the Goons to help other players find them
- Connect to the Tarkov.dev website via remote code
    - Automatically load the website map for the map you're playing on
    - Take an in-game screenshot and show your position on the website map
    - **Control multiple Tarkov.dev browser instances** - Use the same position and zoom commands across multiple monitors or windows
    - **Auto-zoom on location detection** - Automatically zoom in when your player position is detected via screenshot (configurable zoom level: 100-400%)
- Connect to Tarkov Tracker via API token
    - Automatically mark quests as complete as you complete them
- Statistics (all stored locally on your computer)
    - Track your total sales on the flea market
    - Track how many times you play on each map
- Visual Timers (have that friend that never heard the audio and asks "has the runthrough timer happened yet?")
   - Displays "Time in Raid"
   - Displays countdown for "Runthrough time"
   - Display countdown for Scav cooldown time

## Installation

Head on over to the [latest release](https://github.com/the-hideout/TarkovMonitor/releases/latest) page for this project. Download the `TarkovMonitor-<version>.msi` installer from the Assets section and run it.

The installer will:
- Install the **TarkovMonitor Windows Service**, which monitors your EFT log files in the background (even when the UI is closed).
- Install the **TarkovMonitor UI**, which connects to the service and displays notifications and stats.
- Add a firewall rule allowing the service to accept local connections on port 50051.
- Create Start Menu and Desktop shortcuts.

After installation, launch **TarkovMonitor** from the Start Menu or Desktop shortcut. The service starts automatically with Windows.

## Setup

On its own, TarkovMonitor will play audio notifications (e.g., when you match into a raid and when the raid begins). But its most useful features are unlocked when used in conjunction with other tools.

### Quest Tracking with TarkovTracker

[Tarkov Tracker](https://tarkovtracker.org) is a free website that allows you to track your quest progress. Once you log in to create a Tarkov Tracker account, you can share your quest progress with other tools (including TarkovMonitor) by creating an API token. Navigate to the [Tarkov Tracker settings page](https://tarkovtracker.org/settings), click the `create a token` button, and create a token that has permissions to `get progression` and `write progression`. You can give the token any name you want, but if you're creating it for Tarkov Monitor, it makes sense to name it `Tarkov Monitor`. Then click the `create token` button and click the token's copy button. Do not try to manually highlight the displayed token and copy it; some of the displayed token's characters are obfuscated with asterisks (*). Once you've copied the token, paste it in the Tarkov Tracker API token box in Tarkov Monitor settings and click the `Test Token` button. If you see a pop up indicating success, Tarkov Tracker is ready to start automatically updating your progress on Tarkov Tracker.

Tarkov Tracker only automatically marks a quest as complete if it's running when the quest is completed. If you already completed a bunch of quests prior to running Tarkov Monitor, see [the below section on how to read past progress](#ive-installed-and-run-tarkovmonitor-why-hasnt-it-marked-all-my-completed-quests-as-complete).

### Tarkov.dev Website Integration

The [Tarkov.dev website](https://tarkov.dev) has a "remote control" feature that allows the user to navigate to different pages in a browser window by using a different device. The original use case for this was to have the Tarkov.dev website open on a second monitor as you're playing the game and then using your cellphone as the remote control to load different pages on the website shown on the monitor without having to alt+tab out of the game.

TarkovMonitor can act as the "control" device, which allows it to do things like opening the corresponding map page on the website when you're loading into a raid and show your position (and rotation) on the map when you take a screenshot. To enable this integration, open the Tarkov.dev website, click the `Click to connect` button in the lower left, copy the `ID for remote control` shown in that box, and paste it in the Tarkov Monitor "Remote Browser IDs" settings field in the Tarkov.dev Website Remote section.

#### Multiple Browser Instances

TarkovMonitor supports controlling multiple Tarkov.dev browser instances simultaneously. This is useful if you have multiple monitors or windows open showing different maps or views. Simply enter multiple remote IDs separated by commas or semicolons in the "Remote Browser IDs" field (e.g., `abc123,def456`). When you interact with TarkovMonitor (e.g., take a screenshot to show your position), all configured instances receive the same commands.

#### Auto-Zoom on Location Detection

When you take a screenshot to show your player position, TarkovMonitor can automatically zoom into the map to make your position easier to find. This is configurable from 100% to 400% zoom. The default is 200%. Adjust the "Zoom level when location detected via screenshot" slider in the Tarkov.dev Website Remote settings to your preference.

**Note:** If you reload the Tarkov.dev site (including by restarting your browser), you'll need to click the `Click to connect` button again, but the remote code should remain the same.

## Building from Source

TarkovMonitor is built with **.NET 10** and requires Visual Studio 2022 or later, or the .NET 10 SDK installed.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Visual Studio 2022 with .NET desktop development workload, OR
- Any text editor + command line tools

### Quick Build

```bash
# Clone the repository
git clone https://github.com/the-hideout/TarkovMonitor.git
cd TarkovMonitor

# Restore dependencies and build
dotnet build

# Run the UI (service must be running separately for full functionality)
cd TarkovMonitor
dotnet run
```

### Building the MSI Installer

To build the Windows installer:

1. Install WiX Toolset globally:
   ```bash
   dotnet tool install --global wix --version "4.*"
   ```

2. Build the installer:
   ```bash
   .\Scripts\Build-Installer.ps1
   ```

The built MSI will be in the `dist/` directory as `TarkovMonitor-<version>.msi`.

### Architecture Overview

TarkovMonitor is a Windows desktop application built with a gRPC-based service/client architecture:

- **TarkovMonitor.Service** - Headless Windows service that monitors game log files and broadcasts events
- **TarkovMonitor** - WinForms UI that connects to the service as a gRPC client
- **TarkovMonitor.Core** - Shared library with game event types and log parsing logic
- **TarkovMonitor.Installer** - WiX 4 MSI installer project

The service runs in the background and persists even when the UI is closed. The UI can be launched and closed independently, and reconnects to the service automatically.

## FAQ

### How does TarkovMonitor work?

TarkovMonitor simply watches the log files that the game creates as it's running. Certain log messages correspond with particular events, so it's possible to automatically read some game events from these log files.

### I've installed and run TarkovMonitor, why hasn't it marked all my completed quests as complete?

TarkovMonitor only monitors new logs as they are being written while the app is running. Therefore, it doesn't automatically update quest progress that was made prior to the app running. It will, however, still mark quests as complete going forward while the app is running.

If you want to automatically update your progress from previous logs, open the Settings page, scroll down to the Initial Setup section, and click the Read Past Logs button. Tarkov Monitor will then present you with a list of breakpoints to choose the starting point to read logs from. The breakpoints are determined by the game's version number and your player profile id as written into each set of logs. Select the breakpoint corresponding with the start of the wipe for the correct account, click OK, and Tarkov Monitor will process all logs starting from that point through the present for the selected profile and update your quest progress accordingly.

### Is TarkovMonitor a cheat?

We don't have any official word from BSG, but it would be silly for TarkovMonitor to be considered a cheat. It doesn't do anything while players are in-raid because the logs aren't updated while a raid is in-progress. Moreover, the application is simply reading the logs that are written to your computer.

### Can TarkovMonitor update my hideout build progress on Tarkov Tracker?

Unfortunately, there are no log events for when you build hideout stations, so TarkovMonitor cannot automatically mark them as built.

### Does TarkovMonitor update my PMC level on Tarkov Tracker?

PMC level information is not logged by the game, so Tarkov Monitor cannot update it in Tarkov Tracker.

### What is the "Tarkov.dev Website Remote" option for?

The Tarkov.dev website has a feature that allows the user to "control" the website using another device. The typical use case is for someone to have the Tarkov.dev website loaded in a browser on their second monitor and then use their phone as the second device to load pages on the website without having to alt+tab out of the game. TarkovMonitor can act as the remote device and do things like load the Tarkov.dev map page for the map you're loading into a raid on. Linking the remote also enables showing your position on the Tarkov.dev map when you take a screenshot.

To get the remote code for Tarkov.dev, open the Tarkov.dev website in your browser, click the "Click to connect" box in the lower left, and then copy and paste that code into the "Remote Browser IDs" setting in Tarkov Monitor's Tarkov.dev Website Remote section.

TarkovMonitor also supports controlling multiple Tarkov.dev instances simultaneously—just enter multiple remote IDs separated by commas in the field. Additionally, when you take a screenshot to show your player position, TarkovMonitor can automatically zoom the map for you, making it easier to spot your position icon. Configure the zoom level (100-400%) in the same settings section.

### What is the "Submit Queue Time Data" option for?

When enabled, TarkovMonitor will submit the amount of time it takes to queue for a raid to Tarkov.dev. The information is sent anonymously and only the following pieces of information are sent and saved: the map, the time it took to find a raid, and whether the raid was for PMC or scav.

## Trouble Shooting

If the app won't launch and you see an "invalid WebView2 installation" error (sometimes accompanied by "The system cannot find the file specified"), you're likely hitting a known WebView2 issue.

Follow the solution here: [Error on launching program (invalid WebView2 installation) #22 — solution comment](https://github.com/the-hideout/TarkovMonitor/issues/22#issuecomment-3443766675)
