using System.Text;
using System.Windows;
using System.Diagnostics;
using Newtonsoft.Json;
using System.IO;
using System.Windows.Controls;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Reflection;

namespace GameDiskLauncher
{
    public partial class MainWindow : Window
    {
        private readonly string _sourceConfigPath; // The original config on the CD
        private readonly string _launcherDataFolder; // The base AppData folder for the launcher
        private readonly string? _exeDirectory;

        public MainWindow()
        {
            InitializeComponent();

            // Path to the original config next to the .exe (on the CD)
            _exeDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            _sourceConfigPath = Path.Combine(_exeDirectory ?? "", "Config.json");

            // Path to the user-specific, writable config folder in AppData
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _launcherDataFolder = Path.Combine(appDataFolder, "GameDiskLauncher"); // Create a dedicated folder
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // All async initialization happens here.
                LauncherConfig? config = await LoadConfigAsync();

                if (config != null)
                {
                    // Get the full, detailed version string provided by Nerdbank.GitVersioning.
                    string? informationalVersion = Assembly.GetExecutingAssembly()
                        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                        .InformationalVersion;

                    if (!string.IsNullOrEmpty(informationalVersion))
                    {
                        // The string might look like "1.0.15-beta+a1b2c3d". We can format it for display.
                        config.Version = $"v{informationalVersion} - beta release";
                    }
                    else
                    {
                        // Fallback to the simpler version if the detailed one isn't found.
                        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                        if (assemblyVersion != null)
                        {
                            config.Version = $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build} - beta release";
                        }
                    }

                    if (config?.Buttons is not null)
                    {
                        var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == "Start");

                        // Verify that the game is ACTUALLY installed by checking if the executable exists.
                        bool isGameInstalled = startGameButton != null
                                            && !string.IsNullOrEmpty(startGameButton.Path)
                                            && File.Exists(startGameButton.Path);

                        if (isGameInstalled)
                        {
                            // If installed and file exists, show the "Start" button and remove the "Install" button.
                            config.Buttons.RemoveAll(b => b.Id == "Install");
                        }
                        else
                        {
                            // If not installed OR the file is missing, force the installation flow.
                            // If the start button exists but its path is invalid, clear the path.
                            if (startGameButton != null)
                            {
                                startGameButton.Path = null;
                            }
                            config.Buttons.RemoveAll(b => b.Id == "Start");
                        }
                    }

                    this.DataContext = config;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A critical error occurred on startup: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private async Task<LauncherConfig?> LoadConfigAsync()
        {
            // 1. First, always try to load the config from the source (the CD).
            if (!File.Exists(_sourceConfigPath))
            {
                MessageBox.Show("Source Config.json not found next to the executable. Launcher will exit.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return null;
            }

            try
            {
                string sourceJson = await File.ReadAllTextAsync(_sourceConfigPath);
                LauncherConfig? sourceConfig = JsonConvert.DeserializeObject<LauncherConfig>(sourceJson);

                // 2. The GameId is mandatory. Without it, we don't know which local config to use.
                if (string.IsNullOrEmpty(sourceConfig?.GameId))
                {
                    MessageBox.Show("GameId is missing from the source Config.json. Launcher cannot continue.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return null;
                }

                // 3. Construct the path to the specific local config file for this game.
                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{sourceConfig.GameId}.json");

                // 4. If a local config for this game exists, load it instead. It has the user's saved path.
                if (File.Exists(localConfigPath))
                {
                    string localJson = await File.ReadAllTextAsync(localConfigPath);
                    return JsonConvert.DeserializeObject<LauncherConfig>(localJson);
                }

                // 5. If no local config exists, this is the first run. Return the config from the CD.
                return sourceConfig;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading or parsing Config.json: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return null;
            }
        }

        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ButtonConfig btnConfig)
            {
                // Route the click based on the button's ID.
                if (btnConfig.Id == "Install")
                {
                    await HandleInstallation(btnConfig);
                }
                else
                {
                    await HandleButtonClick(btnConfig);
                }
            }
        }

