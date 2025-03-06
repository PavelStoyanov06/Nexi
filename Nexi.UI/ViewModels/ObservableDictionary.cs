using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace Nexi.UI.ViewModels
{
    public class ObservableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public new TValue this[TKey key]
        {
            get => base[key];
            set
            {
                base[key] = value;
                OnPropertyChanged("Item[]");
                OnPropertyChanged($"Item[{key}]");
                OnPropertyChanged(nameof(Keys));
                OnPropertyChanged(nameof(Values));
                OnPropertyChanged(nameof(Count));
            }
        }

        public new void Add(TKey key, TValue value)
        {
            base.Add(key, value);
            OnPropertyChanged("Item[]");
            OnPropertyChanged($"Item[{key}]");
            OnPropertyChanged(nameof(Keys));
            OnPropertyChanged(nameof(Values));
            OnPropertyChanged(nameof(Count));
        }

        public new bool Remove(TKey key)
        {
            bool result = base.Remove(key);
            if (result)
            {
                OnPropertyChanged("Item[]");
                OnPropertyChanged($"Item[{key}]");
                OnPropertyChanged(nameof(Keys));
                OnPropertyChanged(nameof(Values));
                OnPropertyChanged(nameof(Count));
            }
            return result;
        }

        public new void Clear()
        {
            base.Clear();
            OnPropertyChanged("Item[]");
            OnPropertyChanged(nameof(Keys));
            OnPropertyChanged(nameof(Values));
            OnPropertyChanged(nameof(Count));
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 