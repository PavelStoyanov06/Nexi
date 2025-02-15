using Microsoft.Extensions.Logging;
using System.Speech.Recognition;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class VoiceService : IVoiceService, IDisposable
    {
        private readonly ILogger<VoiceService> _logger;
        private SpeechRecognitionEngine? _recognizer;
        private bool _isListening;
        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private bool _disposed;

        public event EventHandler<string>? SpeechRecognized;
        public bool IsListening => _isListening;

        public VoiceService(ILogger<VoiceService> logger)
        {
            _logger = logger;
            InitializeSpeechRecognition();
        }

        private void InitializeSpeechRecognition()
        {
            try
            {
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize speech recognition");
                throw;
            }
        }

        private void Recognizer_SpeechRecognized(object? sender, SpeechRecognizedEventArgs e)
        {
            if (e.Result.Confidence > 0.8)
            {
                OnSpeechRecognized(e.Result.Text);
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
    }
}