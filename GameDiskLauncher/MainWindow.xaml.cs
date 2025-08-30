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
        private readonly string _configPath;

        public MainWindow()
        {
            // The constructor should only do synchronous work.
            InitializeComponent();
            _configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config.json");
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
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
                    // 1. Check if the game is considered installed by looking at the Start button's path.
                    var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == "Start");
                    bool isGameInstalled = startGameButton != null && !string.IsNullOrEmpty(startGameButton.Path);

                    // 2. Filter the buttons list based on the installation status.
                    if (isGameInstalled)
                    {
                        // If installed, show the "Start" button and remove the "Install" button.
                        config.Buttons.RemoveAll(b => b.Id == "Install");
                    }
                    else
                    {
                        // If not installed, show the "Install" button and remove the "Start" button.
                        config.Buttons.RemoveAll(b => b.Id == "Start");
                    }              
                }

                this.DataContext = config;
            }   
        }

        private async Task<LauncherConfig?> LoadConfigAsync()
        {
            if (!File.Exists(_configPath))
            {
                MessageBox.Show("Config.json not found. Launcher will exit.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return null;
            }

            try
            {
                string json = await File.ReadAllTextAsync(_configPath);
                return JsonConvert.DeserializeObject<LauncherConfig>(json);
            }
            catch (JsonException ex)
            {
                MessageBox.Show($"Error reading Config.json: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            // 1. Check registry if a display name is specified in the config.
            if (!string.IsNullOrEmpty(installButtonConfig.RegistryDisplayName) && IsGameInstalledViaRegistry(installButtonConfig.RegistryDisplayName))
            {
                // If found, the app is already installed.
                MessageBox.Show("The application appears to be already installed. Please locate the main game executable.", "Game Already Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                await AskForGamePathAndUpdateConfig();
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
                await AskForGamePathAndUpdateConfig();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during installation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task AskForGamePathAndUpdateConfig()
        {
            var gameDialog = new OpenFileDialog
            {
                Title = "Select Game Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (gameDialog.ShowDialog() == true)
            {
                string gameExePath = gameDialog.FileName;

                // Update the configuration file with the new path.
                LauncherConfig? config = await LoadConfigAsync();
                if (config != null)
                {
                    var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == "Start");
                    if (startGameButton != null)
                    {
                        startGameButton.Path = gameExePath;
                        string updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                        await File.WriteAllTextAsync(_configPath, updatedJson);

                        // Restart the launcher to reflect the changes.
                        if (Environment.ProcessPath != null)
                        {
                            Process.Start(Environment.ProcessPath, "--restarting");
                        }
                        Application.Current.Shutdown();
                    }
                }
            }
        }

        /// <summary> Scans the standard Uninstall registry locations for a program by its display name. </summary>
        /// <param name="displayNameFragment">The name of the application to search for (e.g., "My Awesome Game").</param>
        /// <returns>True if a matching application is found, otherwise false.</returns>
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

        private static async Task HandleButtonClick(ButtonConfig btnConfig)
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
                        path = Path.Combine(Directory.GetCurrentDirectory(), path);
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