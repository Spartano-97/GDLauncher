# G.D.L. - Game Disk Launcher

G.D.L. is a smart, configurable WPF application designed to provide a professional front-end for games and applications distributed on physical media (like CDs/DVDs) or as self-contained folders. It manages the entire lifecycle, from initial installation to subsequent launches, providing a seamless "install once, run forever" user experience.

---

## Core Features

-   **Smart Installation Workflow**: Guides the user through a one-time setup process, including running an installer and locating the game's executable.
-   **Persistent User Configuration**: After the initial setup, the launcher saves the game's path to the user's `AppData` folder. It never needs to be configured again, even if the disk is removed.
-   **Automatic Installation Detection**: Can check the Windows Registry to see if a game is already installed, intelligently guiding the user to locate the executable instead of re-installing.
-   **Robust Error Recovery**: If the game's executable is moved or deleted after setup, the launcher gracefully prompts the user to either re-locate the file or reset the configuration.
-   **Customizable UI via JSON**: The entire UI—titles, subtitles, and all buttons—is driven by a single `Config.json` file. No recompiling is needed to adapt the launcher for a new game.
-   **Dependency & Content Management**: Easily add buttons to run dependency installers (e.g., DirectX, VCRedist), open PDF manuals, or link to websites.

---

## How It Works

G.D.L. operates in two main states:

### 1. The First Run Experience (Installation)

When a user runs the launcher for the first time, it reads the default `Config.json` from the disk.
1.  The UI displays an **"Install"** button as the primary action.
2.  Clicking "Install" prompts the user to select the game's installer (`setup.exe`).
3.  The launcher runs the installer and waits for it to finish.
4.  After installation, it prompts the user to select the main game executable (e.g., `Game.exe`).
5.  This path is saved to a new configuration file in the user's `AppData` folder, and the launcher restarts itself.

### 2. Subsequent Launches

On every subsequent run, the launcher detects the saved configuration in `AppData`.
1.  The UI now displays a **"Start Game"** button as the primary action.
2.  Clicking "Start Game" launches the game executable directly.
3.  The launcher automatically closes itself a moment later.

---

## Example Distribution Structure

For the launcher to work correctly, your final distribution folder should be structured similarly to this:

```
/ (Your CD/DVD Root or App Root)
├── GameDiskLauncher.exe          <-- The launcher application
├── Config.json                   <-- The master configuration file
├── GameInstaller.exe             <-- The game's main installer
├── Manual.pdf                    <-- Other content to launch
└── _CommonRedist/                <-- Folder for dependencies
    ├── install_dependencies.bat
    ├── dxwebsetup.exe
    └── vcredist_x86.exe
```

---

## Advanced Configuration (`Config.json`)

This file is the brain of the launcher. The initial `Config.json` on your disk should be set up for the "first run" experience.

```json
{
  "GameId": "MyAwesomeGame_v1",
  "Title": "- Welcome to G.D.L. -",
  "SubTitle": "My Awesome Game",
  "Buttons": [
    {
      "Id": "Start",
      "Text": "Start Game",
      "Type": "File",
      "Path": "",
      "StyleType": "Primary"
    },
    {
      "Id": "Install",
      "Text": "Install Game",
      "Type": "File",
      "Path": "",
      "StyleType": "Primary",
      "RegistryDisplayName": "My Awesome Game"
    },
    {
      "Id": "Dep",
      "Text": "Install Dependencies",
      "Type": "File",
      "Path": "_CommonRedist\\install_dependencies.bat",
      "StyleType": "Default"
    },
    {
      "Id": "Docs",
      "Text": "View Manual",
      "Type": "File",
      "Path": "Manual.pdf",
      "StyleType": "Default"
    }
  ]
}
```

### Key Properties Explained:

-   **`GameId`**: **Crucial.** A unique identifier for your game. This is used to create the local config file (e.g., `Config_MyAwesomeGame_v1.json`), allowing multiple games using this launcher to coexist on one PC.
-   **`Title` / `SubTitle`**: The main text displayed at the top of the launcher.
-   **`Buttons`**: A list of button objects.
    -   **`Id`**: A unique ID for the button. `"Start"` and `"Install"` have special logic.
    -   **`Text`**: The text displayed on the button.
    -   **`Type`**: `"File"` for local executables/documents or `"Website"` for URLs.
    -   **`Path`**: The relative path to the file or the full URL. **Leave this empty for the initial "Start" and "Install" buttons.**
    -   **`StyleType`**: `"Primary"` for the main action button or `"Default"` for others. The launcher closes after clicking a `"Primary"` button.
    -   **`RegistryDisplayName`**: (For the "Install" button only) A fragment of the name that appears in the Windows "Programs and Features" list. This is used to check if the game is already installed.

---

## Building from Source

### Prerequisites

-   [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or the version specified in `GameDiskLauncher.csproj`)
-   An IDE like Visual Studio or VS Code.

### Commands

Open a terminal in the project's root directory and run the following commands:

**To build the project (for debugging):**
```shell
dotnet build
```

**To publish a single-file executable for distribution:**
```shell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true
```
The final `GameDiskLauncher.exe` will be located in the `bin/Release/net9.0-windows/win-x64/publish/` directory.

---

## Technologies Used

-   **.NET 9**
-   **WPF (Windows Presentation Foundation)**
-   **Newtonsoft.Json** for configuration parsing
-   **Nerdbank.GitVersioning** for automatic versioning