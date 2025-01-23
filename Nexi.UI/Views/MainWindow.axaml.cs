using Avalonia.Controls;
using Avalonia.ReactiveUI;
using Nexi.UI.ViewModels;

namespace Nexi.UI.Views
{
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }
    }
}