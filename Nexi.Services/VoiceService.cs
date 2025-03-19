using Microsoft.Extensions.Logging;
using System.Speech.Recognition;
using Nexi.Services.Interfaces;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexi.Services
{
    public class VoiceService : IVoiceService, IDisposable
    {
        // MMDevice API constants and P/Invoke for audio device enumeration
        [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint waveInGetNumDevs();

        [DllImport("winmm.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint waveInGetDevCaps(uint deviceID, ref WAVEINCAPS waveInCaps, uint cbwaveInCaps);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }
        
        private readonly ILogger<VoiceService> _logger;
        private SpeechRecognitionEngine? _recognizer;
        private bool _isListening;
        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private bool _disposed;
        private double _confidenceThreshold = 0.7; // Default confidence threshold
        private List<string> _cachedInputDevices = new();
        
        public event EventHandler<string>? SpeechRecognized;
        public bool IsListening => _isListening;

        public VoiceService(ILogger<VoiceService> logger)
        {
            _logger = logger;
            InitializeSpeechRecognition();
            // Initialize the device list
            _ = RefreshAvailableInputDevicesAsync();
        }

        private void InitializeSpeechRecognition()
        {
            try
            {
                _recognizer = new SpeechRecognitionEngine();

                // Create a more comprehensive grammar for basic commands
                var basicCommands = new Choices(new string[] {
                    "minimize", "maximize", "restore",
                    "close", "close window",
                    "open browser", "open calculator",
                    "time", "help"
                });

                // Add grammar for basic commands
                var basicGrammarBuilder = new GrammarBuilder(basicCommands);
                var basicGrammar = new Grammar(basicGrammarBuilder);
                _recognizer.LoadGrammar(basicGrammar);

                // Add grammar for search commands
                var searchPrefix = new Choices(new string[] { "search", "search for", "search in chat", "search in chat for" });
                var searchGrammarBuilder = new GrammarBuilder(searchPrefix);
                searchGrammarBuilder.AppendDictation();
                var searchGrammar = new Grammar(searchGrammarBuilder);
                _recognizer.LoadGrammar(searchGrammar);

                // Add grammar for document creation commands
                var createPrefix = new Choices(new string[] { "create document", "create text", "create text file", "create document called", "create text called", "create text file called" });
                var createGrammarBuilder = new GrammarBuilder(createPrefix);
                createGrammarBuilder.AppendDictation();
                var createGrammar = new Grammar(createGrammarBuilder);
                _recognizer.LoadGrammar(createGrammar);

                // Add grammar for URL opening commands
                var openPrefix = new Choices(new string[] { "open url", "open website", "go to", "navigate to" });
                var openGrammarBuilder = new GrammarBuilder(openPrefix);
                openGrammarBuilder.AppendDictation();
                var openGrammar = new Grammar(openGrammarBuilder);
                _recognizer.LoadGrammar(openGrammar);

                _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
                _recognizer.SetInputToDefaultAudioDevice();

                _logger.LogInformation("Speech recognition initialized with expanded command set");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize speech recognition");
                throw;
            }
        }

        private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result.Confidence > _confidenceThreshold) // Use the configurable threshold
            {
                string recognizedText = e.Result.Text;
                _logger.LogInformation($"Speech recognized with confidence {e.Result.Confidence}: {recognizedText}");
                
                // Process the recognized text to format it as a command
                string processedCommand = ProcessRecognizedText(recognizedText);
                
                OnSpeechRecognized(processedCommand);
            }
            else
            {
                _logger.LogDebug($"Speech recognized with low confidence {e.Result.Confidence}: {e.Result.Text}");
            }
        }

        private string ProcessRecognizedText(string text)
        {
            // Format the recognized text to match command patterns expected by CommandProcessor
            
            // Handle search commands
            if (text.StartsWith("search for", StringComparison.OrdinalIgnoreCase))
            {
                return "search " + text.Substring("search for".Length).Trim();
            }
            else if (text.StartsWith("search in chat for", StringComparison.OrdinalIgnoreCase))
            {
                return "search in chat " + text.Substring("search in chat for".Length).Trim();
            }
            
            // Handle document creation commands
            if (text.StartsWith("create document called", StringComparison.OrdinalIgnoreCase))
            {
                return "create document " + text.Substring("create document called".Length).Trim();
            }
            else if (text.StartsWith("create text called", StringComparison.OrdinalIgnoreCase) || 
                     text.StartsWith("create text file called", StringComparison.OrdinalIgnoreCase))
            {
                return "create text " + text.Substring(text.IndexOf("called") + "called".Length).Trim();
            }
            
            // Handle URL opening commands
            if (text.StartsWith("open website", StringComparison.OrdinalIgnoreCase) || 
                text.StartsWith("go to", StringComparison.OrdinalIgnoreCase) || 
                text.StartsWith("navigate to", StringComparison.OrdinalIgnoreCase))
            {
                string url = text.Contains(" ") ? text.Substring(text.IndexOf(" ")).Trim() : "";
                return "open url " + url;
            }
            
            return text;
        }

        public async Task StartListeningAsync(CancellationToken cancellationToken = default)
        {
            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                if (_isListening)
                {
                    _logger.LogWarning("Already listening");
                    return;
                }

                _recognizer?.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
                _logger.LogInformation("Started listening for voice commands");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting voice recognition");
                throw;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task StopListeningAsync()
        {
            await _stateLock.WaitAsync();
            try
            {
                if (!_isListening)
                {
                    _logger.LogWarning("Not currently listening");
                    return;
                }

                _recognizer?.RecognizeAsyncStop();
                _isListening = false;
                _logger.LogInformation("Stopped listening for voice commands");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping voice listening");
                throw;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        protected virtual void OnSpeechRecognized(string text)
        {
            try
            {
                SpeechRecognized?.Invoke(this, text);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in speech recognized event handler");
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    if (_recognizer != null)
                    {
                        _recognizer.RecognizeAsyncStop();
                        _recognizer.Dispose();
                    }
                    _stateLock.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error disposing voice service");
                }
                finally
                {
                    _disposed = true;
                }
            }
        }

        public List<string> GetAvailableInputDevices()
        {
            // Return the cached list of devices
            return _cachedInputDevices;
        }

        public async Task<List<string>> RefreshAvailableInputDevicesAsync()
        {
            await _stateLock.WaitAsync();
            try
            {
                _cachedInputDevices.Clear();
                
                // Always add "Default" as the first option
                _cachedInputDevices.Add("Default");
                
                // For Windows, enumerate audio input devices using WinMM API
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    try
                    {
                        uint deviceCount = waveInGetNumDevs();
                        _logger.LogInformation($"Found {deviceCount} audio input devices");
                        
                        for (uint i = 0; i < deviceCount; i++)
                        {
                            var waveInCaps = new WAVEINCAPS();
                            uint structSize = (uint)Marshal.SizeOf(typeof(WAVEINCAPS));
                            uint result = waveInGetDevCaps(i, ref waveInCaps, structSize);
                            
                            if (result == 0 && !string.IsNullOrEmpty(waveInCaps.szPname))
                            {
                                _cachedInputDevices.Add(waveInCaps.szPname);
                                _logger.LogInformation($"Added device: {waveInCaps.szPname}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error enumerating audio input devices using WinMM");
                    }
                }
                
                // If no devices were found or on other platforms, add some default options
                if (_cachedInputDevices.Count <= 1)
                {
                    _logger.LogWarning("No actual devices found, using fallback device list");
                    _cachedInputDevices.Add("Built-in Microphone");
                    _cachedInputDevices.Add("Headset Microphone");
                }
                
                return _cachedInputDevices;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task SetInputDeviceAsync(string deviceName)
        {
            await _stateLock.WaitAsync();
            try
            {
                // We would normally set the specific input device here
                // However, for simplicity, we'll just log the selected device
                _logger.LogInformation($"Setting input device to: {deviceName}");
                
                // Recreate the speech recognizer with the new device
                if (_recognizer != null)
                {
                    _recognizer.RecognizeAsyncStop();
                    _recognizer.Dispose();
                }
                
                _recognizer = new SpeechRecognitionEngine();
                
                // Re-initialize all the grammars
                InitializeSpeechRecognition();
                
                // Set the input device
                if (deviceName == "Default" || string.IsNullOrEmpty(deviceName))
                {
                    _recognizer.SetInputToDefaultAudioDevice();
                    _logger.LogInformation("Set input to default audio device");
                }
                else
                {
                    // Try to find the device by name
                    try 
                    {
                        bool deviceFound = false;
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        {
                            uint deviceCount = waveInGetNumDevs();
                            for (uint i = 0; i < deviceCount; i++)
                            {
                                var waveInCaps = new WAVEINCAPS();
                                uint structSize = (uint)Marshal.SizeOf(typeof(WAVEINCAPS));
                                uint result = waveInGetDevCaps(i, ref waveInCaps, structSize);
                                
                                if (result == 0 && waveInCaps.szPname == deviceName)
                                {
                                    // Use the device ID to set the input device
                                    // For now, we still need to use the default device as
                                    // System.Speech doesn't provide a way to set input by index
                                    _recognizer.SetInputToDefaultAudioDevice();
                                    _logger.LogInformation($"Found device '{deviceName}' but using default device as System.Speech limitation");
                                    deviceFound = true;
                                    break;
                                }
                            }
                        }
                        
                        if (!deviceFound)
                        {
                            _recognizer.SetInputToDefaultAudioDevice();
                            _logger.LogWarning($"Device '{deviceName}' not found. Using default device instead.");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error setting input device to {deviceName}. Using default device.");
                        _recognizer.SetInputToDefaultAudioDevice();
                    }
                }
                
                // If we were previously listening, start listening again
                if (_isListening)
                {
                    _recognizer.RecognizeAsync(RecognizeMode.Multiple);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting input device to {deviceName}");
                throw;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task SetInputSensitivityAsync(int sensitivity)
        {
            try
            {
                // Convert sensitivity (0-100) to confidence threshold (0-1)
                // Lower sensitivity = higher threshold
                _confidenceThreshold = 1.0 - (sensitivity / 100.0);
                
                // Ensure threshold is within reasonable bounds
                if (_confidenceThreshold < 0.3) _confidenceThreshold = 0.3;
                if (_confidenceThreshold > 0.9) _confidenceThreshold = 0.9;
                
                _logger.LogInformation($"Set input sensitivity to {sensitivity}, confidence threshold: {_confidenceThreshold}");
                
                // No need to restart the recognizer as the threshold is used at runtime
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error setting input sensitivity to {sensitivity}");
                throw;
            }
        }
    }
}