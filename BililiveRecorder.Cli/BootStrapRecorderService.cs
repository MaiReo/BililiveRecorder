using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BililiveRecorder.Cli
{
    internal class BootStrapRecorderService : BackgroundService
    {
        private readonly IRecorder recorder;
        private readonly IBackgroundTaskTracer backgroundTaskTracer;
        private readonly ILogger<BootStrapRecorderService> logger;

        public BootStrapRecorderService(
            IRecorder recorder,
            IBackgroundTaskTracer backgroundTaskTracer,
            ILogger<BootStrapRecorderService> logger)
        {
            this.recorder = recorder;
            this.backgroundTaskTracer = backgroundTaskTracer;
            this.logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            recorder.LoadRoom();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            logger.LogWarning("Waiting for traced task to be complete");
            try
            {
                await backgroundTaskTracer.WhenAll(cancellationToken);
            }
            catch (System.Exception ex)
            {
                logger.LogError(ex, "Waiting for traced task error");
            }
            logger.LogWarning("Traced task completed");
        }
    }
}