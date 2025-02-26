using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nexi.UI.Views
{
    public partial class ApiTokensSettingsView : UserControl
    {
        public ApiTokensSettingsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}