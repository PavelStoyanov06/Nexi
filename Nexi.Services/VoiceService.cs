using NAudio.Wave;
using Nexi.Services.Interfaces;
using Whisper.net;
using Whisper.net.Ggml;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Nexi.Services
{
    public class VoiceService : IVoiceService, IDisposable
    {
        private readonly ILogger<VoiceService> _logger;
        private WaveInEvent? _waveIn;
        private WhisperFactory? _whisperFactory;
        private readonly BlockingCollection<byte[]> _audioQueue;
        private CancellationTokenSource? _processingCts;
        private bool _isListening;
        private readonly SemaphoreSlim _stateLock = new(1, 1);
        private bool _disposed;

        public event EventHandler<string>? SpeechRecognized;

        public bool IsListening => _isListening;

        public VoiceService(ILogger<VoiceService> logger)
        {
            _logger = logger;
            _audioQueue = new BlockingCollection<byte[]>();
            InitializeWhisper();
        }

        private async void InitializeWhisper()
        {
            try
            {
                // Download and load the model if not present
                var modelPath = "whisper-model.bin";
                if (!File.Exists(modelPath))
                {
                    _logger.LogInformation("Downloading Whisper model...");
                    using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Tiny);
                    using var fileStream = File.Create(modelPath);
                    await modelStream.CopyToAsync(fileStream);
                    _logger.LogInformation("Whisper model downloaded successfully");
                }

                _whisperFactory = WhisperFactory.FromPath(modelPath);
                _logger.LogInformation("Whisper initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Whisper");
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

                // Check if we have the Whisper model
                if (_whisperFactory == null)
                {
                    _logger.LogError("Whisper not initialized - cannot start listening");
                    return;
                }

                // Log microphone devices
                var devices = WaveIn.Devices;
                _logger.LogInformation($"Available audio devices: {string.Join(", ", devices.Select(d => d.ProductName))}");

                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 1),
                    BufferMilliseconds = 50
                };
                _logger.LogInformation("WaveIn initialized");

                _waveIn.DataAvailable += WaveIn_DataAvailable;
                _processingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                // Start audio processing task
                _ = Task.Run(ProcessAudioQueueAsync, _processingCts.Token);

                _waveIn.StartRecording();
                _isListening = true;

                _logger.LogInformation("Started listening for voice commands");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting voice listening");
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

                _waveIn?.StopRecording();
                _waveIn?.Dispose();
                _waveIn = null;

                _processingCts?.Cancel();
                _processingCts?.Dispose();
                _processingCts = null;

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

        private void WaveIn_DataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isListening) return;

            try
            {
                // Log audio levels for debugging
                var maxAmplitude = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    var sample = BitConverter.ToInt16(e.Buffer, i);
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
                }
                _logger.LogDebug($"Audio level: {maxAmplitude}");

                // Copy the audio data
                var buffer = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, buffer, e.BytesRecorded);

                // Add to processing queue
                _audioQueue.TryAdd(buffer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio data");
            }
        }

        private async Task ProcessAudioQueueAsync()
        {
            try
            {
                while (!_processingCts?.Token.IsCancellationRequested ?? false)
                {
                    var audioData = new List<byte>();
                    var silence = 0;
                    const int silenceThreshold = 3; // Number of silent chunks before processing
                    const int maxAudioLength = 16000 * 2 * 30; // 30 seconds of audio max

                    // Collect audio until silence is detected or max length reached
                    while (silence < silenceThreshold && audioData.Count < maxAudioLength)
                    {
                        if (_audioQueue.TryTake(out var chunk, 100, _processingCts.Token))
                        {
                            audioData.AddRange(chunk);

                            // Check if chunk is silence
                            if (IsSilence(chunk))
                                silence++;
                            else
                                silence = 0;
                        }
                    }

                    if (audioData.Count > 0)
                    {
                        await ProcessAudioChunkAsync(audioData.ToArray());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                _logger.LogInformation("Audio processing cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio queue");
            }
        }

        private bool IsSilence(byte[] audio, int threshold = 1000)
        {
            try
            {
                // Convert bytes to 16-bit samples and check amplitude
                for (int i = 0; i < audio.Length; i += 2)
                {
                    if (i + 1 >= audio.Length) break;
                    var sample = BitConverter.ToInt16(audio, i);
                    if (Math.Abs(sample) > threshold)
                        return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for silence");
                return true; // Assume silence on error
            }
        }

        private async Task ProcessAudioChunkAsync(byte[] audioData)
        {
            if (_whisperFactory == null)
            {
                _logger.LogWarning("Whisper factory not initialized");
                return;
            }

            try
            {
                using var processor = _whisperFactory.CreateBuilder()
                    .WithLanguage("en")
                    .Build();

                // Convert audio to required format if needed
                using var memoryStream = new MemoryStream(audioData);

                // Process with Whisper
                await foreach (var result in processor.ProcessAsync(memoryStream))
                {
                    if (!string.IsNullOrWhiteSpace(result.Text))
                    {
                        var processedText = result.Text.Trim();
                        _logger.LogInformation("Speech recognized: {Text}", processedText);
                        OnSpeechRecognized(processedText);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing audio chunk");
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
                    _waveIn?.Dispose();
                    _whisperFactory?.Dispose();
                    _audioQueue.Dispose();
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