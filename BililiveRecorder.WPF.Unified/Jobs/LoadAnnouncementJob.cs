using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BililiveRecorder.WPF.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BililiveRecorder.WPF.Jobs
{
    public class LoadAnnouncementJob : BackgroundService
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly AnnouncementDataModel model;
        private readonly ILogger<LoadAnnouncementJob> _logger;

        public LoadAnnouncementJob(
            IHttpClientFactory httpClientFactory,
            AnnouncementDataModel model,
            ILogger<LoadAnnouncementJob> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.model = model;
            _logger = logger;
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();
            model.Loading = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                while (!model.Loading)
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }
                await LoadWhileNotSuccessAsync(stoppingToken);
            }
        }

        private async Task LoadWhileNotSuccessAsync(CancellationToken stoppingToken)
        {
            var waitCount = 0;
            _logger.LogDebug("LoadAnnouncementJob:");
            model.Loading = true;
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogDebug("LoadAnnouncementJob: {TryTimes} wait for next exec", waitCount);
                await Task.Delay(8 << waitCount, stoppingToken);
                _logger.LogDebug("LoadAnnouncementJob: {TryTimes} next exec", waitCount);
                var stopwatch = Stopwatch.StartNew();
                using var http = httpClientFactory.CreateClient(nameof(LoadAnnouncementJob));
                try
                {
                    //var uri = App.DebugMode
                    //    ? $"http://rec.127-0-0-1.nip.io/wpf/announcement.php?c={CultureInfo.CurrentUICulture.Name}"
                    //    : $"https://rec.danmuji.org/wpf/announcement.xml?c={CultureInfo.CurrentUICulture.Name}";

                    var uri = $"https://rec.danmuji.org/wpf/announcement.xml?c={CultureInfo.CurrentUICulture.Name}";

                    using var resp = await http.GetAsync(uri, stoppingToken);
                    var mstream = new MemoryStream();
                    using (var stream = await resp.EnsureSuccessStatusCode().Content.ReadAsStreamAsync(stoppingToken))
                    {
                        await stream.CopyToAsync(mstream, stoppingToken);
                    }
                    mstream.Seek(0, SeekOrigin.Begin);
                    var obj = System.Windows.Markup.XamlReader.Load(mstream);
                    if (obj is UIElement elem)
                    {
                        model.Content = elem;
                        model.CacheTime = DateTimeOffset.Now;
                        model.Loading = false;
                        break;
                    }
                    waitCount++;
                }
                catch (Exception)
                {
                    waitCount++;
                }
                finally
                {
                    stopwatch.Stop();
                    _logger.LogDebug("Fetch Announcement data success: {Success}, time elapsed: {Elapsed}, fail times: {FailCount}",
                        model.Content is not null, stopwatch.Elapsed, waitCount);
                    if (!model.Loading)
                    {
                        waitCount = 0;
                    }
                }
            }
        }
    }
}
