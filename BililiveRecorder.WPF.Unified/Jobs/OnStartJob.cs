using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BililiveRecorder.WPF.Jobs
{
    internal class OnStartJob : BackgroundService
    {
        private readonly ILogger<OnStartJob> _logger;

        public OnStartJob(ILogger<OnStartJob> logger) 
        {
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken) 
        {
            await Task.Yield();
            _logger.LogDebug("Starting, Version: {Version}, CurrentDirectory: {CurrentDirectory}, CommandLine: {CommandLine}",
                GitVersionInformation.InformationalVersion,
                Environment.CurrentDirectory,
                Environment.CommandLine);
        }
    }
}
