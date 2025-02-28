using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Nexi.UI.ViewModels;
using System;

namespace Nexi.UI.Views
{
    public partial class ModelsView : UserControl
    {
        private ScrollViewer _scrollViewer;

        public ModelsView()
        {
            InitializeComponent();

            // Get the scroll viewer reference 
            _scrollViewer = this.FindControl<ScrollViewer>("ModelsScrollViewer");

            // Attach to visual tree events
            this.AttachedToVisualTree += ModelsView_AttachedToVisualTree;
            this.DetachedFromVisualTree += ModelsView_DetachedFromVisualTree;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void ModelsView_AttachedToVisualTree(object sender, Avalonia.VisualTreeAttachmentEventArgs e)
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            }
        }

        private void ModelsView_DetachedFromVisualTree(object sender, Avalonia.VisualTreeAttachmentEventArgs e)
        {
            if (_scrollViewer != null)
            {
                _scrollViewer.ScrollChanged -= ScrollViewer_ScrollChanged;
            }
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (DataContext is ModelsViewModel viewModel)
            {
                // Don't trigger if already loading or no more models
                if (viewModel.IsLoadingMoreModels || !viewModel.HasMoreModels)
                    return;

                // Check if we've scrolled to the bottom (with some threshold)
                if (_scrollViewer.Offset.Y >= _scrollViewer.ScrollBarMaximum.Y - _scrollViewer.Viewport.Height - 200)
                {
                    // Load more models when we're near the bottom
                    viewModel.LoadMoreModelsCommand.Execute(null);
                }
            }
        }
    }
}