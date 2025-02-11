using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using System.Collections.Specialized;
using Avalonia.Threading;
using Avalonia.Animation;
using Avalonia.Media;
using Nexi.UI.ViewModels;
using System.Threading.Tasks;

namespace Nexi.UI.Views
{
    public partial class ChatView : UserControl
    {
        private ScrollViewer? _scrollViewer;

        public ChatView()
        {
            InitializeComponent();

            _scrollViewer = this.FindControl<ScrollViewer>("MessagesScroll");

            // Listen for changes in the Messages collection
            this.AttachedToVisualTree += (s, e) =>
            {
                if (DataContext is ChatViewModel viewModel)
                {
                    viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
                }
            };

            this.DetachedFromVisualTree += (s, e) =>
            {
                if (DataContext is ChatViewModel viewModel)
                {
                    viewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
                }
            };
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Add && _scrollViewer != null)
            {
                // Scroll to bottom smoothly
                Dispatcher.UIThread.Post(async () =>
                {
                    await Task.Delay(50); // Small delay to ensure message is rendered
                    _scrollViewer.ScrollToEnd();
                });
            }
        }
    }
}