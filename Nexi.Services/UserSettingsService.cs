using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class UserSettingsService : IUserSettingsService
    {
        private readonly NexiDbContext _context;
        private readonly ILogger<UserSettingsService> _logger;

        public UserSettingsService(NexiDbContext context, ILogger<UserSettingsService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<UserSettings> GetSettingsAsync()
        {
            var settings = await _context.UserSettings.Include(s => s.SelectedModel).FirstOrDefaultAsync();
            if (settings == null)
            {
                // Create default settings if none exist
                settings = new UserSettings
                {
                    UseGPU = false,
                    InputSensitivity = 50,
                    SelectedTheme = ThemeMode.System,
                    UseSystemAccent = true,
                    AccentColor = "#A880E4",
                    LastModifiedAt = DateTime.UtcNow
                };
                _context.UserSettings.Add(settings);
                await _context.SaveChangesAsync();
            }
            return settings;
        }

        public async Task<UserSettings> UpdateSettingsAsync(UserSettings settings)
        {
            var existingSettings = await _context.UserSettings.FirstOrDefaultAsync();
            if (existingSettings == null)
            {
                _context.UserSettings.Add(settings);
            }
            else
            {
                _context.Entry(existingSettings).CurrentValues.SetValues(settings);
                existingSettings.LastModifiedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
            return existingSettings ?? settings;
        }

        public async Task<UserSettings> UpdateThemeAsync(ThemeMode theme)
        {
            var settings = await GetSettingsAsync();
            settings.SelectedTheme = theme;
            settings.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateSelectedModelAsync(string modelId)
        {
            var settings = await GetSettingsAsync();
            settings.SelectedModelId = modelId;
            settings.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateUseGPUAsync(bool useGPU)
        {
            var settings = await GetSettingsAsync();
            settings.UseGPU = useGPU;
            settings.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateVoiceSettingsAsync(string? inputDevice, int sensitivity)
        {
            var settings = await GetSettingsAsync();
            settings.SelectedInputDevice = inputDevice;
            settings.InputSensitivity = sensitivity;
            settings.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateAccentColorAsync(bool useSystem, string? color = null)
        {
            var settings = await GetSettingsAsync();
            settings.UseSystemAccent = useSystem;
            if (!useSystem && !string.IsNullOrEmpty(color))
            {
                settings.AccentColor = color;
            }
            settings.LastModifiedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return settings;
        }
    }
}