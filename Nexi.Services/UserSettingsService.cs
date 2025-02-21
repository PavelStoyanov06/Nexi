using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Data.Models;
using Nexi.Services.Interfaces;

namespace Nexi.Services
{
    public class UserSettingsService : IUserSettingsService
    {
        private readonly IDbContextFactory<NexiDbContext> _contextFactory;
        private readonly ILogger<UserSettingsService> _logger;

        public UserSettingsService(IDbContextFactory<NexiDbContext> contextFactory, ILogger<UserSettingsService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public async Task<UserSettings> GetSettingsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.Include(s => s.SelectedModel).FirstOrDefaultAsync();
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
                context.UserSettings.Add(settings);
                await context.SaveChangesAsync();
            }
            return settings;
        }

        public async Task<UserSettings> UpdateSettingsAsync(UserSettings settings)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existingSettings = await context.UserSettings.FirstOrDefaultAsync();
            if (existingSettings == null)
            {
                context.UserSettings.Add(settings);
            }
            else
            {
                context.Entry(existingSettings).CurrentValues.SetValues(settings);
                existingSettings.LastModifiedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            return existingSettings ?? settings;
        }

        public async Task<UserSettings> UpdateThemeAsync(ThemeMode theme)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
                settings = await GetSettingsAsync();

            settings.SelectedTheme = theme;
            settings.LastModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateSelectedModelAsync(string modelId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
                settings = await GetSettingsAsync();

            settings.SelectedModelId = modelId;
            settings.LastModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateUseGPUAsync(bool useGPU)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
                settings = await GetSettingsAsync();

            settings.UseGPU = useGPU;
            settings.LastModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateVoiceSettingsAsync(string? inputDevice, int sensitivity)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
                settings = await GetSettingsAsync();

            settings.SelectedInputDevice = inputDevice;
            settings.InputSensitivity = sensitivity;
            settings.LastModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateAccentColorAsync(bool useSystem, string? color = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
                settings = await GetSettingsAsync();

            settings.UseSystemAccent = useSystem;
            if (!useSystem && !string.IsNullOrEmpty(color))
            {
                settings.AccentColor = color;
            }
            settings.LastModifiedAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
            return settings;
        }
    }
}