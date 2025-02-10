using ReactiveUI;
using System;

namespace Nexi.UI.ViewModels
{
    public class ViewModelBase : ReactiveObject, IDisposable
    {
        public virtual string Name => GetType().Name;

        public virtual void Dispose()
        {
            // Base implementation
        }
    }
}