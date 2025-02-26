using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nexi.UI.ViewModels;
using System;

namespace Nexi.UI.Views
{
    public partial class TokenRequiredDialog : Window
    {
        public TokenRequiredDialog()
        {
            InitializeComponent();

            // Handle view model's request to close
            if (DataContext is TokenRequiredViewModel viewModel)
            {
                viewModel.RequestClose += (sender, result) =>
                {
                    Close(result);
                };
            }

#if DEBUG
            this.AttachDevTools();
#endif
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}