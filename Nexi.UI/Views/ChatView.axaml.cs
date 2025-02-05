using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nexi.UI.Views
{
    public partial class ChatView : UserControl
    {
        public ChatView()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}