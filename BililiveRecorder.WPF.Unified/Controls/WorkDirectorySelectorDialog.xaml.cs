using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
//using Microsoft.WindowsAPICodePack.Dialogs;
using WPFLocalizeExtension.Extensions;

#nullable enable
namespace BililiveRecorder.WPF.Controls
{
    /// <summary>
    /// Interaction logic for WorkDirectorySelectorDialog.xaml
    /// </summary>
    public partial class WorkDirectorySelectorDialog : INotifyPropertyChanged
    {
        private WorkDirectorySelectorDialogError error = WorkDirectorySelectorDialogError.None;
        private string path = string.Empty;
        private bool skipAsking;

        public WorkDirectorySelectorDialogError Error { get => this.error; set => this.SetField(ref this.error, value); }

        public virtual string Path { get => this.path; set => this.SetField(ref this.path, value); }

        public virtual bool SkipAsking { get => this.skipAsking; set => this.SetField(ref this.skipAsking, value); }

        public WorkDirectorySelectorDialog(StartupOptions? startupOptions = null)
        {
            this.DataContext = this;
            this.path = startupOptions?.Data?.Path ?? string.Empty;
            this.skipAsking = (startupOptions?.Data?.SkipAsking == true);

            this.InitializeComponent();
        }

        public enum WorkDirectorySelectorDialogError
        {
            None,
            UnknownError,
            PathNotSupported,
            PathDoesNotExist,
            PathContainsFiles,
            FailedToLoadConfig,
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) { return false; }
            field = value; this.OnPropertyChanged(propertyName); return true;
        }

        private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:WorkDirectorySelector_Title");

            var fileDialog = new System.Windows.Forms.FolderBrowserDialog()
            {
                InitialDirectory = Path,
                Description = title,
                UseDescriptionForTitle = true,
            };
            if (fileDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                this.Path = fileDialog.SelectedPath;
            }
        }
    }
}
