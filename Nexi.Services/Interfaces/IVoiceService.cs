using System;
using System.Threading;
using System.Threading.Tasks;

namespace Nexi.Services.Interfaces
{
    public interface IVoiceService
    {
        Task StartListeningAsync(CancellationToken cancellationToken = default);
        Task StopListeningAsync();
        bool IsListening { get; }
        event EventHandler<string> SpeechRecognized;
    }
}