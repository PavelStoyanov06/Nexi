using Nexi.Services.Interfaces;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.IO;
using System.Threading.Tasks;
using System.Text;

namespace Nexi.Services
{
    public class CommandProcessor : ICommandProcessor
    {
        private readonly Dictionary<string, Func<string>> _commands;
        private readonly IWebSearchService _webSearchService;
        private readonly IDocumentService _documentService;

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowTextLength(IntPtr hWnd);
        
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
        
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        
        // Delegate for EnumWindows callback
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        // Window state constants
        private const int SW_MINIMIZE = 6;
        private const int SW_MAXIMIZE = 3;
        private const int SW_RESTORE = 9;
        
        // Window messages
        private const uint WM_CLOSE = 0x0010;

        public CommandProcessor(IWebSearchService webSearchService, IDocumentService documentService)
        {
            _webSearchService = webSearchService;
            _documentService = documentService;
            
            _commands = new Dictionary<string, Func<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["minimize"] = MinimizeActiveWindow,
                ["maximize"] = MaximizeActiveWindow,
                ["restore"] = RestoreActiveWindow,
                ["close"] = CloseActiveWindow,
                ["close window"] = CloseActiveWindow,
                ["open browser"] = OpenDefaultBrowser,
                ["open calculator"] = OpenCalculator,
                ["help"] = () => $"Available commands: {string.Join(", ", GetAvailableCommands())}",
                ["time"] = () => $"Current time is: {DateTime.Now:T}",
                ["search"] = () => "Please use '/search query' to perform a web search",
                ["search google"] = () => "Please use '/search query' to perform a web search",
                ["create document"] = () => "Please use '/create document filename' to create a new document",
                ["create text"] = () => "Please use '/create text filename' to create a new text file",
                ["open url"] = () => "Please use '/open url [number]' or '/open [full url]' to open a URL"
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
        
        private string CloseActiveWindow()
        {
            var handle = GetForegroundWindow();
            if (handle != IntPtr.Zero)
            {
                // Get the window title for feedback
                string windowTitle = GetWindowTitle(handle);
                
                // Post a close message to the window
                bool result = PostMessage(handle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                if (result)
                {
                    return $"Closed window: {(string.IsNullOrEmpty(windowTitle) ? "Untitled" : windowTitle)}";
                }
                return "Failed to close the active window";
            }
            return "No active window to close";
        }
        
        private string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0)
                return string.Empty;
                
            StringBuilder windowTitle = new StringBuilder(length + 1);
            GetWindowText(hWnd, windowTitle, windowTitle.Capacity);
            return windowTitle.ToString();
        }
        
        private bool IsProcessRunning(string processName)
        {
            Process[] processes = Process.GetProcessesByName(processName);
            return processes.Length > 0;
        }
        
