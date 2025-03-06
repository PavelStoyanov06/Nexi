using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.Logging;

namespace Nexi.UI.Views
{
    public partial class ModelsView : UserControl
    {
        private readonly ILogger<ModelsView>? _logger;

        public ModelsView()
        {
            InitializeComponent();
        }

        public ModelsView(ILogger<ModelsView> logger)
        {
            _logger = logger;
            InitializeComponent();
            _logger.LogInformation("ModelsView initialized");
        }
        
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}