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
        private bool _disposed;
        private string _selectedInputDevice = "Default";
        private int _inputSensitivity = 50;
        private double _minConfidenceThreshold = 0.6;

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
            await _stateLock.WaitAsync();
            try
            {
                bool wasListening = _isListening;

                // Stop listening if currently active
                if (wasListening)
                {
                    _recognizer?.RecognizeAsyncStop();
                    _isListening = false;
                }

                _selectedInputDevice = deviceName;
                _inputSensitivity = sensitivity;

                // In a real implementation, you would select the specific device
                // For now, just update the confidence threshold based on sensitivity
                _minConfidenceThreshold = 0.8 - (sensitivity / 100.0 * 0.4); // Scale from 0.4 to 0.8

                _logger.LogInformation("Updated input device to {DeviceName} with sensitivity {Sensitivity} (threshold: {Threshold})",
                    deviceName, sensitivity, _minConfidenceThreshold);

                // Resume listening if it was active before
                if (wasListening)
                {
                    _recognizer?.RecognizeAsync(RecognizeMode.Multiple);
                    _isListening = true;
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
                _recognizer = new SpeechRecognitionEngine();

                // Create a simple grammar for commands - use fewer commands for now
                var choices = new Choices(new string[] {
            "help", "time"
        });

                var grammarBuilder = new GrammarBuilder(choices);
                var grammar = new Grammar(grammarBuilder);

                _recognizer.LoadGrammar(grammar);
                _recognizer.SpeechRecognized += Recognizer_SpeechRecognized;
                _recognizer.SetInputToDefaultAudioDevice();

                // Calculate initial confidence threshold based on sensitivity
                _minConfidenceThreshold = 0.6; // Start with a fixed value
                _logger.LogInformation("Speech recognition initialized with confidence threshold {Threshold}", _minConfidenceThreshold);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize speech recognition - continuing without voice support");
                // Don't throw - allow app to run without speech
            }
        }

        private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
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
                        if (_isListening)
                        {
                            try
                            {
                                _recognizer.RecognizeAsyncStop();
                                _isListening = false;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error stopping recognition during disposal");
                            }
                        }

                        try
                        {
                            _recognizer.SpeechRecognized -= Recognizer_SpeechRecognized;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error removing event handler during disposal");
                        }

                        try
                        {
                            _recognizer.Dispose();
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error disposing recognizer");
                        }

                        _recognizer = null;
                    }

                    try
                    {
                        _stateLock.Dispose();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error disposing state lock");
                    }
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
    }
}