using Nexi.Services.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Nexi.Services
{
    public class CommandProcessor : ICommandProcessor
    {
        private readonly Dictionary<string, Func<string>> _commands;

        public CommandProcessor()
        {
            _commands = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["minimize"] = MinimizeActiveWindow,
                ["maximize"] = MaximizeActiveWindow,
                ["close"] = CloseActiveWindow,
                ["open browser"] = OpenDefaultBrowser,
                ["help"] = () => $"Available commands: {string.Join(", ", GetAvailableCommands())}",
                ["time"] = () => $"Current time is: {DateTime.Now:T}"
            };
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
                try
                {
                    return handler();
                }
                catch (Exception ex)
                {
                    return $"Error executing command: {ex.Message}";
                }
            }
            return $"Unknown command: {input}";
        }

        public IEnumerable<string> GetAvailableCommands()
        {
            return _commands.Keys;
        }

        private string OpenDefaultBrowser()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("cmd", "/c start http://www.google.com") { CreateNoWindow = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", "http://www.google.com");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", "http://www.google.com");
                }
                return "Opening browser...";
            }
            catch (Exception ex)
            {
                return $"Failed to open browser: {ex.Message}";
            }
        }

        private string MinimizeActiveWindow()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = GetForegroundWindow();
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_MINIMIZE);
                    return "Window minimized";
                }
            }
            return "Minimize command is only supported on Windows";
        }

        private string MaximizeActiveWindow()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = GetForegroundWindow();
                if (handle != IntPtr.Zero)
                {
                    ShowWindow(handle, SW_MAXIMIZE);
                    return "Window maximized";
                }
            }
            return "Maximize command is only supported on Windows";
        }

        private string CloseActiveWindow()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var handle = GetForegroundWindow();
                if (handle != IntPtr.Zero)
                {
                    SendMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    return "Window closed";
                }
            }
            return "Close command is only supported on Windows";
        }

        #region Windows API
        private const int SW_MINIMIZE = 6;
        private const int SW_MAXIMIZE = 3;
        private const int WM_CLOSE = 0x0010;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        #endregion
    }
}