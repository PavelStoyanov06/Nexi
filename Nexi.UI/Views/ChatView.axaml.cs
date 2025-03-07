using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Nexi.UI.ViewModels;
using System;
using System.Collections.Specialized;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using Nexi.UI.Models;
using System.Linq;
using Avalonia.Threading;

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
                    
                    // Subscribe to property changes on individual messages
                    foreach (var message in viewModel.Messages)
                    {
                        if (message is INotifyPropertyChanged notifyPropertyChanged)
                        {
                            notifyPropertyChanged.PropertyChanged += Message_PropertyChanged;
                        }
                    }
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
                    
                    // Unsubscribe from property changes on individual messages
                    foreach (var message in viewModel.Messages)
                    {
                        if (message is INotifyPropertyChanged notifyPropertyChanged)
                        {
                            notifyPropertyChanged.PropertyChanged -= Message_PropertyChanged;
                        }
                    }
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
                // Handle new items
                if (e.NewItems != null)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is INotifyPropertyChanged notifyPropertyChanged)
                        {
                            notifyPropertyChanged.PropertyChanged += Message_PropertyChanged;
                        }
                    }
                }
                
                // Handle removed items
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is INotifyPropertyChanged notifyPropertyChanged)
                        {
                            notifyPropertyChanged.PropertyChanged -= Message_PropertyChanged;
                        }
                    }
                }
                
                // Scroll to bottom for new items
                if (e.Action == NotifyCollectionChangedAction.Add || 
                    e.Action == NotifyCollectionChangedAction.Replace)
                {
                    ScrollToBottom();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in Messages_CollectionChanged");
            }
        }
        
        private void Message_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            try
            {
                // When a message's content changes, scroll to bottom
                if (e.PropertyName == nameof(ChatMessage.Content))
                {
                    // Use Dispatcher to ensure we're on the UI thread
                    Dispatcher.UIThread.Post(ScrollToBottom);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in Message_PropertyChanged");
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