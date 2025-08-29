using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace GameDiskLauncher
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // A unique name for the mutex. A GUID is recommended to ensure it's unique.
        private const string AppMutexName = "GameDiskLauncher-7E2242A5-B4D5-4FE1-A3E9-44F2A0B1A51C";
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // Try to create a new mutex.
            _mutex = new Mutex(true, AppMutexName, out bool createdNew);

            if (!createdNew)
            {
                // If the mutex already exists, another instance is running.
                // Find the existing process and bring its window to the foreground.
                ActivateExistingInstance();
                // Shut down the current (new) instance.
                Application.Current.Shutdown();
                return;
            }

            // If we are here, this is the first instance.
            // Manually create and show the main window.
            var mainWindow = new MainWindow();
            mainWindow.Show();

            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Release the mutex when the application exits.
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }

        private static void ActivateExistingInstance()
        {
            Process current = Process.GetCurrentProcess();
            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                if (process.Id != current.Id)
                {
                    // Found the other process.
                    IntPtr handle = process.MainWindowHandle;
                    if (IsIconic(handle))
                    {
                        ShowWindow(handle, SW_RESTORE);
                    }
                    SetForegroundWindow(handle);
                    break;
                }
            }
        }

        // Win32 API calls to interact with the window of the other process.
        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool IsIconic(IntPtr hWnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetForegroundWindow(IntPtr hWnd);

        private const int SW_RESTORE = 9;
    }
}