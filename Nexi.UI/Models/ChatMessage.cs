using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Nexi.UI.Models
{
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _content = string.Empty;
        private DateTime _timestamp;
        private bool _isUser;
        private bool _isSystemMessage;
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsUser
        {
            get => _isUser;
            set
            {
                if (_isUser != value)
                {
                    _isUser = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsSystemMessage
        {
            get => _isSystemMessage;
            set
            {
                if (_isSystemMessage != value)
                {
                    _isSystemMessage = value;
                    OnPropertyChanged();
                }
            }
        }
        
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}