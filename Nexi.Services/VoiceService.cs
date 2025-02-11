using NAudio.Wave;
using NAudio.CoreAudioApi;
using Whisper.net;
using Whisper.net.Ggml;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;

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
        private readonly WaveOutEvent _waveOut;
        private AudioFileReader? _startSound;
        private AudioFileReader? _stopSound;

        public event EventHandler<string>? SpeechRecognized;

        public bool IsListening => _isListening;

        public VoiceService(ILogger<VoiceService> logger)
        {
            _logger = logger;
            _audioQueue = new BlockingCollection<byte[]>();
            _waveOut = new WaveOutEvent();
            InitializeWhisper();
            LoadSoundEffects();
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

        private void LoadSoundEffects()
        {
            try
            {
                var startSoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", "start.wav");
                var stopSoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Sounds", "stop.wav");

                _logger.LogInformation($"Loading sound effects from: {startSoundPath} and {stopSoundPath}");

                if (File.Exists(startSoundPath))
                {
                    _startSound = new AudioFileReader(startSoundPath);
                    _logger.LogInformation("Start sound loaded successfully");
                }
                else
                {
                    _logger.LogWarning("Start sound file not found");
                }

                if (File.Exists(stopSoundPath))
                {
                    _stopSound = new AudioFileReader(stopSoundPath);
                    _logger.LogInformation("Stop sound loaded successfully");
                }
                else
                {
                    _logger.LogWarning("Stop sound file not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load sound effects");
            }
        }

        private void PlaySound(AudioFileReader? sound)
        {
            try
            {
                if (sound != null)
                {
                    sound.Position = 0;
                    _waveOut.Stop();
                    _waveOut.Init(sound);
                    _waveOut.Play();
                    _logger.LogInformation("Sound effect played successfully");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to play sound effect");
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

                if (_whisperFactory == null)
                {
                    _logger.LogError("Whisper not initialized - cannot start listening");
                    return;
                }

                // Log available audio devices
                using var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
                _logger.LogInformation($"Available audio devices: {string.Join(", ", devices.Select(d => d.FriendlyName))}");

                // Play start sound
                PlaySound(_startSound);

                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(16000, 1), // Required format for Whisper
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

                // Play stop sound
                PlaySound(_stopSound);

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

                // Create a new MemoryStream for the WAV file
                using var wavStream = new MemoryStream();

                // Write WAV header
                // RIFF header
                await wavStream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("RIFF")); // ChunkID
                await wavStream.WriteAsync(BitConverter.GetBytes(36 + audioData.Length)); // ChunkSize
                await wavStream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("WAVE")); // Format

                // fmt subchunk
                await wavStream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("fmt ")); // Subchunk1ID
                await wavStream.WriteAsync(BitConverter.GetBytes(16)); // Subchunk1Size
                await wavStream.WriteAsync(BitConverter.GetBytes((short)1)); // AudioFormat (PCM)
                await wavStream.WriteAsync(BitConverter.GetBytes((short)1)); // NumChannels (Mono)
                await wavStream.WriteAsync(BitConverter.GetBytes(16000)); // SampleRate
                await wavStream.WriteAsync(BitConverter.GetBytes(16000 * 2)); // ByteRate
                await wavStream.WriteAsync(BitConverter.GetBytes((short)2)); // BlockAlign
                await wavStream.WriteAsync(BitConverter.GetBytes((short)16)); // BitsPerSample

                // data subchunk
                await wavStream.WriteAsync(System.Text.Encoding.ASCII.GetBytes("data")); // Subchunk2ID
                await wavStream.WriteAsync(BitConverter.GetBytes(audioData.Length)); // Subchunk2Size
                await wavStream.WriteAsync(audioData); // Data

                // Reset stream position
                wavStream.Position = 0;

                await foreach (var result in processor.ProcessAsync(wavStream))
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
                    _startSound?.Dispose();
                    _stopSound?.Dispose();
                    _waveOut.Dispose();
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