        private async Task HandleInstallation(ButtonConfig installButtonConfig)
        {
            // Get the currently loaded config from the DataContext.
            if (this.DataContext is not LauncherConfig currentConfig) return;

            // 1. Check registry if a display name is specified in the config.
            if (!string.IsNullOrEmpty(installButtonConfig.RegistryDisplayName) && IsGameInstalledViaRegistry(installButtonConfig.RegistryDisplayName))
            {
                // If found, the app is already installed.
                MessageBox.Show("The application appears to be already installed. Please locate the main game executable.", "Game Already Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                await AskForGamePathAndUpdateConfig(currentConfig); // Pass the existing config
                return;
            }

            // 2. If key not found, proceed with normal installation.
            var installerDialog = new OpenFileDialog
            {
                Title = "Select Installer Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (installerDialog.ShowDialog() != true)
            {
                return; // User cancelled selecting the installer.
            }

            string installerPath = installerDialog.FileName;

            try
            {
                // 3. Run the installer and wait for it to complete.
                Process? installerProcess = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });
                if (installerProcess != null)
                {
                    await installerProcess.WaitForExitAsync();
                    installerProcess.Dispose();
                }

                // After installation, ask for the game path.
                await AskForGamePathAndUpdateConfig(currentConfig); // Pass the existing config
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during installation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task AskForGamePathAndUpdateConfig(LauncherConfig config)
        {
            var gameDialog = new OpenFileDialog
            {
                Title = "Select Game Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (gameDialog.ShowDialog() == true)
            {
                string gameExePath = gameDialog.FileName;

                // The config object is already loaded and passed in.
                if (config != null && !string.IsNullOrEmpty(config.GameId))
                {
                    var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == "Start");

                    // If the "Start" button was removed or never existed, create it now.
                    if (startGameButton == null)
                    {
                        // Create a new button object.
                        startGameButton = new ButtonConfig
                        {
                            Id = "Start",
                            Text = "Start Game",
                            Type = "File",
                            StyleType = "Primary"
                        };
                        // Use Insert(0, ...) to add the button to the beginning of the list.
                        config.Buttons.Insert(0, startGameButton);
                    }

                    // Update the path to the game executable.
                    startGameButton.Path = gameExePath;

                    // Before saving, remove the now-redundant "Install" button.
                    config.Buttons.RemoveAll(b => b.Id == "Install");

                    string updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);

                    string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{config.GameId}.json");
                    Directory.CreateDirectory(_launcherDataFolder);
                    await File.WriteAllTextAsync(localConfigPath, updatedJson);
                    MessageBox.Show("Configuration saved successfully. The launcher will now restart to apply the changes.", "Restarting", MessageBoxButton.OK, MessageBoxImage.Information);
                    if (Environment.ProcessPath != null)
                    {
                        Process.Start(Environment.ProcessPath, "--restarting");
                    }
                    Application.Current.Shutdown();
                }
            }
        }

        private static bool IsGameInstalledViaRegistry(string displayNameFragment)
        {
            // The two standard 32-bit and 64-bit uninstall registry paths
            string[] uninstallKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            // Prepare a "cleaned" version of the search term by removing spaces.
            string cleanedDisplayNameFragment = displayNameFragment.Replace(" ", "");

            // Search in HKEY_LOCAL_MACHINE for a system-wide installation
            using (RegistryKey hklm = Registry.LocalMachine)
            {
                foreach (string keyPath in uninstallKeys)
                {
                    using (RegistryKey? uninstallKey = hklm.OpenSubKey(keyPath))
                    {
                        if (uninstallKey == null) continue;

                        foreach (string subKeyName in uninstallKey.GetSubKeyNames())
                        {
                            using (RegistryKey? appKey = uninstallKey.OpenSubKey(subKeyName))
                            {
                                if (appKey != null)
                                {
                                    object? displayNameValue = appKey.GetValue("DisplayName");
                                    if (displayNameValue is string displayName)
                                    {
                                        // Clean the registry display name by removing spaces.
                                        string cleanedDisplayName = displayName.Replace(" ", "");

                                        // Perform a case-insensitive "contains" check on the cleaned strings.
                                        if (cleanedDisplayName.IndexOf(cleanedDisplayNameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                                        {
                                            return true; // Found a match!
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return false; // No match found after checking all paths
        }

        private async Task HandleButtonClick(ButtonConfig btnConfig)
        {
            if (string.IsNullOrEmpty(btnConfig.Path))
            {
                MessageBox.Show("Button has no path configured.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ProcessStartInfo startInfo;
                if (btnConfig.Type == "File")
                {
                    string path = btnConfig.Path;
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.Combine(_exeDirectory ?? "", path);
                    }

                    if (!File.Exists(path))
                    {
                        MessageBox.Show($"File not found: {path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    startInfo = new ProcessStartInfo(path)
                    {
                        // Set the working directory to the folder containing the executable.
                        WorkingDirectory = Path.GetDirectoryName(path)
                    };
                }
                else // Assumes "Website" or other URL-based types
                {
                    startInfo = new ProcessStartInfo(btnConfig.Path);
                }

                startInfo.UseShellExecute = true;
                Process.Start(startInfo);

                // Only shut down if the button is the primary "Start Game" button.
                if (btnConfig.StyleType == "Primary" && btnConfig.Id == "Start")
                {
                    await Task.Delay(1500);
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                string buttonIdentifier = btnConfig.Text ?? "the item";
                MessageBox.Show($"Failed to open {buttonIdentifier}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}