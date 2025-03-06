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
            {
                settings = new UserSettings
                {
                    SelectedTheme = theme,
                    LastModifiedAt = DateTime.UtcNow
                };
                context.UserSettings.Add(settings);
            }
            else
            {
                settings.SelectedTheme = theme;
                settings.LastModifiedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateSelectedModelAsync(string? modelId)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var settings = await context.UserSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new UserSettings
                    {
                        SelectedModelId = modelId,
                        LastModifiedAt = DateTime.UtcNow
                    };
                    context.UserSettings.Add(settings);
                    await context.SaveChangesAsync();
                    return settings;
                }
                
                // If modelId is null, just clear the selection
                if (modelId == null)
                {
                    settings.SelectedModelId = null;
                    settings.SelectedModel = null;
                    settings.LastModifiedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                    _logger.LogInformation("Cleared selected model");
                    return settings;
                }
                
                // Validate the model exists in the database
                var model = await context.AIModels.FindAsync(modelId);
                if (model == null)
                {
                    _logger.LogWarning($"Model {modelId} not found in database, not updating selection");
                    return settings;
                }
                
                // Update the settings
                settings.SelectedModelId = modelId;
                settings.SelectedModel = model;
                settings.LastModifiedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                _logger.LogInformation($"Updated selected model to {modelId}");
                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating selected model to {modelId}");
                throw;
            }
        }

        public async Task<UserSettings> UpdateUseGPUAsync(bool useGPU)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings
                {
                    UseGPU = useGPU,
                    LastModifiedAt = DateTime.UtcNow
                };
                context.UserSettings.Add(settings);
            }
            else
            {
                settings.UseGPU = useGPU;
                settings.LastModifiedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateVoiceSettingsAsync(string? inputDevice, int sensitivity)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings
                {
                    SelectedInputDevice = inputDevice,
                    InputSensitivity = sensitivity,
                    LastModifiedAt = DateTime.UtcNow
                };
                context.UserSettings.Add(settings);
            }
            else
            {
                settings.SelectedInputDevice = inputDevice;
                settings.InputSensitivity = sensitivity;
                settings.LastModifiedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateAccentColorAsync(bool useSystem, string? color = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var settings = await context.UserSettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new UserSettings
                {
                    UseSystemAccent = useSystem,
                    AccentColor = color ?? "#A880E4",
                    LastModifiedAt = DateTime.UtcNow
                };
                context.UserSettings.Add(settings);
            }
            else
            {
                settings.UseSystemAccent = useSystem;
                if (!useSystem && !string.IsNullOrEmpty(color))
                {
                    settings.AccentColor = color;
                }
                settings.LastModifiedAt = DateTime.UtcNow;
            }
            await context.SaveChangesAsync();
            return settings;
        }

        public async Task<UserSettings> UpdateLlamaSharpSettingsAsync(int contextSize, int gpuLayerCount)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var settings = await context.UserSettings.FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new UserSettings
                    {
                        ContextSize = contextSize,
                        GpuLayerCount = gpuLayerCount,
                        LastModifiedAt = DateTime.UtcNow
                    };
                    context.UserSettings.Add(settings);
                }
                else
                {
                    settings.ContextSize = contextSize;
                    settings.GpuLayerCount = gpuLayerCount;
                    settings.LastModifiedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();
                return settings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating LlamaSharp settings");
                throw;
            }
        }
    }
}