using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Nexi.UI.ViewModels;
using System;
using System.Collections.Specialized;
using Microsoft.Extensions.Logging;

namespace Nexi.UI.Views
{
    public partial class ChatView : ReactiveUserControl<ChatViewModel>
    {
        private ScrollViewer? _scrollViewer;
        private ILogger<ChatView>? _logger;

        public ChatView()
        {
            try
            {
                InitializeComponent();
                _logger = App.Current.Services.GetService(typeof(ILogger<ChatView>)) as ILogger<ChatView>;
                _logger?.LogInformation("ChatView initialized");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error initializing ChatView");
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            try
            {
                base.OnAttachedToVisualTree(e);
                _scrollViewer = this.FindControl<ScrollViewer>("MessagesScroll");

                if (DataContext is ChatViewModel viewModel)
                {
                    // Subscribe to collection changes to scroll to bottom when new messages are added
                    viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
                    
                    // Subscribe to the ScrollToBottom event
                    viewModel.ScrollToBottom += ScrollToBottomHandler;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnAttachedToVisualTree");
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            try
            {
                base.OnDetachedFromVisualTree(e);

                if (DataContext is ChatViewModel viewModel)
                {
                    // Unsubscribe from events to prevent memory leaks
                    viewModel.Messages.CollectionChanged -= Messages_CollectionChanged;
                    viewModel.ScrollToBottom -= ScrollToBottomHandler;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in OnDetachedFromVisualTree");
            }
        }

        private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            try
            {
                if (e.Action == NotifyCollectionChangedAction.Add)
                {
                    ScrollToBottom();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in Messages_CollectionChanged");
            }
        }

        private void ScrollToBottomHandler()
        {
            try
            {
                ScrollToBottom();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in ScrollToBottomHandler");
            }
        }

        private void ScrollToBottom()
        {
            try
            {
                if (_scrollViewer != null)
                {
                    _scrollViewer.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in ScrollToBottom");
            }
        }
    }
}