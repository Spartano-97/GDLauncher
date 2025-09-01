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

            _exeDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            _sourceConfigPath = Path.Combine(_exeDirectory ?? "", "Config.json");
            string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _launcherDataFolder = Path.Combine(appDataFolder, "GameDiskLauncher");
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                LauncherConfig? config = await LoadConfigAsync();
                if (config == null) return;

                SetVersionInfo(config);

                if (config.Buttons is not null)
                {
                    var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == AppConstants.StartButtonId);

                    if (startGameButton != null && !string.IsNullOrEmpty(startGameButton.Path))
                    {
                        if (File.Exists(startGameButton.Path))
                        {
                            config.Buttons.RemoveAll(b => b.Id == AppConstants.InstallButtonId);
                        }
                        else
                        {
                            await HandleMissingGameFile(config);
                            return; // Stop processing since the app will restart or close.
                        }
                    }
                    else
                    {
                        config.Buttons.RemoveAll(b => b.Id == AppConstants.StartButtonId);
                    }
                }

                this.DataContext = config;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"A critical error occurred on startup: {ex.Message}", "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private async Task HandleMissingGameFile(LauncherConfig config)
        {
            string message = "The game executable could not be found. It may have been uninstalled or moved.\n\n" +
                             "• Yes: Reset the launcher to re-install the game.\n" +
                             "• No: Manually locate the game's executable file.";

            MessageBoxResult result = MessageBox.Show(message, "Game Not Found", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{config.GameId}.json");
                if (File.Exists(localConfigPath))
                {
                    File.Delete(localConfigPath);
                }
                if (Environment.ProcessPath != null) Process.Start(Environment.ProcessPath);
                Application.Current.Shutdown();
            }
            else
            {
                await AskForPathOrShutdown(config);
            }
        }

        private async Task<LauncherConfig?> LoadConfigAsync()
        {
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

                if (string.IsNullOrEmpty(sourceConfig?.GameId))
                {
                    MessageBox.Show("GameId is missing from the source Config.json. Launcher cannot continue.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return null;
                }

                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{sourceConfig.GameId}.json");

                if (File.Exists(localConfigPath))
                {
                    string localJson = await File.ReadAllTextAsync(localConfigPath);
                    return JsonConvert.DeserializeObject<LauncherConfig>(localJson);
                }

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
                if (btnConfig.Id == AppConstants.InstallButtonId)
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
            if (this.DataContext is not LauncherConfig currentConfig) return;

            if (!string.IsNullOrEmpty(installButtonConfig.RegistryDisplayName) && IsGameInstalledViaRegistry(installButtonConfig.RegistryDisplayName))
            {
                MessageBox.Show("The application appears to be already installed. Please locate the main game executable.", "Game Already Installed", MessageBoxButton.OK, MessageBoxImage.Information);
                await AskForPathOrShutdown(currentConfig);
                return;
            }

            var installerDialog = new OpenFileDialog
            {
                Title = "Select Installer Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (installerDialog.ShowDialog() != true) return;

            try
            {
                Process? installerProcess = Process.Start(new ProcessStartInfo(installerDialog.FileName) { UseShellExecute = true });
                if (installerProcess != null)
                {
                    await installerProcess.WaitForExitAsync();
                    installerProcess.Dispose();
                }
                await AskForPathOrShutdown(currentConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during installation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> AskForGamePathAndUpdateConfig(LauncherConfig config)
        {
            var gameDialog = new OpenFileDialog
            {
                Title = "Select Game Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (gameDialog.ShowDialog() != true) return false;

            string gameExePath = gameDialog.FileName;

            if (config != null && !string.IsNullOrEmpty(config.GameId))
            {
                var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == AppConstants.StartButtonId);

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

                startGameButton.Path = gameExePath;
                config.Buttons.RemoveAll(b => b.Id == AppConstants.InstallButtonId);

                var serializerSettings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore,
                    Formatting = Formatting.Indented
                };

                string updatedJson = JsonConvert.SerializeObject(config, serializerSettings);
                string localConfigPath = Path.Combine(_launcherDataFolder, $"Config_{config.GameId}.json");
                Directory.CreateDirectory(_launcherDataFolder);
                await File.WriteAllTextAsync(localConfigPath, updatedJson);

                MessageBox.Show("Configuration saved successfully. The launcher will now restart to apply the changes.", "Restarting", MessageBoxButton.OK, MessageBoxImage.Information);
                if (Environment.ProcessPath != null)
                {
                    Process.Start(Environment.ProcessPath, AppConstants.RestartArgument);
                }
                Application.Current.Shutdown();
                return true;
            }
            return false;
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
                string path = btnConfig.Path;
                if (btnConfig.Type == "File" && !Path.IsPathRooted(path))
                {
                    path = Path.Combine(_exeDirectory ?? "", path);
                }

                if (btnConfig.Type == "File" && !File.Exists(path))
                {
                    MessageBox.Show($"File not found: {path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var startInfo = new ProcessStartInfo(path) { UseShellExecute = true };
                if (btnConfig.Type == "File")
                {
                    startInfo.WorkingDirectory = Path.GetDirectoryName(path);
                }

                Process.Start(startInfo);

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

        private static bool IsGameInstalledViaRegistry(string displayNameFragment)
        {
            string[] uninstallKeys =
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
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
                                if (appKey?.GetValue("DisplayName") is string displayName &&
                                    displayName.Replace(" ", "").IndexOf(cleanedFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private void SetVersionInfo(LauncherConfig config)
        {
            string? informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(informationalVersion))
            {
                config.Version = $"v{informationalVersion} - beta release";
            }
            else
            {
                var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version;
                if (assemblyVersion != null)
                {
                    config.Version = $"v{assemblyVersion.Major}.{assemblyVersion.Minor}.{assemblyVersion.Build} - beta release";
                }
            }
        }

        private async Task AskForPathOrShutdown(LauncherConfig config)
        {
            if (!await AskForGamePathAndUpdateConfig(config))
            {
                Application.Current.Shutdown();
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}