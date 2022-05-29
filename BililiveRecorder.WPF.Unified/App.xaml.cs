using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BililiveRecorder.DependencyInjection;
using BililiveRecorder.ToolBox;
using BililiveRecorder.WPF.Controls;
using BililiveRecorder.WPF.Jobs;
using BililiveRecorder.WPF.Models;
using BililiveRecorder.WPF.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;

namespace BililiveRecorder.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const int CODE__WPF = 0x5F_57_50_46;

        internal static readonly bool DebugMode;

        internal static readonly LoggingLevelSwitch levelSwitchGlobal;
        internal static readonly LoggingLevelSwitch levelSwitchConsole;

        static App()
        {
#if DEBUG
            DebugMode = Debugger.IsAttached;
            if (DebugMode) levelSwitchGlobal = new(Serilog.Events.LogEventLevel.Verbose);
#endif
            levelSwitchGlobal = new(Serilog.Events.LogEventLevel.Debug);
            levelSwitchConsole = new(Serilog.Events.LogEventLevel.Error);
        }

        private IHost? _genericHost;

        private StartupOptions? _startupOptions;

        private void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<StartupOptions>();
            services.AddSingleton<ToolCommand>();
            services.AddSingleton<MainWindow>();
            services.AddTransient<WorkDirectoryLoader>();
            services.AddTransient<WorkDirectorySelectorDialog>();

            services.AddSingleton<IRootPage, RootPage>();
            services.AddSingleton<IPage, AboutPage>();
            services.AddSingleton<AboutModel>();

            services.AddSingleton<IPage, ToolboxAutoFixPage>();
            services.AddSingleton<IPage, ToolboxDanmakuMergerPage>();
            services.AddSingleton<IPage, ToolboxRemuxPage>();

            // services.AddSingleton<AnnouncementDataModel>();

            services.AddHostedService<OnStartJob>();
            // services.AddHostedService<LoadAnnouncementJob>();

            services.AddHttpClient(nameof(LoadAnnouncementJob), client =>
            {
                client.DefaultRequestHeaders.Add("User-Agent", $"BililiveRecorder/{GitVersionInformation.FullSemVer}");
            });

            //TODO: Port SleepBlocker as BackgroundService here
            //TODO: Port Updater as BackgroundJob here
        }

        protected override void OnStartup(StartupEventArgs e)
        {

            base.OnStartup(e);
            // Handle CLI
            var logger = Log.Logger = GetLogger();
            _genericHost = Host.CreateDefaultBuilder(e.Args)
                .ConfigureServices((services) =>
                {
                    services.AddSingleton(logger);
                    this.ConfigureServices(services);
                })
                .ConfigureLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddSerilog(dispose: true);
                })
                .UseServiceProviderFactory(new Autofac.Extensions.DependencyInjection.AutofacServiceProviderFactory())
                .Build();

            _startupOptions = _genericHost.Services.GetRequiredService<StartupOptions>();
            var exitCode = BuildCommand(
                _startupOptions,
                _genericHost.Services.GetRequiredService<ToolCommand>()
            ).Invoke(e.Args);

            if (exitCode != CODE__WPF)
            {
                this.Shutdown();
                return;
            }

            this.DispatcherUnhandledException += (_, e) => logger.Fatal(e.Exception, "Unhandled exception from Application.DispatcherUnhandledException");

            TaskScheduler.UnobservedTaskException += (_, e) => logger.Fatal(e.Exception, "Unobserved exception from TaskScheduler.UnobservedTaskException");

            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    logger.Fatal(ex, "Unhandled exception from AppDomain.UnhandledException");
                }
            };

            var aboutModel = _genericHost.Services.GetRequiredService<AboutModel>();

            var loadedAsmList = AppDomain.CurrentDomain.GetAssemblies();

            var metadataAttrType = typeof(AssemblyMetadataAttribute);

            void addLabel(Assembly asm)
            {
                var metadataList = asm.GetCustomAttributes<AssemblyMetadataAttribute>().ToList();
                if (asm.ManifestModule.Name == "<In Memory Module>")
                {
                    return;
                }
                if (metadataList.Any(attr => attr.Key == ".NETFrameworkAssembly"))
                {
                    return;
                }
                if (metadataList.Any(attr => attr.Key == "RepositoryUrl" && attr.Value?.StartsWith("https://github.com/dotnet/wpf") == true))
                {
                    return;
                }
                var projectLibNames = new[] { nameof(BililiveRecorder) };
                var systemLibNames = new[] { "PresentationCore", "PresentationFramework", "WindowsBase", "DirectWriteForwarder", };
                var sysProductNames = new[] { "C#/WinRT", "Visual Studio", "Windows SDK" };
                var name = asm.GetName();
                if (projectLibNames.Any(x => name.Name!.StartsWith(x)) || systemLibNames.Any(x => name.Name!.Contains(x)))
                {
                    return;
                }
                var product = asm.GetCustomAttribute<AssemblyProductAttribute>();
                if (sysProductNames.Any(x => product?.Product?.Contains(x) == true))
                {
                    return;
                }

                var title = asm.GetCustomAttribute<AssemblyTitleAttribute>();
                var version = asm.GetCustomAttribute<AssemblyFileVersionAttribute>();
                var copyright = asm.GetCustomAttribute<AssemblyCopyrightAttribute>();
                var versionText = version?.Version ?? name.Version.ToString();
                var infoText = $"{name.Name} {versionText} {copyright?.Copyright}".TrimEnd();
                if (!aboutModel!.Libraries.Contains(infoText))
                    aboutModel!.Libraries.Add(infoText);
            };

            foreach (var asm in loadedAsmList)
            {
                addLabel(asm);
            }
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) =>
                    {
                        if (Dispatcher.Thread != Thread.CurrentThread)
                        {
                            this.Dispatcher.Invoke(() => addLabel(e.LoadedAssembly));
                        }
                        else
                        {
                            addLabel(e.LoadedAssembly);
                        }
                    };

            _genericHost.Services.GetRequiredService<WorkDirectoryLoader>().Read();

            this.MainWindow = _genericHost.Services.GetRequiredService<MainWindow>();
            this.MainWindow!.Show();
            _genericHost.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            SingleInstance.Cleanup();
            _genericHost?.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            _genericHost?.Dispose();
            _genericHost = null;
            base.OnExit(e);
        }

        private static RootCommand BuildCommand(StartupOptions startOptions, params Command[] commands)
        {
            int RunWpfHandler(string? path, bool squirrelFirstrun, bool askPath, bool hide)
            {
                startOptions.CommandArgumentRecorderPath = path;
                startOptions.CommandArgumentFirstRun = squirrelFirstrun;
                startOptions.CommandArgumentAskPath = askPath;
                startOptions.CommandArgumentHide = hide;
                return CODE__WPF;
            }
            var run = new Command("run", "Run BililiveRecorder at path")
            {
                new Argument<string?>("path", () => null, "Work directory"),
                new Option<bool>("--ask-path", "Ask path in GUI even when \"don't ask again\" is selected before."),
                new Option<bool>("--hide", "Minimize to tray")
            };
            run.Handler = CommandHandler.Create((string? path, bool askPath, bool hide) => RunWpfHandler(path: path, squirrelFirstrun: false, askPath: askPath, hide: hide));

            var root = new RootCommand("")
            {
                run,
                new Option<bool>("--squirrel-firstrun")
                {
                    IsHidden = true
                },
            };
            foreach (var command in commands)
            {
                root.Add(command);
            }

            root.Handler = CommandHandler.Create((bool squirrelFirstrun) => RunWpfHandler(path: null, squirrelFirstrun: squirrelFirstrun, askPath: false, hide: false));
            return root;
        }

        private static Serilog.ILogger GetLogger()
        {
            return new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitchGlobal)
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithThreadName()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .Destructure.ByTransforming<Flv.Xml.XmlFlvFile.XmlFlvFileMeta>(x => new
            {
                x.Version,
                x.ExportTime,
                x.FileSize,
                x.FileCreationTime,
                x.FileModificationTime,
            })
            .WriteTo.Async(x => x.Console(levelSwitch: levelSwitchConsole))

