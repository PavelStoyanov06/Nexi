using System;
using Avalonia;
using Avalonia.ReactiveUI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexi.Data.Context;

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
            var services = new ServiceCollection();

            // Add DbContext configuration
            services.AddDbContext<NexiDbContext>(options =>
                options.UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NexiDb;Trusted_Connection=True;MultipleActiveResultSets=true"));

            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .UseReactiveUI();
        }
    }
}