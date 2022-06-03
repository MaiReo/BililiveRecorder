using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Cli.Configure;
using BililiveRecorder.Core;
using BililiveRecorder.Core.Config;
using BililiveRecorder.Core.Config.V3;
using BililiveRecorder.DependencyInjection;
using BililiveRecorder.ToolBox;
using BililiveRecorder.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Formatting.Compact;

namespace BililiveRecorder.Cli
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            ServicePointManager.Expect100Continue = false;

            var cmd_run = new Command("run", "Run BililiveRecorder in standard mode")
            {
                new Option<string?>(new []{ "--web-bind", "--bind", "-b" }, () => null, "Bind address for web api"),
                new Option<LogEventLevel>(new []{ "--loglevel", "--log", "-l" }, () => LogEventLevel.Information, "Minimal log level output to console"),
                new Option<LogEventLevel>(new []{ "--logfilelevel", "--flog" }, () => LogEventLevel.Debug, "Minimal log level output to file"),
                new Argument<string>("path"),
            };
            cmd_run.AddAlias("r");
            cmd_run.Handler = CommandHandler.Create<RunModeArguments>(RunConfigModeAsync);

            var cmd_portable = new Command("portable", "Run BililiveRecorder in config-less mode")
            {
                new Option<string?>(new []{ "--web-bind", "--bind", "-b" }, () =>
                    Environment.GetEnvironmentVariable("BREC_WEB_BIND"), "Bind address for web api"),
                new Option<LogEventLevel>(new []{ "--loglevel", "--log", "-l" }, () => Enum.Parse<LogEventLevel>(
                    Environment.GetEnvironmentVariable("BREC_LOG_LEVEL_CONSOLE") ?? "Information"), "Minimal log level output to console"),

                new Option<LogEventLevel>(new []{ "--logfilelevel", "--flog" }, () => Enum.Parse<LogEventLevel>(
                    Environment.GetEnvironmentVariable("BREC_LOG_LEVEL_FILE") ?? "Debug"), "Minimal log level output to file"),
                new Option<RecordMode>(new []{ "--record-mode", "--mode" }, () => Enum.Parse<RecordMode>(
                    Environment.GetEnvironmentVariable("BREC_RECORD_MODE") ?? "Standard"),
                    "Recording mode"),
                new Option<string?>(new []{ "--cookie", "-c" }, () =>
                    Environment.GetEnvironmentVariable("BREC_COOKIE") ??
                    "Cookie string for api requests"),
                new Option<string>(new []{ "--filename-format", "-f" }, () =>
                    Environment.GetEnvironmentVariable("BREC_FILENAME_FORMAT") ?? "{roomid}/{date}-{time}-{ms}.flv",
                    "File name format"),
                new Option<PortableModeArguments.PortableDanmakuMode>(new []{ "--danmaku", "-d" }, ()=> Enum.Parse<PortableModeArguments.PortableDanmakuMode>(
                    Environment.GetEnvironmentVariable("BREC_DANMAKU_MODE") ?? "0")
                    , "Flags for danmaku recording"),
                new Option<string?>("--webhook-url", () =>
                    Environment.GetEnvironmentVariable("BREC_WEBHOOK_URL"),
                    "URL of webhoook"),
                new Option<string?>("--live-api-host", ()=>
                    Environment.GetEnvironmentVariable("BREC_LIVE_API_HOST")),
                new Argument<string>("output-path", () =>
                    Environment.GetEnvironmentVariable("BREC_WORKDIR")
                    ?? Environment.CurrentDirectory, "Path to save recording files"),
                new Argument<int[]>("room-ids",()=> (Environment.GetEnvironmentVariable("BREC_ROOM_ID_LIST") ?? "1")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
                    .Select(v => int.Parse(v)).ToArray())
            };
            cmd_portable.AddAlias("p");
            cmd_portable.Handler = CommandHandler.Create<PortableModeArguments>(RunPortableModeAsync);

            var root = new RootCommand("A Stream Recorder For Bilibili Live")
            {
                cmd_run,
                cmd_portable,
                new ConfigureCommand(),
                new ToolCommand()
            };
            return await root.InvokeAsync(args);
        }

        private static async Task<int> RunConfigModeAsync(RunModeArguments args)
        {
            bool logToFile = true;
            try
            {
                var isInCluster = k8s.KubernetesClientConfiguration.IsInCluster();
                if (isInCluster)
                {
                    logToFile = false;
                    Log.Warning("Kubernetes detected. Logging to file is disabled.");
                }
            }
            catch
            {
            }
            var path = Path.GetFullPath(args.Path);

            using var logger = BuildLogger(args.LogLevel, args.LogFileLevel, writeToFile: logToFile);
            Log.Logger = logger;
            path = Path.GetFullPath(path);
            var config = ConfigParser.LoadFrom(path);
            if (config is null)
            {
                logger.Error("Initialize Error");
                return -1;
            }

            config.Global.WorkDirectory = path;

            var serviceProvider = BuildServiceProvider(config, logger);

            return await RunRecorderAsync(serviceProvider, args.WebBind);
        }

        private static async Task<int> RunPortableModeAsync(PortableModeArguments args)
        {
            bool logToFile = true;
            try
            {
                var isInCluster = k8s.KubernetesClientConfiguration.IsInCluster();
                if (isInCluster)
                {
                    logToFile = false;
                    Log.Warning("Kubernetes detected. Logging to file is disabled.");
                }
            }
            catch
            {
            }
            using var logger = BuildLogger(args.LogLevel, args.LogFileLevel, writeToFile: logToFile);
            Log.Logger = logger;

            var config = new ConfigV3()
            {
                DisableConfigSave = true,
            };

            {
                var global = config.Global;

                if (!string.IsNullOrWhiteSpace(args.Cookie))
                    global.Cookie = args.Cookie;

                if (!string.IsNullOrWhiteSpace(args.LiveApiHost))
                    global.LiveApiHost = args.LiveApiHost;

                if (!string.IsNullOrWhiteSpace(args.Filename))
                    global.FileNameRecordTemplate = args.Filename;

                if (!string.IsNullOrWhiteSpace(args.WebhookUrl))
                    global.WebHookUrlsV2 = args.WebhookUrl;

                global.RecordMode = args.RecordMode;

                var danmaku = args.Danmaku;
                global.RecordDanmaku = danmaku != PortableModeArguments.PortableDanmakuMode.None;
                global.RecordDanmakuSuperChat = danmaku.HasFlag(PortableModeArguments.PortableDanmakuMode.SuperChat);
                global.RecordDanmakuGuard = danmaku.HasFlag(PortableModeArguments.PortableDanmakuMode.Guard);
                global.RecordDanmakuGift = danmaku.HasFlag(PortableModeArguments.PortableDanmakuMode.Gift);
                global.RecordDanmakuRaw = danmaku.HasFlag(PortableModeArguments.PortableDanmakuMode.RawData);

                global.WorkDirectory = Path.GetFullPath(args.OutputPath);
                config.Rooms = args.RoomIds.Select(x => new RoomConfig { RoomId = x, AutoRecord = true }).ToList();
            }

            var serviceProvider = BuildServiceProvider(config, logger);

            return await RunRecorderAsync(serviceProvider, args.WebBind);
        }

        private static async Task<int> RunRecorderAsync(IServiceProvider serviceProvider, string? webBind)
        {
            var logger = serviceProvider.GetRequiredService<ILogger>();
            IRecorder recorderAccessProxy(IServiceProvider x) => serviceProvider.GetRequiredService<IRecorder>();

            // recorder setup done
            // check if web service required
            IHost? host = null;
            if (webBind is null)
            {
                logger.Information("Web API not enabled");
            }
            else
            {
                var bind = FixBindUrl(webBind, logger);
                logger.Information("Creating web server on {BindAddress}", bind);

                host = new HostBuilder()
                    .UseSerilog(logger: logger)
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton(recorderAccessProxy);
                    })
                    .ConfigureWebHost(webBuilder =>
                    {
                        webBuilder
                        .UseUrls(urls: bind)
                        .UseKestrel(option =>
                        {

                        })
                        .UseStartup<Startup>();
                    })
                    .Build();
            }

            ConsoleCancelEventHandler? p = null;
            using var cts = new CancellationTokenSource();
            p = (sender, e) =>
            {
                logger.Information("Ctrl+C pressed. Exiting");
                Console.CancelKeyPress -= p;
                e.Cancel = true;
                cts.Cancel();
            };
            Console.CancelKeyPress += p;

            IRecorder? recorder = null;

            try
            {
                var token = cts.Token;
                if (host is not null)
                {
                    try
                    {
                        await host.StartAsync(token);
                    }
                    catch (Exception ex)
                    {
                        logger.Fatal(ex, "Failed to start web server.");
                        return -1;
                    }
                    logger.Information("Web host started.");

                    recorder = serviceProvider.GetRequiredService<IRecorder>();

                    await host.WaitForShutdownAsync(token).ConfigureAwait(false);

                    logger.Information("Shutdown in progress.");

                    await host.StopAsync(token).ConfigureAwait(false);
                }
                else
                {
                    recorder = serviceProvider.GetRequiredService<IRecorder>();
                    await Task.Delay(-1, token).ConfigureAwait(false);
                }
            }
            finally
            {
                recorder?.Dispose();
                // TODO 修复这里 Dispose 之后不会停止房间继续初始化
            }
            await Task.Delay(1000 * 3).ConfigureAwait(false);
            return 0;
        }

        private static string FixBindUrl(string bind, ILogger logger)
        {
            if (Regex.IsMatch(bind, @"^\d+$"))
            {
                var result = "http://localhost:" + bind;
                logger.Warning("标准的参数格式为 {Example} 而不是只有端口号，已自动修正为 {BindUrl}", @"http://{接口地址}:{端口号}", result);
                return result;
            }
            else
            if (Regex.IsMatch(bind, @"https?:"))
            {
                return bind;
            }
            else
            {
                var result = "http://" + bind;
                logger.Warning("标准的参数格式为 {Example} 而不是只有 IP 和端口号，已自动修正为 {BindUrl}", @"http://{接口地址}:{端口号}", result);
                return result;
            }
        }

        private static IServiceProvider BuildServiceProvider(ConfigV3 config, ILogger logger) => new ServiceCollection()
            .AddSingleton(logger)
            .AddFlv()
            .AddRecorderConfig(config)
            .AddRecorder()
            .BuildServiceProvider();

        private static Logger BuildLogger(LogEventLevel logLevel, LogEventLevel logFileLevel, bool writeToFile = true, bool writeToFileAsync = true) => new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProcessId()
            .Enrich.WithThreadId()
            .Enrich.WithThreadName()
            .Enrich.FromLogContext()
            .Enrich.WithExceptionDetails()
            .Destructure.AsScalar<IPAddress>()
            .Destructure.ByTransforming<Flv.Xml.XmlFlvFile.XmlFlvFileMeta>(x => new
            {
                x.Version,
                x.ExportTime,
                x.FileSize,
                x.FileCreationTime,
                x.FileModificationTime,
            })
            .WriteTo.Console(restrictedToMinimumLevel: logLevel, outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] [{RoomId}] {Message:lj}{NewLine}{Exception}")
            .When(writeToFile, x =>
                x.When(writeToFileAsync,
                    y => y.WriteTo.Async(a => a.File(
                    new CompactJsonFormatter(), "./logs/bilirec.txt", restrictedToMinimumLevel: logFileLevel, shared: true, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true
                )), y => y.WriteTo.File(
                    new CompactJsonFormatter(), "./logs/bilirec.txt", restrictedToMinimumLevel: logFileLevel, shared: true, rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true))
            )
            .CreateLogger();

        public class RunModeArguments
        {
            public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

            public LogEventLevel LogFileLevel { get; set; } = LogEventLevel.Information;

            public string? WebBind { get; set; } = null;

            public string Path { get; set; } = string.Empty;
        }

        public class PortableModeArguments
        {
            public LogEventLevel LogLevel { get; set; } = LogEventLevel.Information;

            public LogEventLevel LogFileLevel { get; set; } = LogEventLevel.Debug;

            public string? WebBind { get; set; } = null;

            public RecordMode RecordMode { get; set; } = RecordMode.Standard;

            public string OutputPath { get; set; } = string.Empty;

            public string? Cookie { get; set; }

            public string? LiveApiHost { get; set; }

            public string? Filename { get; set; }

            public string? WebhookUrl { get; set; }

            public PortableDanmakuMode Danmaku { get; set; }

            public IEnumerable<int> RoomIds { get; set; } = Enumerable.Empty<int>();

            [Flags]
            public enum PortableDanmakuMode
            {
                None = 0,
                Danmaku = 1 << 0,
                SuperChat = 1 << 1,
                Guard = 1 << 2,
                Gift = 1 << 3,
                RawData = 1 << 4,
                All = Danmaku | SuperChat | Guard | Gift | RawData
            }
        }
    }


    static class ProgramEx
    {
        public static T When<T>(this T @this, bool condition, Func<T, T> funcThen)
        {
            if (condition)
            {
                return funcThen(@this);
            }
            return @this;
        }

        public static TOut When<TIn, TOut>(this TIn @this, bool condition, Func<TIn, TOut> funcThen, Func<TIn, TOut> funcElse)
        {
            if (condition)
            {
                return funcThen(@this);
            }
            else
            {
                return funcElse(@this);
            }
        }
    }
}
