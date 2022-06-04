using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Nerdbank.Streams;
using Newtonsoft.Json;
using Serilog;
using Timer = System.Timers.Timer;

namespace BililiveRecorder.Core.Api.Danmaku
{
    internal class DanmakuClient : IDanmakuClient, IDisposable
    {
        private readonly ILogger logger;
        private readonly IDanmakuServerApiClient apiClient;
        private readonly IApplicationLifetimeAccessor applicationLifetimeAccessor;
        private readonly IBackgroundTaskTracer backgroundTaskTracer;
        private readonly Timer timer;
        private readonly SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);

        private Stream? danmakuStream;
        private bool disposedValue;

        public bool Connected => this.danmakuStream != null;

        public event EventHandler<StatusChangedEventArgs>? StatusChanged;
        public event EventHandler<DanmakuReceivedEventArgs>? DanmakuReceived;

        public DanmakuClient(IDanmakuServerApiClient apiClient, ILogger logger, IApplicationLifetimeAccessor applicationLifetimeAccessor,IBackgroundTaskTracer backgroundTaskTracer)
        {
            this.apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            this.applicationLifetimeAccessor = applicationLifetimeAccessor;
            this.backgroundTaskTracer = backgroundTaskTracer;
            this.logger = logger?.ForContext<DanmakuClient>() ?? throw new ArgumentNullException(nameof(logger));

            this.timer = new Timer(interval: 1000 * 30)
            {
                AutoReset = true,
                Enabled = false
            };
            this.timer.Elapsed += this.SendPingMessageTimerCallback;
        }

        public async Task DisconnectAsync()
        {
            try
            {
                await this.semaphoreSlim.WaitAsync(applicationLifetimeAccessor.ApplicationStopping).ConfigureAwait(false);
                this.danmakuStream?.DisposeAsync().ConfigureAwait(false);
                this.danmakuStream = null;

                this.timer.Stop();
            }
            finally
            {
                this.semaphoreSlim.Release();
            }

            StatusChanged?.Invoke(this, StatusChangedEventArgs.False);
        }

        public async Task ConnectAsync(int roomid, CancellationToken cancellationToken)
        {
            if (this.disposedValue || cancellationToken.IsCancellationRequested)
                throw new ObjectDisposedException(nameof(DanmakuClient));

            try
            {
                await this.semaphoreSlim.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (this.danmakuStream != null)
                    return;

                var serverInfo = await this.apiClient.GetDanmakuServerAsync(roomid).ConfigureAwait(false);
                if (serverInfo.Data is null)
                    return;
                serverInfo.Data.ChooseOne(out var host, out var port, out var token);

                this.logger.Debug("Connecting to {Host}:{Port} with a {TokenLength} char long token for room {RoomId}", host, port, token?.Length, roomid);

                if (cancellationToken.IsCancellationRequested)
                    return;

                var tcp = new TcpClient();
                await tcp.ConnectAsync(host, port).ConfigureAwait(false);

                this.danmakuStream = tcp.GetStream();

                await SendHelloAsync(this.danmakuStream, roomid, token!, cancellationToken).ConfigureAwait(false);
                await SendPingAsync(this.danmakuStream, cancellationToken: cancellationToken).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    tcp.Dispose();
                    await this.danmakuStream.DisposeAsync().ConfigureAwait(false);
                    this.danmakuStream = null;
                    return;
                }

                this.timer.Start();

                backgroundTaskTracer.AddTask(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessDataAsync(this.danmakuStream, this.ProcessCommand, cancellationToken).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        this.logger.Debug(ex, "Error running ProcessDataAsync");
                    }

                    try
                    {
                        await this.DisconnectAsync().ConfigureAwait(false);
                    }
                    catch (Exception) { }
                }));
            }
            finally
            {
                this.semaphoreSlim.Release();
            }

            StatusChanged?.Invoke(this, StatusChangedEventArgs.True);
        }

        private void ProcessCommand(string json)
        {
            try
            {
                var d = new DanmakuModel(json);
                DanmakuReceived?.Invoke(this, new DanmakuReceivedEventArgs(d));
            }
            catch (Exception ex)
            {
                this.logger.Warning(ex, "Error running ProcessCommand");
            }
        }

