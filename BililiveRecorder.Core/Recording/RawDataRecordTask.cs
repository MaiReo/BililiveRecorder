using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core.Api;
using BililiveRecorder.Core.Event;
using BililiveRecorder.Core.Scripting;
using BililiveRecorder.Core.Templating;
using Serilog;

namespace BililiveRecorder.Core.Recording
{
    internal class RawDataRecordTask : RecordTaskBase
    {
        private RecordFileOpeningEventArgs? fileOpeningEventArgs;
        private readonly IBackgroundTaskTracer backgroundTaskTracer;

        public RawDataRecordTask(IRoom room,
                                 ILogger logger,
                                 IApiClient apiClient,
                                 FileNameGenerator fileNameGenerator,
                                 UserScriptRunner userScriptRunner,
                                 IBackgroundTaskTracer backgroundTaskTracer)
            : base(room: room,
                   logger: logger?.ForContext<RawDataRecordTask>().ForContext(LoggingContext.RoomId, room.RoomConfig.RoomId)!,
                   apiClient: apiClient,
                   fileNameGenerator: fileNameGenerator,
                   userScriptRunner: userScriptRunner)
        {
            this.backgroundTaskTracer = backgroundTaskTracer;
        }

        public override void SplitOutput() { }

        protected override void StartRecordingLoop(Stream stream, CancellationToken cancellationToken)
        {
            var (fullPath, relativePath) = this.CreateFileName();

            try
            {
                var dir = Path.GetDirectoryName(fullPath);
                if (dir is not null)
                    Directory.CreateDirectory(dir);
            }
            catch (Exception) { }

            this.fileOpeningEventArgs = new RecordFileOpeningEventArgs(this.room)
            {
                SessionId = this.SessionId,
                FullPath = fullPath,
                RelativePath = relativePath,
                FileOpenTime = DateTimeOffset.Now,
            };
            this.OnRecordFileOpening(this.fileOpeningEventArgs);

            this.logger.Information("新建录制文件 {Path}", fullPath);

            var file = new FileStream(fullPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete, 1024 * 8, useAsync: true);

            backgroundTaskTracer.AddTask(Task.Run(async () => await this.WriteStreamToFileAsync(stream, file, cancellationToken).ConfigureAwait(false)));
        }

        private async Task ReadStreamToPipeAsync(Stream stream, PipeWriter writer, CancellationToken cancellationToken)
        {
            var minimumBufferSize = 1024 * 1024 * 8;
            Exception? exception = null;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var buffer = writer.GetMemory(minimumBufferSize);
                    var bytesRead = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (bytesRead == 0)
                    {
                        break;
                    }
                    Interlocked.Add(ref this.ioNetworkDownloadedBytes, bytesRead);
                    writer.Advance(bytesRead);
                }
                catch (System.Exception ex)
                {
                    exception = ex;
                }
                var result = await writer.FlushAsync(cancellationToken);
                if (result.IsCanceled || result.IsCompleted)
                {
                    break;
                }

            }
            await writer.CompleteAsync(exception);
        }

        private async Task ReadPipeToFileAsync(PipeReader reader, FileStream file, CancellationToken cancellationToken)
        {
            var minimumBufferSize = 1024 * 1024 * 8;
            Exception? exception = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await reader.ReadAtLeastAsync(minimumBufferSize, cancellationToken);
                    if (result.IsCanceled)
                    {
                        break;
                    }
                    var buffer = result.Buffer;

                    foreach (var memory in buffer)
                    {
                        this.ioDiskStopwatch.Restart();
                        await file.WriteAsync(memory, cancellationToken).ConfigureAwait(false);
                        this.ioDiskStopwatch.Stop();
                        Interlocked.Add(ref ioDiskWriteDurationTicks, this.ioDiskStopwatch.Elapsed.Ticks);
                        Interlocked.Add(ref this.ioDiskWrittenBytes, memory.Length);
                        this.ioDiskStopwatch.Reset();
                    }
                    reader.AdvanceTo(buffer.Start, buffer.End);
                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                catch (System.Exception ex)
                {
                    exception = ex;
                }
            }
            await reader.CompleteAsync(exception);
        }

        private async Task WriteStreamToFileAsync(Stream stream, FileStream file, CancellationToken cancellationToken)
        {
            try
            {
                var pipe = new Pipe(new(
                    resumeWriterThreshold: 1024 * 1024 * 8 * 2,
                    pauseWriterThreshold: 1024 * 1024 * 8,
                    useSynchronizationContext: false));

                var readingStream = ReadStreamToPipeAsync(stream, pipe.Writer, cancellationToken);
                var writingFile = ReadPipeToFileAsync(pipe.Reader, file, cancellationToken);

                this.timer.Start();

                await Task.WhenAll(readingStream, writingFile).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex)
            {
                this.logger.Debug(ex, "录制被取消");
            }
            catch (IOException ex)
            {
                this.logger.Warning(ex, "录制时发生IO错误");
            }
            catch (Exception ex)
            {
                this.logger.Warning(ex, "录制时发生了错误");
            }
            finally
            {
                this.timer.Stop();

                RecordFileClosedEventArgs? recordFileClosedEvent;
                if (this.fileOpeningEventArgs is { } openingEventArgs)
                    recordFileClosedEvent = new RecordFileClosedEventArgs(this.room)
                    {
                        SessionId = this.SessionId,
                        FullPath = openingEventArgs.FullPath,
                        RelativePath = openingEventArgs.RelativePath,
                        FileOpenTime = openingEventArgs.FileOpenTime,
                        FileCloseTime = DateTimeOffset.Now,
                        Duration = 0,
                        FileSize = file.Length,
                    };
                else
                    recordFileClosedEvent = null;

                try
                {
                    // Writes cached contents without cancellationToken
                    await file.FlushAsync().ConfigureAwait(false);
                    await file.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                { this.logger.Warning(ex, "关闭文件时发生错误"); }

                try
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception) { }

                try
                {
                    if (recordFileClosedEvent is not null)
                        this.OnRecordFileClosed(recordFileClosedEvent);
                }
                catch (Exception ex)
                {
                    this.logger.Warning(ex, "Error calling OnRecordFileClosed");
                }

                this.OnRecordSessionEnded(EventArgs.Empty);

                this.logger.Information("录制结束");
            }
        }
    }
}