        private IntPtr FindWindowByProcessName(string processName)
        {
            IntPtr foundWindow = IntPtr.Zero;
            Process[] processes = Process.GetProcessesByName(processName);
            
            if (processes.Length == 0)
                return IntPtr.Zero;
                
            foreach (Process process in processes)
            {
                // Try to use the main window handle first
                if (process.MainWindowHandle != IntPtr.Zero)
                    return process.MainWindowHandle;
            }
            
            // If no main window handle was found, use EnumWindows to find a window
            uint targetProcessId = (uint)processes[0].Id;
            
            EnumWindows((hWnd, lParam) => {
                uint processId;
                GetWindowThreadProcessId(hWnd, out processId);
                if (processId == targetProcessId)
                {
                    foundWindow = hWnd;
                    return false; // Stop enumeration
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            return foundWindow;
        }

        private string OpenDefaultBrowser()
        {
            try
            {
                // Check if browsers are already running and activate them
                bool browserFound = false;
                
                // Check for Chrome, Edge, Firefox, etc.
                string[] browserProcesses = new[] { "chrome", "msedge", "firefox", "opera", "brave" };
                
                foreach (string browser in browserProcesses)
                {
                    IntPtr browserWindow = FindWindowByProcessName(browser);
                    if (browserWindow != IntPtr.Zero)
                    {
                        // Activate the browser window
                        ShowWindow(browserWindow, SW_RESTORE);
                        SetForegroundWindow(browserWindow);
                        browserFound = true;
                        break;
                    }
                }
                
                // If no browser was found, open a new one
                if (!browserFound)
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
                    return "Opening new browser window";
                }
                
                return "Activated existing browser window";
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
                    // Check if calculator is already running
                    IntPtr calcWindow = FindWindowByProcessName("calc");
                    if (calcWindow != IntPtr.Zero)
                    {
                        // Activate the calculator window
                        ShowWindow(calcWindow, SW_RESTORE);
                        SetForegroundWindow(calcWindow);
                        return "Activated existing calculator window";
                    }
                    
                    // Open new calculator if none found
                    Process.Start("calc.exe");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // For Linux, check for gnome-calculator
                    if (IsProcessRunning("gnome-calculator"))
                    {
                        return "Calculator is already running";
                    }
                    Process.Start("gnome-calculator");
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // For macOS, try to activate existing Calculator
                    Process[] calculators = Process.GetProcessesByName("Calculator");
                    if (calculators.Length > 0)
                    {
                        return "Calculator is already running";
                    }
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
            // Normalize the input by removing punctuation and converting to lowercase
            var normalizedInput = new string(input.Where(c => !char.IsPunctuation(c)).ToArray())
                .Trim()
                .ToLower();

            // Try exact match first
            if (_commands.ContainsKey(normalizedInput))
                return true;

            // Try to match command variations
            foreach (var command in _commands.Keys)
            {
                // Handle common voice recognition variations
                if (normalizedInput == command ||
                    normalizedInput.Contains(command) ||
                    normalizedInput.Replace(" ", "") == command.Replace(" ", ""))
                {
                    return true;
                }
            }

            return false;
        }

        public string ProcessCommand(string input)
        {
            var normalizedInput = new string(input.Where(c => !char.IsPunctuation(c)).ToArray())
                .Trim()
                .ToLower();

            // Handle special commands with parameters
            if (normalizedInput.StartsWith("search "))
            {
                string searchQuery = input.Substring("search".Length).Trim();
                return _webSearchService.OpenBrowserSearch(searchQuery);
            }
            
            if (normalizedInput.StartsWith("search in chat "))
            {
                string searchQuery = input.Substring("search in chat".Length).Trim();
                // This is an async method, but we can't make ProcessCommand async
                // So we'll start the task and return a message
                Task.Run(async () => await _webSearchService.SearchAsync(searchQuery));
                return $"Searching for: {searchQuery}. Results will appear shortly.";
            }
            
            if (normalizedInput.StartsWith("create document ") || normalizedInput.StartsWith("create text "))
            {
                bool isTextFile = normalizedInput.StartsWith("create text ");
                string commandPrefix = isTextFile ? "create text " : "create document ";
                
                // Extract the command part
                string remainingText = input.Substring(commandPrefix.Length).Trim();
                
                // Check if there's content after the filename (separated by a pipe character)
                string fileName;
                string? content = null;
                
                int pipeIndex = remainingText.IndexOf('|');
                if (pipeIndex >= 0)
                {
                    fileName = remainingText.Substring(0, pipeIndex).Trim();
                    content = remainingText.Substring(pipeIndex + 1).Trim();
                }
                else
                {
                    fileName = remainingText;
                }
                
                return isTextFile 
                    ? _documentService.CreateTextDocument(fileName, content)
                    : _documentService.CreateDocument(fileName, content);
            }
            
            if (normalizedInput.StartsWith("open url "))
            {
                string urlParam = input.Substring("open url".Length).Trim();
                return OpenUrl(urlParam);
            }
            
            if (normalizedInput.StartsWith("open ") && (normalizedInput.Contains("http") || normalizedInput.Contains("www")))
            {
                string url = input.Substring("open".Length).Trim();
                return OpenUrl(url);
            }

            // Try exact match first
            if (_commands.TryGetValue(normalizedInput, out var handler))
                return handler();

            // Try to find matching command
            foreach (var (command, commandHandler) in _commands)
            {
                if (normalizedInput.Contains(command) ||
                    normalizedInput.Replace(" ", "") == command.Replace(" ", ""))
                {
                    return commandHandler();
                }
            }

            return $"Unknown command: {input}";
        }

        public IEnumerable<string> GetAvailableCommands()
        {
            return _commands.Keys;
        }

        private string OpenUrl(string urlParam)
        {
            try
            {
                string url;
                
                // Check if the parameter is a number (index from search results)
                if (int.TryParse(urlParam, out int index))
                {
                    // This would require storing search results somewhere
                    // For simplicity, we'll just return an error message
                    return "Sorry, opening URLs by index is not implemented yet.";
                }
                
                // Otherwise, treat it as a direct URL
                url = urlParam;
                
                // Add http:// if missing
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    url = "https://" + url;
                }
                
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}")
                    {
                        CreateNoWindow = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                
                return $"Opening URL: {url}";
            }
            catch (Exception ex)
            {
                return $"Failed to open URL: {ex.Message}";
            }
        }

        private string PerformWebSearch(string query)
        {
            return _webSearchService.OpenBrowserSearch(query);
        }

        private string CreateTextDocument(string fileName)
        {
            return _documentService.CreateTextDocument(fileName);
        }
    }
}