using System.Windows;

namespace BililiveRecorder.WPF
{
    public class StartupOptions
    {
        public string? CommandArgumentRecorderPath { get; set; }
        public bool CommandArgumentFirstRun { get; set; } = true;
        public bool CommandArgumentAskPath { get; set; }
        public bool CommandArgumentHide { get; set; }

        public WorkDirectoryLoader.WorkDirectoryData Data { get; init; } = new();

        public void ShutdownApp(int exitCode = 0, System.Windows.Threading.Dispatcher? dispatcher = null)
        {
            if (exitCode != 0)
            {
                promptCloseConfirm = false;
            }
            if (dispatcher is not null)
            {
                dispatcher.Invoke(() => Application.Current.Shutdown(exitCode));
            }
            else
            {
                Application.Current.Shutdown(exitCode);
            }
        }

        public bool ShouldPromptCloseConfirm() => promptCloseConfirm;

        private bool promptCloseConfirm = true;
    }
}
