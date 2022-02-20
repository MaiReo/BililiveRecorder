using System;
using System.ComponentModel;
using System.Windows;

namespace BililiveRecorder.WPF.Models
{
    public class AnnouncementDataModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private UIElement? content;
        private bool loading = true;
        private DateTimeOffset? cacheTime;

        public UIElement? Content
        {
            get => content; set
            {
                content = value;
                this.PropertyChanged?.Invoke(this, new(nameof(Content)));
            }
        }

        public bool Loading
        {
            get => loading; set
            {
                loading = value;
                this.PropertyChanged?.Invoke(this, new(nameof(Loading)));
            }
        }

        public DateTimeOffset? CacheTime
        {
            get => cacheTime; set
            {
                cacheTime = value;
                this.PropertyChanged?.Invoke(this, new(nameof(CacheTime)));
            }
        }
    }
}
