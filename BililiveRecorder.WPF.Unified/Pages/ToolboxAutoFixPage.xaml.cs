using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BililiveRecorder.ToolBox;
using BililiveRecorder.ToolBox.Tool.Analyze;
using BililiveRecorder.ToolBox.Tool.Export;
using BililiveRecorder.ToolBox.Tool.Fix;
using BililiveRecorder.WPF.Controls;
using Microsoft.Win32;
using Serilog;
using WPFLocalizeExtension.Extensions;

#nullable enable
namespace BililiveRecorder.WPF.Pages
{
    /// <summary>
    /// Interaction logic for ToolboxAutoFixPage.xaml
    /// </summary>
    public partial class ToolboxAutoFixPage : IPage
    {
        private static readonly ILogger logger = Log.ForContext<ToolboxAutoFixPage>();
        private readonly SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        public ToolboxAutoFixPage()
        {
            this.InitializeComponent();
        }

        private void SelectFile_Button_Click(object sender, RoutedEventArgs e)
        {
            var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_SelectInputDialog_Title");
            var fileDialog = new OpenFileDialog()
            {
                Title = title,
                Multiselect = false,
                DereferenceLinks = true,
                ValidateNames = true,
                CheckFileExists = true,
                CheckPathExists = true,
                Filter = ".flv,.xml,.gz",
                DefaultExt = ".flv"

            };
            if (fileDialog.ShowDialog() == true)
            {
                this.FileNameTextBox.Text = fileDialog.FileName;
            }
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void Fix_Button_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            AutoFixProgressDialog? progressDialog = null;

            if (!this.semaphoreSlim.Wait(0))
                return;

            try
            {
                var inputPath = this.FileNameTextBox.Text;
                if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                    return;

                logger.Debug("修复文件 {Path}", inputPath);

                progressDialog = new AutoFixProgressDialog()
                {
                    CancelButtonVisibility = Visibility.Visible,
                    CancellationTokenSource = new CancellationTokenSource()
                };
                var token = progressDialog.CancellationTokenSource.Token;
                var showTask = progressDialog.ShowAsync();

                string? output_path;
                {
                    var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_SelectOutputDialog_Title");
                    var fileDialog = new SaveFileDialog()
                    {
                        Title = title,
                        CheckPathExists = true,
                        CheckFileExists = true,
                        ValidateNames = true,
                        DereferenceLinks = true,
                        CreatePrompt = true,
                        OverwritePrompt = false,
                        InitialDirectory = Path.GetDirectoryName(inputPath),
                        FileName = Path.GetFileName(inputPath),
                    };
                    if (fileDialog.ShowDialog() == true)
                        output_path = fileDialog.FileName;
                    else
                        return;
                }

                var req = new FixRequest
                {
                    Input = inputPath,
                    OutputBase = output_path,
                };

                var handler = new FixHandler();

                var resp = await handler.Handle(req, token, async p =>
                {
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.Progress = (int)(p * 98d);
                    });
                }).ConfigureAwait(true);

                logger.Debug("修复结果 {@Response}", resp);

                if (resp.Status != ResponseStatus.Cancelled && resp.Status != ResponseStatus.OK)
                {
                    logger.Warning(resp.Exception, "修复时发生错误 (@Status)", resp.Status);
                    await Task.Run(() => ShowErrorMessageBox(resp)).ConfigureAwait(true);
                }

