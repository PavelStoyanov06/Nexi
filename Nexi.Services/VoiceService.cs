using Microsoft.Extensions.Logging;
using System.Speech.Recognition;
using Nexi.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Nexi.Services
{
    public class VoiceService : IVoiceService, IDisposable
    {
        private readonly ILogger<VoiceService> _logger;
        private SpeechRecognitionEngine? _recognizer;
        private bool _isListening;
        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private readonly SemaphoreSlim _disposeLock = new(1, 1);
        private bool _disposed;
        private string _selectedInputDevice = "Default";
        private int _inputSensitivity = 50;
        private double _minConfidenceThreshold = 0.6;
        private CancellationTokenSource? _listeningCts;

        public event EventHandler<string>? SpeechRecognized;
        public bool IsListening => _isListening;

        public VoiceService(ILogger<VoiceService> logger)
        {
            _logger = logger;
            InitializeSpeechRecognition();
        }

        public IEnumerable<string> GetAvailableInputDevices()
        {
            try
            {
                // In a real implementation, you would use NAudio or another library to get actual devices
                // For simplicity, we'll return hardcoded values
                return new List<string> {
                    "Default",
                    "System Microphone",
                    "Headset Microphone"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available input devices");
                return new List<string> { "Default" };
            }
        }

        public async Task UpdateInputDeviceAsync(string deviceName, int sensitivity)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VoiceService));
            }

            await _stateLock.WaitAsync();
            try
            {
                bool wasListening = _isListening;

                // Stop listening if currently active
                if (wasListening)
                {
                    await StopListeningInternalAsync();
                }

                _selectedInputDevice = deviceName;
                _inputSensitivity = sensitivity;

                // Update confidence threshold based on sensitivity
                _minConfidenceThreshold = 0.8 - (sensitivity / 100.0 * 0.4);

                _logger.LogInformation("Updated input device to {DeviceName} with sensitivity {Sensitivity} (threshold: {Threshold})",
                    deviceName, sensitivity, _minConfidenceThreshold);

                // Re-initialize speech recognition
                InitializeSpeechRecognition();

                // Resume listening if it was active before
                if (wasListening)
                {
                    await StartListeningInternalAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating input device");
                throw;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private void InitializeSpeechRecognition()
        {
            try
            {
                if (_recognizer != null)
                {
                    _recognizer.SpeechRecognized -= Recognizer_SpeechRecognized;
                    _recognizer.Dispose();
                }

                _recognizer = new SpeechRecognitionEngine();

                // Create a simple grammar for commands
                var choices = new Choices(new string[] {
                    "minimize", "maximize", "restore",
                    "open browser", "open calculator",
                    "time", "help"
                });

                var grammarBuilder = new GrammarBuilder(choices);
                var grammar = new Grammar(grammarBuilder);

                _recognizer.LoadGrammar(grammar);
                _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
                _recognizer.SetInputToDefaultAudioDevice();

                _logger.LogInformation("Speech recognition initialized with confidence threshold {Threshold}",
                    _minConfidenceThreshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize speech recognition");
                throw;
            }
        }

        private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            if (_disposed || _listeningCts?.IsCancellationRequested == true)
            {
                return;
            }

            if (e.Result.Confidence > _minConfidenceThreshold)
            {
                _logger.LogInformation("Speech recognized with confidence {Confidence}: {Text}",
                    e.Result.Confidence, e.Result.Text);
                OnSpeechRecognized(e.Result.Text);
            }
            else
            {
                _logger.LogDebug("Speech rejected with low confidence {Confidence}: {Text}",
                    e.Result.Confidence, e.Result.Text);
            }
        }

        public async Task StartListeningAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VoiceService));
            }

            await _stateLock.WaitAsync(cancellationToken);
            try
            {
                await StartListeningInternalAsync();
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async Task StartListeningInternalAsync()
        {
            if (_isListening)
            {
                _logger.LogWarning("Already listening");
                return;
            }

            try
            {
                _listeningCts?.Dispose();
                _listeningCts = new CancellationTokenSource();

                _recognizer?.RecognizeAsync(RecognizeMode.Multiple);
                _isListening = true;
                _logger.LogInformation("Started listening for voice commands");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting voice recognition");
                throw;
            }
        }

        public async Task StopListeningAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (await _disposeLock.WaitAsync(TimeSpan.FromSeconds(1)))
            {
                try
                {
                    await StopListeningInternalAsync();
                }
                finally
                {
                    _disposeLock.Release();
                }
            }
        }

        private async Task StopListeningInternalAsync()
        {
            try
            {
                if (!_isListening)
                {
                    _logger.LogWarning("Not currently listening");
                    return;
                }

                _listeningCts?.Cancel();
                _recognizer?.RecognizeAsyncStop();
                _isListening = false;
                _logger.LogInformation("Stopped listening for voice commands");

                await Task.Delay(100); // Short delay to ensure recognition has stopped
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping voice listening");
                throw;
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
                if (_disposeLock.Wait(TimeSpan.FromSeconds(1)))
                {
                    try
                    {
                        // Cancel any ongoing listening
                        _listeningCts?.Cancel();

                        if (_recognizer != null)
                        {
                            try
                            {
                                if (_isListening)
                                {
                                    _recognizer.RecognizeAsyncStop();
                                    _isListening = false;
                                }
                                _recognizer.SpeechRecognized -= Recognizer_SpeechRecognized;
                                _recognizer.Dispose();
                                _recognizer = null;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error disposing recognizer");
                            }
                        }
                    }
                    finally
                    {
                        _listeningCts?.Dispose();
                        _listeningCts = null;
                        _disposeLock.Dispose();
                        _stateLock.Dispose();
                        _disposed = true;
                    }
                }
                else
                {
                    // Force cleanup if lock can't be acquired
                    try
                    {
                        _listeningCts?.Cancel();
                        _recognizer?.Dispose();
                        _recognizer = null;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during forced cleanup");
                    }
                    finally
                    {
                        _listeningCts?.Dispose();
                        _listeningCts = null;
                        _disposeLock.Dispose();
                        _stateLock.Dispose();
                        _disposed = true;
                    }
                }
            }
        }
    }
}