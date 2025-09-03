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
        // Read-only fields for core application paths.
        private readonly string _sourceConfigPath;      // Path to the default Config.json on the source media (e.g., CD).
        private readonly string _launcherDataFolder;    // Path to the launcher's writable data folder in AppData.
        private readonly string? _exeDirectory;         // The directory where the launcher executable is running.

        /// <summary>
        /// Initializes the main window and sets up the core application paths.
        /// This constructor runs once when the application starts.
        /// </summary>
        public MainWindow()
        {
            // This is a standard WPF method that loads the XAML file and creates the visual UI elements.
            InitializeComponent();

            // Get the directory where the launcher's .exe is currently running.
            // This is crucial for finding relative files that are shipped with the launcher.
            _exeDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);

            // Define the full path to the default "Config.json" file, expected to be next to the executable.
            _sourceConfigPath = Path.Combine(_exeDirectory ?? "", "Config.json");

            // Define the full path to the application's data folder in the user's AppData\Roaming directory.
            // This is the standard, writable location for storing user-specific configuration files.
            _launcherDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameDiskLauncher");
        }

        /// <summary>
        /// Handles the main application logic as soon as the window is loaded.
        /// This method orchestrates loading the configuration, determining the application's state
        /// (e.g., first run, installed, or broken installation), and preparing the UI.
        /// </summary>
        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Wrap the entire startup sequence in a try-catch block to handle any unexpected errors gracefully.
            try
            {
                // Asynchronously load the configuration. This will prioritize a local user config
                // over the default one shipped with the application.
                LauncherConfig? config = await LoadConfigAsync();

                // If loading fails, LoadConfigAsync handles showing an error and shutting down.
                // This return statement is a crucial guard clause to stop further execution.
                if (config == null) return;

                // Populate the version information into the config object for display in the UI.
                SetVersionInfo(config);

                // Proceed only if the configuration contains a list of buttons.
                if (config.Buttons is not null)
                {
                    // Attempt to find the primary "Start Game" button.
                    var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == AppConstants.StartButtonId);

                    // This condition checks if the game has been configured before (i.e., a path has been saved).
                    if (startGameButton != null && !string.IsNullOrEmpty(startGameButton.Path))
                    {
                        // If the configured executable exists, the game is installed and ready.
                        if (File.Exists(startGameButton.Path))
                        {
                            // Happy path: Remove the "Install" button as it's no longer needed.
                            config.Buttons.RemoveAll(b => b.Id == AppConstants.InstallButtonId);
                        }
                        else
                        {
                            // The path was saved, but the file is missing. The game was likely moved or uninstalled.
                            // Delegate to the specific handler for this scenario.
                            await HandleMissingGameFile(config);
                            return; // Stop processing, as the handler will restart or close the application.
                        }
                    }
                    else
                    {
                        // This is the "first run" scenario where no path has been saved yet.
                        // Remove the non-functional "Start" button to show the "Install" button as the primary action.
                        config.Buttons.RemoveAll(b => b.Id == AppConstants.StartButtonId);
                    }
                }

                // Set the window's DataContext to the prepared config object. This crucial step
                // populates the entire UI (titles, buttons, etc.) via data binding.
                this.DataContext = config;
            }
            catch (Exception ex)
            {
                // If any unhandled exception occurs during startup, show an error and shut down.
                MessageBox.Show($"A critical error occurred on startup: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        /// <summary>
        /// Handles the scenario where the configured game executable is missing.
        /// It prompts the user with a choice to either reset the launcher (by deleting the local config)
        /// or to manually locate the game's executable file again.
        /// </summary>
        /// <param name="config">The current, invalid configuration object.</param>
        private async Task HandleMissingGameFile(LauncherConfig config)
        {
            // Create a clear, user-friendly message explaining the problem and the available options.
            string message = "The game executable could not be found. It may have been uninstalled or moved.\n\n" +
                             "• Yes: Reset the launcher to re-install the game.\n" +
                             "• No: Manually locate the game's executable file.";

            // Display the message in a dialog with "Yes" and "No" buttons.
            MessageBoxResult result = MessageBox.Show(message, "Game Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            // Handle the user's choice.
            if (result == MessageBoxResult.Yes)
            {
                // User chose to reset. Delete the local config file to revert to the "first run" state.
                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{config.GameId}.json");
                if (File.Exists(localConfigPath))
                {
                    File.Delete(localConfigPath);
                }
                // Restart the application to force it to read the original CD config.
                if (Environment.ProcessPath != null) Process.Start(Environment.ProcessPath);
                Application.Current.Shutdown();
            }
            else
            {
                // User chose to find the file manually. Delegate to the helper method that handles this workflow.
                await AskForPathOrShutdown(config);
            }
        }

        /// <summary>
        /// Asynchronously loads the launcher configuration. It prioritizes loading the user-specific
        /// configuration from AppData. If not found, it falls back to the source Config.json
        /// located next to the executable.
        /// </summary>
        /// <returns>A populated LauncherConfig object, or null if a critical error occurs.</returns>
        private async Task<LauncherConfig?> LoadConfigAsync()
        {
            // Guard clause: The source config on the CD is mandatory.
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

                // Guard clause: The GameId is essential for naming the local config file.
                if (string.IsNullOrEmpty(sourceConfig?.GameId))
                {
                    MessageBox.Show("GameId is missing from the source Config.json. Launcher cannot continue.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return null;
                }

                // Construct the path to the user's local config file.
                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{sourceConfig.GameId}.json");

                // If a local config exists, it takes precedence as it contains the user's saved path.
                if (File.Exists(localConfigPath))
                {
                    string localJson = await File.ReadAllTextAsync(localConfigPath);
                    return JsonConvert.DeserializeObject<LauncherConfig>(localJson);
                }

                // Otherwise, this is a first run, so return the default config from the source.
                return sourceConfig;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading or parsing Config.json: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return null;
            }
        }

        /// <summary>
        /// The primary event handler for all dynamically generated buttons.
        /// It acts as a router, delegating the click event to the appropriate handler
        /// based on the button's configured ID.
        /// </summary>
        private async void LaunchButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is ButtonConfig btnConfig)
            {
                // Route to the installation handler if the "Install" button is clicked.
                if (btnConfig.Id == AppConstants.InstallButtonId)
                {
                    await HandleInstallation(btnConfig);
                }
                else
                {
                    // Handle all other button clicks (Start, Docs, Website, etc.).
                    await HandleButtonClick(btnConfig);
                }
            }
        }

        /// <summary>
        /// Manages the game installation process. It checks if the game is already installed
        /// via the registry. If not, it prompts the user for the installer, runs it, and then
        /// prompts for the game's executable path.
        /// </summary>
        /// <param name="installButtonConfig">The configuration for the "Install" button.</param>
        private async Task HandleInstallation(ButtonConfig installButtonConfig)
        {
            if (this.DataContext is not LauncherConfig currentConfig) return;

            // First, check the registry to see if the game is already present.
            if (!string.IsNullOrEmpty(installButtonConfig.RegistryDisplayName) && IsGameInstalledViaRegistry(installButtonConfig.RegistryDisplayName))
            {
                MessageBox.Show("The application appears to be already installed. Please locate the main game executable.", "Game Already Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                await AskForPathOrShutdown(currentConfig);
                return;
            }

            // If not installed, prompt the user to select the installer file.
            var installerDialog = new OpenFileDialog
            {
                Title = "Select Installer Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (installerDialog.ShowDialog() != true) return; // User cancelled.

            try
            {
                // Run the selected installer and wait for it to complete.
                Process? installerProcess = Process.Start(new ProcessStartInfo(installerDialog.FileName) { UseShellExecute = true });
                if (installerProcess != null)
                {
                    await installerProcess.WaitForExitAsync();
                    installerProcess.Dispose();
                }
                // After installation, ask the user to locate the game's main executable.
                await AskForPathOrShutdown(currentConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during installation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Prompts the user to select the game's executable file, updates the configuration,
        /// saves it to a local file in AppData, and restarts the application.
        /// </summary>
        /// <param name="config">The configuration object to update.</param>
        /// <returns>True if the user selected a file and the config was saved; otherwise, false.</returns>
        private async Task<bool> AskForGamePathAndUpdateConfig(LauncherConfig config)
        {
            var gameDialog = new OpenFileDialog
            {
                Title = "Select Game Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (gameDialog.ShowDialog() != true) return false; // User cancelled.

            string gameExePath = gameDialog.FileName;

            if (config != null && !string.IsNullOrEmpty(config.GameId))
            {
                var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == AppConstants.StartButtonId);

                // If the "Start" button doesn't exist (e.g., first run), create it.
                if (startGameButton == null)
                {
                    startGameButton = new ButtonConfig
                    {
                        Id = AppConstants.StartButtonId,
                        Text = "Start Game",
                        Type = "File",
                        StyleType = AppConstants.PrimaryStyle
                    };
                    config.Buttons.Insert(0, startGameButton);
                }

                // Update the button's path and remove the now-redundant "Install" button.
                startGameButton.Path = gameExePath;
                config.Buttons.RemoveAll(b => b.Id == AppConstants.InstallButtonId);

                // Configure the JSON serializer for clean, human-readable output.
                var serializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.Indented
                };

                // Save the updated configuration to the user's AppData folder.
                string updatedJson = JsonConvert.SerializeObject(config, serializerSettings);
                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{config.GameId}.json");
                Directory.CreateDirectory(_launcherDataFolder); // Ensure the directory exists.
                await File.WriteAllTextAsync(localConfigPath, updatedJson);

                // Inform the user and restart the application to apply changes.
                MessageBox.Show("Configuration saved successfully. The launcher will now restart to apply the changes.", "Restarting", MessageBoxButton.OK, MessageBoxImage.Information);
                if (Environment.ProcessPath != null)
                {
                    Process.Start(Environment.ProcessPath, AppConstants.RestartArgument);
                }
                Application.Current.Shutdown();
                return true; // Signal success.
            }
            return false; // Signal failure.
        }

        /// <summary>
        /// Handles the click event for any non-install button (e.g., Start, Docs, Website).
        /// It resolves the path and launches the appropriate file or URL.
        /// </summary>
        /// <param name="btnConfig">The configuration of the clicked button.</param>
        private async Task HandleButtonClick(ButtonConfig btnConfig)
        {
            if (string.IsNullOrEmpty(btnConfig.Path))
            {
                MessageBox.Show("Button has no path configured.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string path = btnConfig.Path;
                // If the path is relative (for a file), combine it with the launcher's directory.
                if (btnConfig.Type == "File" && !Path.IsPathRooted(path))
                {
                    path = Path.Combine(_exeDirectory ?? "", path);
                }

                // Ensure the file exists before trying to launch it.
                if (btnConfig.Type == "File" && !File.Exists(path))
                {
                    MessageBox.Show($"File not found: {path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
                // Set the working directory for file-based launches to prevent issues with relative assets.
                if (btnConfig.Type == "File")
                {
                    startInfo.WorkingDirectory = Path.GetDirectoryName(path);
                }

                Process.Start(startInfo);

                // If the primary "Start Game" button was clicked, shut down the launcher after a short delay.
                if (btnConfig.StyleType == AppConstants.PrimaryStyle && btnConfig.Id == AppConstants.StartButtonId)
                {
                    await Task.Delay(1500);
                    Application.Current.Shutdown();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open {btnConfig.Text ?? "the item"}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Checks the Windows Registry to determine if a game is installed.
        /// It searches both 32-bit and 64-bit uninstall locations for a matching display name.
        /// </summary>
        /// <param name="displayNameFragment">A fragment of the game's name as it appears in "Programs and Features".</param>
        /// <returns>True if a matching registry key is found; otherwise, false.</returns>
        private static bool IsGameInstalledViaRegistry(string displayNameFragment)
        {
            // Standard 32-bit and 64-bit registry paths for installed applications.
            string[] uninstallKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            // Clean the search term for a more robust, case-insensitive comparison.
            string cleanedFragment = displayNameFragment.Replace(" ", "");

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
                                // Perform a case-insensitive "contains" check on the cleaned display name.
                                if (appKey?.GetValue("DisplayName") is string displayName &&
                                    displayName.Replace(" ", "").IndexOf(cleanedFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return true; // Found a match.
                                }
                            }
                        }
                    }
                }
            }
            return false; // No match found.
        }

        /// <summary>
        /// Populates the version string in the configuration object for display in the UI.
        /// It prioritizes the detailed informational version (from Git) and falls back to the standard assembly version.
        /// </summary>
        /// <param name="config">The configuration object to update.</param>
        private void SetVersionInfo(LauncherConfig config)
        {
            // Prefer the more detailed version string from Nerdbank.GitVersioning.
            string? informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informationalVersion))
            {
                config.Version = $"v{informationalVersion} - beta release";
            }
            else
            {
                // Fallback to the standard assembly version if the detailed one isn't available.
                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (assemblyVersion != null)
                {
                    config.Version = $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build} - beta release";
                }
            }
        }

        /// <summary>
        /// A helper method that wraps the call to ask for a game path.
        /// If the user cancels the operation, it shuts down the application to prevent a broken state.
        /// </summary>
        /// <param name="config">The configuration object to pass to the underlying method.</param>
        private async Task AskForPathOrShutdown(LauncherConfig config)
        {
            // If the user cancels selecting a path, shut down the application.
            if (!await AskForGamePathAndUpdateConfig(config))
            {
                Application.Current.Shutdown();
            }
        }

        /// <summary>
        /// Handles the click event for the main "Exit" button.
        /// </summary>
        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}