using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using System.Xaml;
using BililiveRecorder.WPF.Models;

#nullable enable
namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for AnnouncementPage.xaml
    /// </summary>
    public partial class AnnouncementPage : IPage
    {

#if DEBUG
        public AnnouncementPage() : this(null)
        {

        }
#endif
        public AnnouncementPage(AnnouncementDataModel model)
        {
            this.DataContext = model;
            this.InitializeComponent();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            //TODO: Notifies BackgroundJob to run once again.
            ((AnnouncementDataModel)this.DataContext).Content = null;
            ((AnnouncementDataModel)this.DataContext).Loading = true;
        }



#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void Button_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;

            var fileDialog = new Microsoft.Win32.OpenFileDialog()
            {
                Multiselect = false,
                Title = "Load local file",
                CheckFileExists = true,
                CheckPathExists = true,
                DereferenceLinks = true,
                ValidateNames = true,
            };
            if (fileDialog.ShowDialog() == true)
            {
                try
                {
                    var ms = new MemoryStream();
                    using (var fs = File.OpenRead(fileDialog.FileName))
                    {
                        await fs.CopyToAsync(ms);
                    }
                    ms.Seek(0, SeekOrigin.Begin);
                    var obj = System.Windows.Markup.XamlReader.Load(ms);
                    ((AnnouncementDataModel)this.DataContext).Content = null;
                    ((AnnouncementDataModel)this.DataContext).Content = obj as UIElement;
                    ((AnnouncementDataModel)this.DataContext).CacheTime = DateTimeOffset.Now;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.ToString(), "Loading Error");
                }
            }
        }
    }
}
