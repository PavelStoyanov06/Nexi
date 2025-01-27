using ReactiveUI;
using System.Collections.ObjectModel;

namespace Nexi.Desktop.ViewModels
{
    public class NotificationsViewModel : ViewModelBase
    {
        public NotificationsViewModel()
        {
            Notifications = new ObservableCollection<string>();
        }

        public ObservableCollection<string> Notifications { get; }
    }
}