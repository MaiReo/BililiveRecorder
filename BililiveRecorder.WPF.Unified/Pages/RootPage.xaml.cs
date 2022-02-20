using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using BililiveRecorder.Core;
using BililiveRecorder.Core.Config;
using BililiveRecorder.Core.Config.V2;
using BililiveRecorder.DependencyInjection;
using BililiveRecorder.WPF.Controls;
using BililiveRecorder.WPF.Extensions;
using BililiveRecorder.WPF.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModernWpf.Controls;
using ModernWpf.Media.Animation;
using Serilog;
using Path = System.IO.Path;

#nullable enable
namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for RootPage.xaml
    /// </summary>
    public partial class RootPage : UserControl, IRootPage, IDisposable
    {
        private readonly StartupOptions startupOptions;
        private readonly WorkDirectoryLoader workDirectoryLoader;
        private readonly WorkDirectorySelectorDialog workDirectorySelectorDialog;
        private readonly ILifetimeScope lifetimeScope;
        private readonly HashSet<IPage> pages = new();
        private ILifetimeScope? lifetimeScopeChild;
        private IHostApplicationLifetime? _applicationLifetime;


        private bool disposedValue;
        private readonly NavigationTransitionInfo transitionInfo = new DrillInNavigationTransitionInfo();

        private int SettingsClickCount = 0;

#if DEBUG
        public RootPage() : this(
            null,
            null,
            new WorkDirectorySelectorDialog(null),
            Enumerable.Empty<IPage>(),
            null,
            null)
        {
        }
#endif
        //void AddType(Type t) => this.PageMap.Add(t.Name, t);
        //AddType(typeof(RoomListPage));
        //AddType(typeof(SettingsPage));
        //AddType(typeof(LogPage));
        //AddType(typeof(AboutPage));
        //AddType(typeof(AdvancedSettingsPage));
        //AddType(typeof(AnnouncementPage));
        //AddType(typeof(ToolboxAutoFixPage));
        //AddType(typeof(ToolboxRemuxPage));
        //AddType(typeof(ToolboxDanmakuMergerPage));

        public RootPage(
            StartupOptions startupOptions,
            WorkDirectoryLoader workDirectoryLoader,
            WorkDirectorySelectorDialog workDirectorySelectorDialog,
            IEnumerable<IPage> pages,
            ILifetimeScope lifetimeScope,
            IHostApplicationLifetime applicationLifetime)
        {
            this.startupOptions = startupOptions;
            this.workDirectoryLoader = workDirectoryLoader;
            this.workDirectorySelectorDialog = workDirectorySelectorDialog;
            this.lifetimeScope = lifetimeScope;
            this._applicationLifetime = applicationLifetime;
            foreach (var page in pages)
            {
                this.pages.Add(page);
            }
            this.InitializeComponent();
#if DEBUG
            this.DebugBuildIcon.Visibility = Visibility.Visible;
#endif
        }

        private void LoopAskPath(Func<ConfigV2?, bool, bool, CancellationToken, Task>? postConfigure)
        {
            var token = _applicationLifetime?.ApplicationStopping ?? default;
            _ = Task.Run(async () =>
            {
                var (config, runToolBox, exit) = await AskPath(this.workDirectorySelectorDialog, token);
                if (postConfigure is not null)
                {
                    await postConfigure(config, runToolBox, exit, token);
                }
            }, token);
        }

        private async Task<(ConfigV2? config, bool runToolbox, bool exit)> AskPath(WorkDirectorySelectorDialog dialog, CancellationToken cancellationToken)
        {
            var error = dialog.Error;
            var loop = true;
            var exit = false;
            var runToolbox = false;
            var skipAsking = false;
            while (loop)
            {
                cancellationToken.ThrowIfCancellationRequested();
                dialog.Error = error;
                ContentDialogResult result = ContentDialogResult.Primary;
                if (!startupOptions.Data.SkipAsking && error == WorkDirectorySelectorDialog.WorkDirectorySelectorDialogError.None)
                {
                    result = await this.Dispatcher.Invoke(async () => await dialog.ShowAsync());
                    skipAsking = false;
                }
                else
                {
                    skipAsking = true;
                }
                switch (result)
                {
                    case ContentDialogResult.Primary:
                        break;
                    case ContentDialogResult.Secondary:
                        //TODO: ToolBox
                        runToolbox = true;
                        loop = false;
                        break;
                    case ContentDialogResult.None:
                    default:
                        exit = true;
                        loop = false;
                        break;
                }
                cancellationToken.ThrowIfCancellationRequested();
                if (!loop)
                {
                    break;
                }
                try
                {
                    var path = Path.GetFullPath(dialog.Path);

                    var configFilePath = Path.Combine(path, "config.json");

                    if (!Directory.Exists(path))
                    {
                        error = WorkDirectorySelectorDialog.WorkDirectorySelectorDialogError.PathDoesNotExist;
                        continue;
                    }
                    else if (!Directory.EnumerateFiles(path).Any())
                    {
                        // 可用的空文件夹
                    }
                    else if (!File.Exists(configFilePath))
                    {
                        error = WorkDirectorySelectorDialog.WorkDirectorySelectorDialogError.PathContainsFiles;
                        continue;
                    }
                    var config = ConfigParser.LoadFrom(path);
                    if (config is null)
                    {
                        error = WorkDirectorySelectorDialog.WorkDirectorySelectorDialogError.FailedToLoadConfig;
                        continue;
                    }
                    config.Global.WorkDirectory = path;
                    if (startupOptions.CommandArgumentRecorderPath is null || startupOptions.CommandArgumentAskPath || !skipAsking)
                    {
                        this.workDirectoryLoader.Write(new() { Path = path, SkipAsking = dialog.SkipAsking });
                    }
                    error = dialog.Error = WorkDirectorySelectorDialog.WorkDirectorySelectorDialogError.None;
                    return (config, false, false);
                }
                catch (Exception)
                {
                    loop = true;
                }
            }
            return (null, runToolbox, exit);
        }

        private void RootPage_Loaded(object sender, RoutedEventArgs e)
        {
            this.workDirectorySelectorDialog.Owner = Window.GetWindow(this);
            this.LoopAskPath(async (cfg, runToolbox, exit, token) =>
            {
                if (exit)
                {
                    startupOptions.ShutdownApp(0, this.Dispatcher);
                    return;
                }
                if (cfg is null)
                {
                    if (!runToolbox)
                    {
                        startupOptions.ShutdownApp(-1, this.Dispatcher);
                        return;
                    }
                }
                else
                {
                    // 检查已经在同目录运行的其他进程
                    if (!SingleInstance.CheckMutex(cfg.Global.WorkDirectory!))
                    {
                        // 有已经在其他目录运行的进程，已经通知该进程，本进程退出
                        startupOptions.ShutdownApp(-2, this.Dispatcher);
                        return;
                    }
                }

                

                this.lifetimeScopeChild = this.lifetimeScope.BeginLifetimeScope(nameof(RootPage), builder =>
                {
                    if (cfg is not null)
                    {
                        builder
                            .Populate(new ServiceCollection()
                            .AddFlv().AddRecorder().AddRecorderConfig(cfg)
                            .AddScoped<IPage, SettingsPage>()
                            .AddScoped<IPage, RoomListPage>()
                            .AddScoped<IPage, AdvancedSettingsPage>()
                        // Remote exec issue
                        // .AddScoped<IPage, AnnouncementPage>()
                        );
                    }
                });

                this.Dispatcher.Invoke(() =>
                {
                    
                    (Window.GetWindow(this) as MainWindow)!.HideToTray = true;
                    var allPages = this.lifetimeScopeChild.Resolve<IEnumerable<IPage>>();
                    foreach (var page in allPages)
                    {
                        this.pages.Add(page);
                    }

                    foreach (var item in this.NaviViews.MenuItems.OfType<NavigationViewItem>().Concat(this.NaviViews.FooterMenuItems.OfType<NavigationViewItem>()))
                    {
                        if (item.Tag is string tag)
                        {
                            var hasPage = pages.Any(p => p.PageName == tag);
                            if (!item.IsEnabled)
                            {
                                item.IsEnabled = hasPage;
                            }
                            if (tag != "AdvancedSettingsPage")
                            {
                                item.Visibility = hasPage ? Visibility.Visible : Visibility.Collapsed;
                            }
                        }
                    }
                });

                await Task.Delay(150, token);
                this.Dispatcher.Invoke(() =>
                {
                    if (cfg is not null)
                    {
                        this.RoomListPageNavigationViewItem.IsSelected = true;
                    }

                    if (startupOptions.CommandArgumentHide)
                        Window.GetWindow(this).WindowState = WindowState.Minimized;
                });
            });

        }


        private void NavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            var frame = sender.Content as ModernWpf.Controls.Frame;

            this.SettingsClickCount = 0;
            if (args.IsSettingsSelected)
            {
                var settingPage = pages.FirstOrDefault(x => x.PageName == "SettingsPage");
                if (settingPage is not null)
                {
                    frame.NavigateContent(settingPage, settingPage, this.transitionInfo);
                }
                return;
            }

            var selectedItem = args.SelectedItem as NavigationViewItem;
            var selectedItemTag = selectedItem?.Tag as string;
            if (selectedItemTag?.StartsWith("http") == true)
            {
                try
                {
                    frame.Navigate(new Uri(selectedItemTag), null, this.transitionInfo);
                }
                catch (Exception)
                {
                }
                return;
            }
            var page = this.pages.FirstOrDefault(x => x.PageName == selectedItemTag);
            if (page is not null)
            {
                frame.NavigateContent(page, null, this.transitionInfo);
            }
        }

        private void NavigationViewItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!this.AdvancedSettingsPageItem.IsEnabled)
            {
                return;
            }
            if (++this.SettingsClickCount > 1)
            {
                this.SettingsClickCount = 0;
                this.AdvancedSettingsPageItem.Visibility = this.AdvancedSettingsPageItem.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void MainFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            try
            {
                if (sender is not ModernWpf.Controls.Frame frame) return;

                while (frame.BackStackDepth > 0)
                {
                    frame.RemoveBackEntry();
                }
            }
            catch (Exception)
            {
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    lifetimeScopeChild?.Dispose();
                }

                lifetimeScopeChild = null;
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            //GC.SuppressFinalize(this);
        }
    }
}
