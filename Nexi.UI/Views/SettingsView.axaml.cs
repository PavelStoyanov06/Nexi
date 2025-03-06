using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nexi.UI.ViewModels;
using System;

namespace Nexi.UI.Views
{
    public partial class SettingsView : UserControl
    {
        private SettingsViewModel _viewModel;

        public SettingsView()
        {
            AvaloniaXamlLoader.Load(this);
            
            // Get the SettingsViewModel from the service provider
            var app = (App)App.Current;
            _viewModel = app.Services.GetRequiredService<SettingsViewModel>();
            
            // Set the DataContext
            DataContext = _viewModel;
            
            // Refresh models when the view is loaded
            this.AttachedToVisualTree += SettingsView_AttachedToVisualTree;
        }

        private async void SettingsView_AttachedToVisualTree(object sender, Avalonia.VisualTreeAttachmentEventArgs e)
        {
            try
            {
                // Refresh the models
                await _viewModel.RefreshModelsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error refreshing models: {ex.Message}");
            }
        }
    }
}