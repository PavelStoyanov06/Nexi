using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nexi.Desktop.Views
{
    public partial class NotificationsView : UserControl
    {
        public NotificationsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}