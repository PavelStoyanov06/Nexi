using ReactiveUI;

namespace Nexi.UI.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public virtual string Name => GetType().Name;
}
