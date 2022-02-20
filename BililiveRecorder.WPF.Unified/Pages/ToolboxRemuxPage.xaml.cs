using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Win32;
using Serilog;
using WPFLocalizeExtension.Extensions;

namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for ToolboxRemuxPage.xaml
    /// </summary>
    public partial class ToolboxRemuxPage : IPage
    {
        private static readonly ILogger logger = Log.ForContext<ToolboxRemuxPage>();
        private static readonly string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        private static readonly string FFmpegWorkingDirectory;
        private static readonly string FFmpegPath;

        static ToolboxRemuxPage()
        {
            FFmpegWorkingDirectory = Path.Combine(AppContext.BaseDirectory, "lib");
            var isArm64 = RuntimeInformation.OSArchitecture == Architecture.Arm64;
            FFmpegPath = Path.Combine(FFmpegWorkingDirectory, isArm64 ? "ffmpeg-arm64.exe" : "miniffmpeg");

        }

        public ToolboxRemuxPage()
        {
            this.InitializeComponent();
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void RemuxButton_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            try
            {
                await this.RunAsync();
            }
            catch (Exception ex)
            {
                logger.Warning(ex, "转封装时发生未知错误");
            }
        }

        private async Task RunAsync()
        {
            string source, target;

            {

                var d = new OpenFileDialog()
                {
                    Title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_Remux_OpenFileTitle"),
                    InitialDirectory = DesktopPath,
                    DereferenceLinks = true,
                    ValidateNames = true,
                    CheckFileExists = true,
                    CheckPathExists = true,
                    Multiselect = false,
                    DefaultExt = "flv",
                    Filter = "*.flv",
                };

                if (d.ShowDialog() != true)
                    return;

                source = d.FileName;
            }

            {
                var d = new SaveFileDialog()
                {
                    Title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_Remux_SaveFileTitle"),
                    AddExtension = true,
                    DefaultExt = "mp4",
                    CheckPathExists = true,
                    CheckFileExists = true,
                    ValidateNames = true,
                    InitialDirectory = Path.GetDirectoryName(source),
                    FileName = Path.GetFileNameWithoutExtension(source),
                    Filter = "*.mp4",
                };

                if (d.ShowDialog() != true)
                    return;

                target = d.FileName;
            }

            logger.Debug("Remux starting, {Source}, {Target}", source, target);

            var result = await Cli.Wrap(FFmpegPath)
                .WithValidation(CommandResultValidation.None)
                .WithWorkingDirectory(FFmpegWorkingDirectory)
                .WithArguments(new[] { "-hide_banner", "-loglevel", "error", "-y", "-i", source, "-c", "copy", target })
#if DEBUG
                .ExecuteBufferedAsync();
#else
                .ExecuteAsync();
#endif

            logger.Debug("Remux completed {@Result}", result);
        }
    }
}
