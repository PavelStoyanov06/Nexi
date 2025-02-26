using Avalonia;
using Avalonia.ReactiveUI;
using Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nexi.Data.Context;
using Nexi.Services;
using Nexi.Services.AI;
using Nexi.Services.Interfaces;
using Nexi.UI.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;

namespace Nexi.UI.Desktop
{
    class Program
    {
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Note: This is internal, not public
        internal static AppBuilder BuildAvaloniaApp()
        {
            // Create a configuration, with graceful fallback for different environments
            IConfiguration configuration;
            try
            {
                // Try to find appsettings.json in the executable directory
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");

                if (File.Exists(configPath))
                {
                    configuration = new ConfigurationBuilder()
                        .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                        .AddJsonFile("appsettings.json", optional: true)
                        .Build();
                }
                else
                {
                    // If not found, use default connection string
                    Console.WriteLine("No appsettings.json found. Using default connection string.");
                    configuration = new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string>
                        {
                            { "ConnectionStrings:DefaultConnection", "Server=(localdb)\\mssqllocaldb;Database=NexiDb;Trusted_Connection=True;MultipleActiveResultSets=true" }
                        })
                        .Build();
                }
            }
            catch (Exception ex)
            {
                // Use default configuration in case of error
                Console.WriteLine($"Error loading configuration: {ex.Message}. Using default values.");
                configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "ConnectionStrings:DefaultConnection", "Server=(localdb)\\mssqllocaldb;Database=NexiDb;Trusted_Connection=True;MultipleActiveResultSets=true" }
                    })
                    .Build();
            }

            // Set up services
            var services = new ServiceCollection();

            // Add DbContext
            services.AddDbContext<NexiDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            // Add DbContextFactory for use in services
            services.AddDbContextFactory<NexiDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            // Add logging
            services.AddLogging(config =>
            {
                config.AddConsole();
                config.AddDebug();
            });

            services.AddSingleton<IAuthenticationService, AuthenticationService>();

            // It should look something like this:
            services.AddSingleton<ICommandProcessor, CommandProcessor>();
            services.AddSingleton<IVoiceService, VoiceService>();
            services.AddSingleton<IChatStorageService, ChatStorageService>();
            services.AddSingleton<IAIModelService, AIModelService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();
            services.AddSingleton<IModelRepository>(sp => new ModelRepository(
                    sp.GetRequiredService<IDbContextFactory<NexiDbContext>>(),
                    sp.GetRequiredService<ILogger<ModelRepository>>(),
                    sp));
            services.AddSingleton<IAIService>(sp => new OnnxAIService(
                    sp.GetRequiredService<ILogger<OnnxAIService>>(),
                    sp.GetRequiredService<IAIModelService>(),
                    sp));
            services.AddSingleton<IAuthenticationService, AuthenticationService>(); // Add this line

            // Register view models
            services.AddTransient<MainViewModel>();
            services.AddTransient<ChatHistoryViewModel>();
            services.AddTransient<ModelsViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ApiTokensViewModel>();

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI()
                .WithServices(services);
        }
    }
}