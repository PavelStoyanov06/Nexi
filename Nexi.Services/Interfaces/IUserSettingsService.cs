using Nexi.Data.Models;

namespace Nexi.Services.Interfaces
{
    public interface IUserSettingsService
    {
        Task<UserSettings> GetSettingsAsync();
        Task<UserSettings> UpdateSettingsAsync(UserSettings settings);
        Task<UserSettings> UpdateThemeAsync(ThemeMode theme);
        Task<UserSettings> UpdateSelectedModelAsync(string modelId);
        Task<UserSettings> UpdateUseGPUAsync(bool useGPU);
        Task<UserSettings> UpdateVoiceSettingsAsync(string? inputDevice, int sensitivity);
        Task<UserSettings> UpdateAccentColorAsync(bool useSystem, string? color = null);
    }
}