                progressDialog.Hide();
                await showTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "修复时发生未处理的错误");
            }
            finally
            {
                try
                {
                    _ = this.Dispatcher.BeginInvoke((Action)(() => progressDialog?.Hide()));
                    progressDialog?.CancellationTokenSource?.Cancel();
                }
                catch (Exception) { }

                this.semaphoreSlim.Release();
            }
        }

        private static void ShowErrorMessageBox<T>(CommandResponse<T> resp) where T : IResponseData
        {
            var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_Error_Title");
            var type = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_Error_Type_" + resp.Status.ToString());
            MessageBox.Show($"{type}\n{resp.ErrorMessage}", title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void Analyze_Button_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            AutoFixProgressDialog? progressDialog = null;

            if (!this.semaphoreSlim.Wait(0))
                return;

            try
            {
                var inputPath = this.FileNameTextBox.Text;
                if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                    return;

                logger.Debug("分析文件 {Path}", inputPath);

                progressDialog = new AutoFixProgressDialog()
                {
                    CancelButtonVisibility = Visibility.Visible,
                    CancellationTokenSource = new CancellationTokenSource()
                };
                var token = progressDialog.CancellationTokenSource.Token;
                var showTask = progressDialog.ShowAsync();

                var req = new AnalyzeRequest
                {
                    Input = inputPath
                };

                var handler = new AnalyzeHandler();

                var resp = await handler.Handle(req, token, async p =>
                {
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.Progress = (int)(p * 98d);
                    });
                }).ConfigureAwait(true);

                logger.Debug("分析结果 {@Response}", resp);

                if (resp.Status != ResponseStatus.Cancelled)
                {
                    if (resp.Status != ResponseStatus.OK)
                    {
                        logger.Warning(resp.Exception, "分析时发生错误 (@Status)", resp.Status);
                        await Task.Run(() => ShowErrorMessageBox(resp)).ConfigureAwait(true);
                    }
                    else
                    {
                        this.analyzeResultDisplayArea.DataContext = resp.Data;
                    }
                }

                progressDialog.Hide();
                await showTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "分析时发生未处理的错误");
            }
            finally
            {
                try
                {
                    _ = this.Dispatcher.BeginInvoke((Action)(() => progressDialog?.Hide()));
                    progressDialog?.CancellationTokenSource?.Cancel();
                }
                catch (Exception) { }

                this.semaphoreSlim.Release();
            }
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void Export_Button_Click(object sender, RoutedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            AutoFixProgressDialog? progressDialog = null;

            if (!this.semaphoreSlim.Wait(0))
                return;

            try
            {
                var inputPath = this.FileNameTextBox.Text;
                if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                    return;

                logger.Debug("导出文件 {Path}", inputPath);

                progressDialog = new AutoFixProgressDialog()
                {
                    CancelButtonVisibility = Visibility.Visible,
                    CancellationTokenSource = new CancellationTokenSource()
                };
                var token = progressDialog.CancellationTokenSource.Token;
                var showTask = progressDialog.ShowAsync();

                var outputPath = string.Empty;
                {
                    var title = LocExtension.GetLocalizedValue<string>("BililiveRecorder.WPF:Strings:Toolbox_AutoFix_SelectOutputDialog_Title");
                    var fileDialog = new SaveFileDialog()
                    {
                        Title = title,
                        CheckPathExists = true,
                        ValidateNames = true,
                        DereferenceLinks = true,
                        CreatePrompt = true,
                        OverwritePrompt = true,
                        InitialDirectory = Path.GetDirectoryName(inputPath),
                        FileName = Path.GetFileNameWithoutExtension(inputPath) + ".brec.xml.gz"
                    };
                    if (fileDialog.ShowDialog() == true)
                        outputPath = fileDialog.FileName;
                    else
                        return;
                }

                var req = new ExportRequest
                {
                    Input = inputPath,
                    Output = outputPath
                };

                var handler = new ExportHandler();

                var resp = await handler.Handle(req, token, async p =>
                {
                    await this.Dispatcher.InvokeAsync(() =>
                    {
                        progressDialog.Progress = (int)(p * 95d);
                    });
                }).ConfigureAwait(true);

                logger.Debug("导出分析数据结果 {@Response}", resp);

                if (resp.Status != ResponseStatus.Cancelled && resp.Status != ResponseStatus.OK)
                {
                    logger.Warning(resp.Exception, "导出分析数据时发生错误 (@Status)", resp.Status);
                    await Task.Run(() => ShowErrorMessageBox(resp)).ConfigureAwait(true);
                }

                progressDialog.Hide();
                await showTask.ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "导出时发生未处理的错误");
            }
            finally
            {
                try
                {
                    _ = this.Dispatcher.BeginInvoke((Action)(() => progressDialog?.Hide()));
                    progressDialog?.CancellationTokenSource?.Cancel();
                }
                catch (Exception) { }

                this.semaphoreSlim.Release();
            }
        }

        private void FileNameTextBox_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    this.FileNameTextBox.Text = files[0];
                }
            }
            catch (Exception)
            { }
        }
    }
}
