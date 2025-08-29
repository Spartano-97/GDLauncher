# G.D.L. - Game Disk Launcher

A simple, modern, and customizable WPF application designed to act as a launcher for games, applications, and content distributed on physical media like CDs or DVDs.

---

## Features

- **Fully Configurable**: The entire launcher UI, including the title, subtitle, and buttons, is driven by a simple `Config.json` file. No need to recompile to change the content.
- **Custom Styling**: Supports different button styles (e.g., "Primary" and "Default") defined in the JSON configuration.
- **Flexible Actions**: Launch local executable files (`.exe`), documents (`.pdf`), installers, or open websites directly from the launcher buttons.
- **Single Instance**: Automatically prevents multiple instances of the launcher from running at the same time. If a user tries to open it again, the existing window is brought to the front.
- **Auto-Exit**: The launcher can be configured to automatically close after successfully launching the main game or application.
- **Custom Branding**: Easily change the background image and logo by replacing the files in the `Assets` folder.
- **Self-Contained**: Can be published as a single, self-contained `.exe` file that runs on Windows without requiring the user to have .NET installed.

---

## How to Use

This launcher is designed to be placed in the root directory of your disk/folder alongside the content you want to launch.

### Directory Structure

For the launcher to work correctly, your final distribution folder should look like this:

```
/ (Your CD/DVD Root)
├── GameDiskLauncher.exe      <-- The launcher application
├── Config.json               <-- The configuration file
├── MyApp.exe                 <-- Your main game/application
├── Manual.pdf                <-- Other content to launch

```

### Configuration (`Config.json`)

This file controls everything the user sees. Edit it with any text editor to customize the launcher.

```json
{
  "Title": "- Welcome to G.D.L. -",
  "SubTitle": "Your Game Name Here",
  "Buttons": [
    {
      "Text": "Start Game",
      "Type": "File",
      "Path": "MyApp.exe",
      "StyleType": "Primary"
    },
    {
      "Text": "Install Bonus Content",
      "Type": "File",
      "Path": "Setup.exe",
      "StyleType": "Default"
    },
    {
      "Text": "Open Manual",
      "Type": "File",
      "Path": "Manual.pdf",
      "StyleType": "Default"
    },
    {
      "Text": "Visit Website",
      "Type": "Website",
      "Path": "https://github.com/Spartano-97",
      "StyleType": "Default"
    }
  ]
}
```

- **`Title` / `SubTitle`**: The main text displayed at the top of the launcher.
- **`Buttons`**: A list of buttons to display.
- **`Text`**: The text on the button.
- **`Type`**: Can be `"File"` (for local files) or `"Website"` (for URLs).
- **`Path`**: The relative path to the file or the full URL for a website.
- **`StyleType`**: Can be `"Primary"` for the main action button or `"Default"` for others. The launcher will automatically close after a button with `"Primary"` style is clicked.

---

## Building from Source

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (or the version specified in `GameDiskLauncher.csproj`)
- [Visual Studio Code](https://code.visualstudio.com/)

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
The final `GameDiskLauncher.exe` will be located in the `GameDiskLauncher/bin/Release/net9.0-windows/win-x64/publish/` directory.

---

## Technologies Used

- **.NET 9**
- **WPF (Windows Presentation Foundation)**
- **Newtonsoft.Json** for configuration parsing

---

## License
