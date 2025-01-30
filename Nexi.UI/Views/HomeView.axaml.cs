using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Nexi.UI;

public partial class HomeView : UserControl
{
    public HomeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
