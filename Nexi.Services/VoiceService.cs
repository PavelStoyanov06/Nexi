using NAudio.Wave;
using NAudio.CoreAudioApi;
using Whisper.net;
using Whisper.net.Ggml;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Nexi.Services.Interfaces;
using System.Text;

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
            _logger.LogInformation("VoiceService constructor called");
            _audioQueue = new BlockingCollection<byte[]>();
            _waveOut = new WaveOutEvent();
            InitializeWhisper();
            LoadSoundEffects();
            _logger.LogInformation("VoiceService initialization completed");
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
                    WaveFormat = new WaveFormat(16000, 16, 1), // 16kHz, 16-bit, mono
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
                // More obvious logging for debugging
                _logger.LogInformation($"Receiving audio data: {e.BytesRecorded} bytes");

                // Log audio levels for debugging
                var maxAmplitude = 0;
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    var sample = BitConverter.ToInt16(e.Buffer, i);
                    maxAmplitude = Math.Max(maxAmplitude, Math.Abs(sample));
                }
                _logger.LogInformation($"Audio level: {maxAmplitude}"); // Changed to Information for debugging

                // Copy the audio data
                var buffer = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, buffer, e.BytesRecorded);

                // Add to processing queue
                var added = _audioQueue.TryAdd(buffer);
                _logger.LogInformation($"Added to queue: {added}");
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
                // Convert raw PCM to WAV format
                using var memoryStream = new MemoryStream();
                using var writer = new BinaryWriter(memoryStream);

                // Write WAV header
                writer.Write(Encoding.ASCII.GetBytes("RIFF")); // ChunkID
                writer.Write(36 + audioData.Length); // ChunkSize
                writer.Write(Encoding.ASCII.GetBytes("WAVE")); // Format
                writer.Write(Encoding.ASCII.GetBytes("fmt ")); // Subchunk1ID
                writer.Write(16); // Subchunk1Size
                writer.Write((short)1); // AudioFormat (PCM)
                writer.Write((short)1); // NumChannels (Mono)
                writer.Write(16000); // SampleRate
                writer.Write(32000); // ByteRate
                writer.Write((short)2); // BlockAlign
                writer.Write((short)16); // BitsPerSample
                writer.Write(Encoding.ASCII.GetBytes("data")); // Subchunk2ID
                writer.Write(audioData.Length); // Subchunk2Size
                writer.Write(audioData); // Data

                memoryStream.Position = 0;

                using var processor = _whisperFactory.CreateBuilder()
                    .WithLanguage("en")
                    .Build();

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