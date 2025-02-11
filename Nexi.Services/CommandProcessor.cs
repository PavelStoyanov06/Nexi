using Nexi.Services.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nexi.Services
{
    public class CommandProcessor : ICommandProcessor
    {
        private readonly Dictionary<string, Func<string>> _commands;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        // Window state constants
        private const int SW_MINIMIZE = 6;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;

        public CommandProcessor()
        {
            _commands = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["minimize"] = MinimizeActiveWindow,
                ["maximize"] = MaximizeActiveWindow,
                ["restore"] = RestoreActiveWindow,
                ["open browser"] = OpenDefaultBrowser,
                ["open calculator"] = OpenCalculator,
                ["help"] = () => $"Available commands: {string.Join(", ", GetAvailableCommands())}",
                ["time"] = () => $"Current time is: {DateTime.Now:T}"
            };
        }

        private string MinimizeActiveWindow()
        {
            var handle = GetForegroundWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_MINIMIZE);
                return "Window minimized";
            }
            return "No active window to minimize";
        }

        private string MaximizeActiveWindow()
        {
            var handle = GetForegroundWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_MAXIMIZE);
                return "Window maximized";
            }
            return "No active window to maximize";
        }

        private string RestoreActiveWindow()
        {
            var handle = GetForegroundWindow();
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_RESTORE);
                return "Window restored";
            }
            return "No active window to restore";
        }

        private string OpenDefaultBrowser()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("cmd", "/c start https://www.google.com")
                    {
                        CreateNoWindow = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", "https://www.google.com");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", "https://www.google.com");
                }
                return "Opening browser";
            }
            catch (Exception ex)
            {
                return $"Failed to open browser: {ex.Message}";
            }
        }

        private string OpenCalculator()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start("calc.exe");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("gnome-calculator");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", "-a Calculator");
                }
                return "Opening calculator";
            }
            catch (Exception ex)
            {
                return $"Failed to open calculator: {ex.Message}";
            }
        }

        public bool IsCommand(string input)
        {
            return _commands.ContainsKey(input.Trim().ToLower());
        }

        public string ProcessCommand(string input)
        {
            var trimmedInput = input.Trim().ToLower();
            if (_commands.TryGetValue(trimmedInput, out var handler))
            {
                return handler();
            }
            return $"Unknown command: {input}";
        }

        public IEnumerable<string> GetAvailableCommands()
        {
            return _commands.Keys;
        }
    }
}