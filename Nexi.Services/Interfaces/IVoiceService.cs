using System;
using System.Collections.Generic;
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
        IEnumerable<string> GetAvailableInputDevices();
        Task UpdateInputDeviceAsync(string deviceName, int sensitivity);
    }
}