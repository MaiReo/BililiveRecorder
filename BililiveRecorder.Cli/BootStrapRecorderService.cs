using System.Threading;
using System.Threading.Tasks;
using BililiveRecorder.Core;
using Microsoft.Extensions.Hosting;

namespace BililiveRecorder.Cli
{
    internal class BootStrapRecorderService : BackgroundService
    {
        private readonly IRecorder recorder;

        public BootStrapRecorderService(IRecorder recorder)
        {
            this.recorder = recorder;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            recorder.LoadRoom();
        }
    }
}