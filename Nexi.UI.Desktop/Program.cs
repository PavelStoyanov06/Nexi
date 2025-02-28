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

            // Excerpt from Program.cs showing updated service registration
            services.AddSingleton<IAuthenticationService, AuthenticationService>();
            services.AddSingleton<ICommandProcessor, CommandProcessor>();
            services.AddSingleton<IVoiceService, VoiceService>();
            services.AddSingleton<IChatStorageService, ChatStorageService>();
            services.AddSingleton<IAIModelService, AIModelService>();
            services.AddSingleton<IUserSettingsService, UserSettingsService>();

            // Updated ModelRepository registration which uses the consolidated AIModelData model
            services.AddSingleton<IModelRepository, ModelRepository>();

            // Register OnnxAIService which depends on IAIModelService and IModelRepository
            services.AddSingleton<IAIService>(sp => new OnnxAIService(
                sp.GetRequiredService<ILogger<OnnxAIService>>(),
                sp.GetRequiredService<IAIModelService>(),
                sp)); // Pass service provider for resolving dependencies

            // Make sure to register view models that might need updated services
            services.AddTransient<MainViewModel>();
            services.AddTransient<ChatHistoryViewModel>();
            services.AddTransient<ModelsViewModel>(sp => new ModelsViewModel(
                sp.GetRequiredService<IAIModelService>(),
                sp.GetRequiredService<IModelRepository>(),
                sp.GetRequiredService<IAIService>(),
                sp.GetRequiredService<ILogger<ModelsViewModel>>(),
                sp.GetRequiredService<IDbContextFactory<NexiDbContext>>()
            ));
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<ApiTokensViewModel>();

            // Create service provider
            var serviceProvider = services.BuildServiceProvider();

            // Ensure database is created
            using (var scope = serviceProvider.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<NexiDbContext>();
                dbContext.Database.EnsureCreated();
                Console.WriteLine("Database initialization completed");
            }

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI()
                .WithServices(services);
        }
    }
}