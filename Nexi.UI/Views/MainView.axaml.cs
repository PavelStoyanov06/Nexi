using Avalonia.Controls;
using Nexi.UI.ViewModels;

namespace Nexi.UI.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
