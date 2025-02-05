using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Nexi.UI.ViewModels;

namespace Nexi.UI.Views;

public partial class MainView : UserControl
{
    public MainView()
    {
        InitializeComponent();
        DataContext = App.Current.Services.GetRequiredService<MainViewModel>();
    }
}
