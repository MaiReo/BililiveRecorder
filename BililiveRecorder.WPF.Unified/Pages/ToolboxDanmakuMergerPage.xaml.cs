using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BililiveRecorder.ToolBox;
using BililiveRecorder.ToolBox.Tool.DanmakuMerger;
using BililiveRecorder.ToolBox.Tool.DanmakuStartTime;
using BililiveRecorder.WPF.Controls;
using BililiveRecorder.WPF.Models;
using Microsoft.Win32;
using Serilog;
using WPFLocalizeExtension.Extensions;

#nullable enable
namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for ToolboxDanmakuMergerPage.xaml
    /// </summary>
    public partial class ToolboxDanmakuMergerPage : IPage
    {
        private static readonly ILogger logger = Log.ForContext<ToolboxDanmakuMergerPage>();
        private static readonly string DesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        private readonly ObservableCollection<DanmakuFileWithOffset> Files = new();

        public ToolboxDanmakuMergerPage()
        {
            this.InitializeComponent();
            this.listView.ItemsSource = this.Files;
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            var f = (DanmakuFileWithOffset)b.DataContext;
            this.Files.Remove(f);

            this.CalculateOffsets();
        }

        private async void DragDrop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    await this.AddFilesAsync(files.Where(x => x.EndsWith(".xml", StringComparison.InvariantCultureIgnoreCase)).ToArray());
                }
            }
            catch (Exception)
            { }
        }

        private async void AddFile_Click(object sender, RoutedEventArgs e)
        {
            var d = new OpenFileDialog
            {
                Title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_Merge_OpenFileDialogTitle"),

                Filter = "*.xml",
                CheckFileExists = true,
                CheckPathExists = true,
                ValidateNames = true,
                InitialDirectory = DesktopPath,
                DefaultExt = ".xml",
                Multiselect = true,
            };

            if (d.ShowDialog() != true)
                return;

            await this.AddFilesAsync(d.FileNames.ToArray());
        }

        private async Task AddFilesAsync(string[] paths)
        {
            var req = new DanmakuStartTimeRequest { Inputs = paths };
            var handler = new DanmakuStartTimeHandler();
            var resp = await handler.Handle(req, default, default).ConfigureAwait(true);

            if (resp.Status != ResponseStatus.OK || resp.Data is null)
                return;

            var toBeAdded = resp.Data.StartTimes.Select(x => new DanmakuFileWithOffset(x.Path) { StartTime = x.StartTime });
            foreach (var file in toBeAdded)
                this.Files.Add(file);

            _ = this.Dispatcher.BeginInvoke(DispatcherPriority.Normal, (Action)this.CalculateOffsets);
        }

        private void CalculateOffsets()
        {
            if (this.Files.Count == 0)
                return;

            var minTime = this.Files.Min(x => x.StartTime);

            foreach (var item in this.Files)
            {
                item.Offset = (int)(item.StartTime - minTime).TotalSeconds;
            }

            this.listView.DataContext = null;
            this.listView.DataContext = this.Files;
        }

        private async void Merge_Click(object sender, RoutedEventArgs e)
        {
            AutoFixProgressDialog? progressDialog = null;

            try
            {
                var inputPaths = this.Files.Distinct().ToArray();

                if (inputPaths.Length < 2)
                {
                    MessageBox.Show(LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_Merge_Error_AtLeastTwo"),
                        LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_Merge_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                logger.Debug("合并弹幕文件 {Paths}", inputPaths);

                progressDialog = new AutoFixProgressDialog()
                {
                    CancelButtonVisibility = Visibility.Collapsed,
                    CancellationTokenSource = new CancellationTokenSource()
                };
                var token = progressDialog.CancellationTokenSource.Token;
                var showTask = progressDialog.ShowAsync();

                string? outputPath;
                {
                    var fileDialog = new SaveFileDialog()
                    {
                        Title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_SelectOutputDialog_Title"),
                        AddExtension = true,
                        DefaultExt = "xml",
                        DereferenceLinks = true,
                        CheckFileExists = true,
                        CheckPathExists = true,
                        ValidateNames = true,
                        InitialDirectory = Path.GetDirectoryName(inputPaths[0].Path),
                        CreatePrompt = true,
                        OverwritePrompt = false,
                        Filter = "*.xml",
                    };

                    if (fileDialog.ShowDialog() == true)
                        outputPath = fileDialog.FileName;
                    else
                        return;
                }

                var req = new DanmakuMergerRequest
                {
                    Inputs = inputPaths.Select(x => x.Path).ToArray(),
                    Offsets = inputPaths.Select(x => x.Offset).ToArray(),
                    Output = outputPath,
                };

                var handler = new DanmakuMergerHandler();

                var resp = await handler.Handle(req, token, async p =>
                {
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.Progress = (int)(p * 98d);
                    });
                }).ConfigureAwait(true);

                logger.Debug("弹幕合并结果 {@Response}", resp);

                if (resp.Status != ResponseStatus.Cancelled && resp.Status != ResponseStatus.OK)
                {
                    logger.Warning(resp.Exception, "弹幕合并时发生错误 (@Status)", resp.Status);
                    await Task.Run(() => ShowErrorMessageBox(resp)).ConfigureAwait(true);
                }
                else
                {
                    this.Files.Clear();
                }

                progressDialog.Hide();
                await showTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "弹幕合并时发生未处理的错误");
            }
            finally
            {
                try
                {
                    _ = this.Dispatcher.BeginInvoke((Action)(() => progressDialog?.Hide()));
                    progressDialog?.CancellationTokenSource?.Cancel();
                }
                catch (Exception) { }
            }
        }

        private static void ShowErrorMessageBox<T>(CommandResponse<T> resp) where T : IResponseData
        {
            var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_Error_Title");
            var type = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_Error_Type_" + resp.Status.ToString());
            MessageBox.Show($"{type}\n{resp.ErrorMessage}", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
