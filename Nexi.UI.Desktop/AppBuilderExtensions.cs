using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Nexi.UI;
using System;

namespace Nexi.UI.Desktop
{
    public static class AppBuilderExtensions
    {
        public static AppBuilder WithServices(this AppBuilder builder, IServiceCollection services)
        {
            return builder.AfterSetup(appBuilder =>
            {
                // Set the services provider using the static property directly
                App.Services = services.BuildServiceProvider();
            });
        }
    }
}