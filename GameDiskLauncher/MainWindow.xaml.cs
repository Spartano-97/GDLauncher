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

namespace GameDiskLauncher
{
    public partial class MainWindow : Window
    {
        private readonly string _configPath;

        public MainWindow()
        {
            InitializeComponent();
            _configPath = Path.Combine(Directory.GetCurrentDirectory(), "Config.json");
            LauncherConfig? config = LoadConfig();

            if (config != null)
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

                this.DataContext = config;
            }
        }

        private LauncherConfig? LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                MessageBox.Show("Config.json not found. Launcher will exit.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return null;
            }

            try
            {
                string json = File.ReadAllText(_configPath);
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
                    await HandleInstallation();
                }
                else
                {
                    await HandleButtonClick(btnConfig);
                }
            }
        }

        private async Task HandleInstallation()
        {
            // 2. Ask the user to locate the installer executable.
            var installerDialog = new OpenFileDialog
            {
                Title = "Select Installer Executable",
                Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            };

            if (installerDialog.ShowDialog() != true)
            {
                return; // User cancelled.
            }

            string installerPath = installerDialog.FileName;

            try
            {
                // 3. Run the installer and wait for it to complete.
                // Start the process and assign it to a nullable variable.
                Process? installerProcess = Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true });

                // Only proceed if a new process was actually started.
                if (installerProcess != null)
                {
                    // Now it's safe to wait for the process to exit.
                    await installerProcess.WaitForExitAsync();
                    // Manually dispose of the process object after we're done with it.
                    installerProcess.Dispose();
                }

                // 3. After installation, ask the user for the installed game's executable path.
                MessageBox.Show("Installation complete! Please locate the main game executable.", "Installation", MessageBoxButton.OK, MessageBoxImage.Information);
                var gameDialog = new OpenFileDialog
                {
                    Title = "Select Game Executable",
                    Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
                };

                if (gameDialog.ShowDialog() == true)
                {
                    string gameExePath = gameDialog.FileName;

                    // Update the configuration file with the new path.
                    LauncherConfig? config = LoadConfig();
                    if (config != null)
                    {
                        var startGameButton = config.Buttons.FirstOrDefault(b => b.Id == "Start");
                        if (startGameButton != null)
                        {
                            startGameButton.Path = gameExePath;
                            string updatedJson = JsonConvert.SerializeObject(config, Formatting.Indented);
                            File.WriteAllText(_configPath, updatedJson);

                            // 3. Restart the launcher with a special argument.
                            if (Environment.ProcessPath != null)
                            {
                                // Pass the argument directly to Process.Start
                                Process.Start(Environment.ProcessPath, "--restarting");
                            }
                            Application.Current.Shutdown();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during installation: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
                if (btnConfig.Type == "File")
                {
                    string path = btnConfig.Path;
                    // If the path is not absolute, it's relative to the launcher's directory.
                    if (!Path.IsPathRooted(path))
                    {
                        path = Path.Combine(Directory.GetCurrentDirectory(), path);
                    }

                    if (File.Exists(path))
                    {
                        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                        // Only shut down if the button is the primary "Start Game" button.
                        if (btnConfig.StyleType == "Primary")
                        {
                            await Task.Delay(1500);
                            Application.Current.Shutdown();
                        }
                    }
                    else
                    {
                        MessageBox.Show($"File not found: {path}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else if (btnConfig.Type == "Website")
                {
                    Process.Start(new ProcessStartInfo(btnConfig.Path) { UseShellExecute = true });
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