#pragma warning disable VSTHRD100 // Avoid async void methods
        private async void SendPingMessageTimerCallback(object? sender, ElapsedEventArgs e)
#pragma warning restore VSTHRD100 // Avoid async void methods
        {
            if (applicationLifetimeAccessor.ApplicationStopping.IsCancellationRequested)
            {
                return;
            }
            try
            {
                try
                {
                    await this.semaphoreSlim.WaitAsync(applicationLifetimeAccessor.ApplicationStopping).ConfigureAwait(false);
                    if (this.danmakuStream is null)
                        return;

                    await SendPingAsync(this.danmakuStream, applicationLifetimeAccessor.ApplicationStopping).ConfigureAwait(false);
                }
                finally
                {
                    this.semaphoreSlim.Release();
                }
            }
            catch (Exception ex)
            {
                this.logger.Warning(ex, "Error running SendPingMessageTimerCallback");
            }
        }

        #region Dispose

        protected virtual void Dispose(bool disposing)
        {
            if (!this.disposedValue)
            {
                if (disposing)
                {
                    // dispose managed state (managed objects)
                    this.timer.Stop();
                    this.timer.Dispose();
                    this.danmakuStream?.Dispose();
                    this.semaphoreSlim.Dispose();
                }

                // free unmanaged resources (unmanaged objects) and override finalizer
                // set large fields to null
                this.disposedValue = true;
            }
        }

        // override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~DanmakuClient()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            this.Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Send

        private static Task SendHelloAsync(Stream stream, int roomid, string token, CancellationToken cancellationToken) =>
            SendMessageAsync(stream, 7, JsonConvert.SerializeObject(new
            {
                uid = 0,
                roomid = roomid,
                protover = 0,
                platform = "web",
                clientver = "2.6.25",
                type = 2,
                key = token,
            }, Formatting.None), cancellationToken);

        private static Task SendPingAsync(Stream stream, CancellationToken cancellationToken) =>
            SendMessageAsync(stream, 2, cancellationToken: cancellationToken);

        private static async Task SendMessageAsync(Stream stream, int action, string body = "", CancellationToken cancellationToken = default)
        {
            if (stream is null)
                throw new ArgumentNullException(nameof(stream));

            cancellationToken.ThrowIfCancellationRequested();
            var payloadMaxSize = body is null ? 0 : Encoding.UTF8.GetMaxByteCount(body.Length);
            using var payload = MemoryPool<byte>.Shared.Rent(payloadMaxSize);
            var realPayloadSize = Encoding.UTF8.GetBytes(body, payload.Memory.Span);
            const int headerSize = 16;
            var size = realPayloadSize + headerSize;
            // 返回的buffer是会大于给定的size， ArrayPool也一样
            using var buffer = MemoryPool<byte>.Shared.Rent(headerSize);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Memory.Slice(0, 4).Span, (uint)size);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Memory.Slice(4, 2).Span, headerSize);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.Memory.Slice(6, 2).Span, 1);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Memory.Slice(8, 4).Span, (uint)action);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.Memory.Slice(12, 4).Span, 1);

            // cancellationToken触发取消时，数据是继续写入还是丢弃? 给b站服务器发送消息，那寄了就行了
            // 官方代码的buffer.Length 是错的。要用实际数据长度 
            await stream.WriteAsync(buffer.Memory.Slice(0, headerSize), cancellationToken).ConfigureAwait(false);
            if (body is not null && body.Length is not 0)
                await stream.WriteAsync(payload.Memory.Slice(0, realPayloadSize), cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        #endregion

        #region Receive

        private static async Task ProcessDataAsync(Stream stream, Action<string> callback, CancellationToken cancellationToken = default)
        {
            var reader = PipeReader.Create(stream);

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TryParseCommand(ref buffer, callback))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                    break;
            }
            await reader.CompleteAsync();
        }

        private static bool TryParseCommand(ref ReadOnlySequence<byte> buffer, in Action<string> callback)
        {
            if (buffer.Length < 4)
                return false;

            int length;
            {
                var lengthSlice = buffer.Slice(buffer.Start, 4);
                if (lengthSlice.IsSingleSegment)
                {
                    length = BinaryPrimitives.ReadInt32BigEndian(lengthSlice.First.Span);
                }
                else
                {
                    Span<byte> stackBuffer = stackalloc byte[4];
                    lengthSlice.CopyTo(stackBuffer);
                    length = BinaryPrimitives.ReadInt32BigEndian(stackBuffer);
                }
            }

            if (buffer.Length < length)
                return false;

            var headerSlice = buffer.Slice(buffer.Start, 16);
            buffer = buffer.Slice(headerSlice.End);
            var bodySlice = buffer.Slice(buffer.Start, length - 16);
            buffer = buffer.Slice(bodySlice.End);

            DanmakuProtocol header;
            if (headerSlice.IsSingleSegment)
            {
                Parse2Protocol(headerSlice.First.Span, out header);
            }
            else
            {
                Span<byte> stackBuffer = stackalloc byte[16];
                headerSlice.CopyTo(stackBuffer);
                Parse2Protocol(stackBuffer, out header);
            }

            if (header.Version == 2 && header.Action == 5)
                ParseCommandDeflateBody(ref bodySlice, callback);
            else
                ParseCommandNormalBody(ref bodySlice, header.Action, callback);

            return true;
        }

        private static void ParseCommandDeflateBody(ref ReadOnlySequence<byte> buffer, Action<string> callback)
        {
            using var deflate = new DeflateStream(buffer.Slice(2, buffer.End).AsStream(), CompressionMode.Decompress, leaveOpen: false);
            var reader = PipeReader.Create(deflate);
            while (reader.TryRead(out var result))
            {
                var inner_buffer = result.Buffer;

                while (TryParseCommand(ref inner_buffer, callback)) { }

                reader.AdvanceTo(inner_buffer.Start, inner_buffer.End);

                if (result.IsCompleted)
                    break;
            }
            reader.Complete();
        }

        private static void ParseCommandNormalBody(ref ReadOnlySequence<byte> buffer, int action, Action<string> callback)
        {
            switch (action)
            {
                case 5:
                    {
                        if (buffer.Length > int.MaxValue)
                            throw new ArgumentOutOfRangeException("ParseCommandNormalBody buffer length larger than int.MaxValue");

                        var b = ArrayPool<byte>.Shared.Rent((int)buffer.Length);
                        try
                        {
                            buffer.CopyTo(b);
                            var json = Encoding.UTF8.GetString(b, 0, (int)buffer.Length);
                            callback(json);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(b);
                        }
                    }
                    break;
                case 3:

                    break;
                default:
                    break;
            }
        }

        private static unsafe void Parse2Protocol(ReadOnlySpan<byte> buffer, out DanmakuProtocol protocol)
        {
            fixed (byte* ptr = buffer)
            {
                protocol = *(DanmakuProtocol*)ptr;
            }
            protocol.ChangeEndian();
        }

        private struct DanmakuProtocol
        {
            /// <summary>
            /// 消息总长度 (协议头 + 数据长度)
            /// </summary>
            public int PacketLength;
            /// <summary>
            /// 消息头长度 (固定为16[sizeof(DanmakuProtocol)])
            /// </summary>
            public short HeaderLength;
            /// <summary>
            /// 消息版本号
            /// </summary>
            public short Version;
            /// <summary>
            /// 消息类型
            /// </summary>
            public int Action;
            /// <summary>
            /// 参数, 固定为1
            /// </summary>
            public int Parameter;
            /// <summary>
            /// 转为本机字节序
            /// </summary>
            public void ChangeEndian()
            {
                this.PacketLength = IPAddress.HostToNetworkOrder(this.PacketLength);
                this.HeaderLength = IPAddress.HostToNetworkOrder(this.HeaderLength);
                this.Version = IPAddress.HostToNetworkOrder(this.Version);
                this.Action = IPAddress.HostToNetworkOrder(this.Action);
                this.Parameter = IPAddress.HostToNetworkOrder(this.Parameter);
            }
        }

        #endregion
    }
}
