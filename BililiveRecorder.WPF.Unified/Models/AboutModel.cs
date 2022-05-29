using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

#nullable enable
namespace BililiveRecorder.WPF.Models
{
    public class AboutModel : INotifyPropertyChanged
    {

        private ObservableCollection<string> libraries = new();
        public event PropertyChangedEventHandler? PropertyChanged;


        public ObservableCollection<string> Libraries
        {
            get => libraries; set
            {
                this.libraries = value;
                PropertyChanged?.Invoke(this, new(nameof(Libraries)));
            }
        }


    }
}