#if DEBUG
            .WriteTo.Debug()
#endif
            .WriteTo.Sink<WpfLogEventSink>(Serilog.Events.LogEventLevel.
#if DEBUG
                Debug
#else
              Information
#endif
            )
            .WriteTo.Async(x => x.File(new CompactJsonFormatter(), "./logs/bilirec.txt", shared: true, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true))
            // Unoffical build, omit
            //            .WriteTo.Sentry(o =>
            //            {
            //                o.Dsn = "https://38036b2031474b8ba0a728ac2a961cfa@o210546.ingest.sentry.io/5556540";
            //                o.SendDefaultPii = true;
            //                o.IsGlobalModeEnabled = true;
            //                o.DisableAppDomainUnhandledExceptionCapture();
            //                o.DisableTaskUnobservedTaskExceptionCapture();
            //                o.AddExceptionFilterForType<System.Net.Http.HttpRequestException>();

            //                o.TextFormatter = new MessageTemplateTextFormatter("[{RoomId}] {Message}{NewLine}{Exception}{@ExceptionDetail:j}");

            //                o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Debug;
            //                o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;

            //#if DEBUG
            //                o.Environment = "debug-build";
            //#else
            //                o.Environment = "release-build";
            //#endif
            //            })
            .CreateLogger();
        }
    }
}
