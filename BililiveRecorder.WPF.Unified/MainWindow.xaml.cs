using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BililiveRecorder.WPF.Controls;
using BililiveRecorder.WPF.Pages;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModernWpf.Controls;
using WPFLocalizeExtension.Extensions;

namespace BililiveRecorder.WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly StartupOptions startupOptions;
        private readonly IRootPage? _rootPage;
        private readonly IHostApplicationLifetime applicationLifetime;
        private readonly ILogger _logger;

        public string SoftwareVersion { get; }

#if DEBUG
        public MainWindow() : this(null, null, null)
        {
        }
#endif

        public MainWindow(
            StartupOptions startupOptions,
            IRootPage? rootPage,
            IHostApplicationLifetime applicationLifetime,
            ILogger<MainWindow>? logger = null)
        {
            this.SoftwareVersion = GitVersionInformation.FullSemVer;
            this.startupOptions = startupOptions;
            this._rootPage = rootPage;
            this.applicationLifetime = applicationLifetime;
            this._logger = logger as ILogger ?? NullLogger.Instance;
            this.InitializeComponent();
        }

        private bool CloseConfirmed = false;

        private bool notification_showed = false;
        public bool HideToTray { get; set; }

        internal Action<string, string, BalloonIcon>? ShowBalloonTipCallback { get; set; }

        public bool PromptCloseConfirm { get; set; } = true;

        private Task? _windowClosingTask;

        internal void SuperActivateAction()
        {
            try
            {
                this.Show();
                this.WindowState = WindowState.Normal;
                this.Topmost = true;
                this.Activate();
                this.Topmost = false;
                this.Focus();
            }
            catch (Exception)
            { }
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (this.startupOptions.ShouldPromptCloseConfirm() && !this.CloseConfirmed)
            {
                e.Cancel = true;
                if (_windowClosingTask is not null)
                {
                    if (!_windowClosingTask.IsCompleted)
                    {
                        return;
                    }
                }
                _ = Task.Run(async () => await this.Dispatcher.Invoke(async () =>
                {
                    var result = ContentDialogResult.None;
                    try
                    {
                        result = await new CloseWindowConfirmDialog().ShowAsync();
                    }
                    catch (TaskCanceledException)
                    {
                        result = ContentDialogResult.Primary;
                    }
                    finally
                    {
                        if (result == ContentDialogResult.Primary)
                        {
                            this.CloseConfirmed = true;
                            this.Closing -= this.Window_Closing;
                            this.Dispatcher.Invoke(this.Close, DispatcherPriority.Normal);
                        }
                        _windowClosingTask = null;
                    }
                }), applicationLifetime.ApplicationStopping);
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (this.HideToTray && this.WindowState == WindowState.Minimized)
            {
                this.Hide();
                if (!this.notification_showed)
                {
                    this.notification_showed = true;
                    var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:TaskbarIconControl_Title");
                    var body = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:TaskbarIconControl_MinimizedNotification");
                    this.ShowBalloonTipCallback?.Invoke(title, body, BalloonIcon.Info);
                }
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            this.Content = this._rootPage;
        }

        public void CloseWithoutConfirmAction()
        {
            this.Closing -= this.Window_Closing;
            this.Close();
        }
    }
}
