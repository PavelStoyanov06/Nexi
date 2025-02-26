using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nexi.UI.ViewModels;

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
            this.AttachDevTools();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}