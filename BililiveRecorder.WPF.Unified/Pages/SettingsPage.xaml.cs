using BililiveRecorder.Core.Config.V2;

namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for SettingsPage.xaml
    /// </summary>
    public partial class SettingsPage : IPage
    {
#if DEBUG

        public SettingsPage():this(new())
        {
        }
#endif

        public SettingsPage(GlobalConfig config)
        {
            this.DataContext = config;
            this.InitializeComponent();
        }
    }
}
