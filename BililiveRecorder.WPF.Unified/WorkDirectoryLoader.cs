using System;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

#nullable enable
namespace BililiveRecorder.WPF
{
    public class WorkDirectoryLoader
    {
        private const string fileName = "path.json";
        private readonly string basePath;
        private readonly string filePath;
        private readonly StartupOptions startupOptions;
        private readonly ILogger<WorkDirectoryLoader> logger;

        public WorkDirectoryLoader(
            StartupOptions startupOptions,
            ILogger<WorkDirectoryLoader> logger)
        {
            this.startupOptions = startupOptions;
            this.logger = logger;
            var exePath = typeof(App).Assembly.Location;
            this.basePath = (Path.GetDirectoryName(exePath == string.Empty ? null : exePath) ?? AppContext.BaseDirectory);

            if (Regex.IsMatch(this.basePath, @"^.*\\app-\d+\.\d+\.\d+\\?$") && File.Exists(Path.Combine(this.basePath, "..", "Update.exe")))
                this.basePath = Path.Combine(this.basePath, "..");

            this.basePath = Path.GetFullPath(this.basePath);
            this.filePath = Path.Combine(this.basePath, fileName);
        }

        public WorkDirectoryData Read()
        {
            var pathFromJsonFile = string.Empty;
            var skipAskingFromJsonFile = false;
            try
            {
                if (!File.Exists(this.filePath))
                {
                    logger.LogDebug("Path file {FilePath} does not exist", this.filePath);
                }
                else
                {
                    logger.LogDebug("Reading path file from {FilePath}.", this.filePath);
                    var str = File.ReadAllText(this.filePath);
                    logger.LogDebug("Path file content: {Content}", str);
                    var obj = JsonConvert.DeserializeObject<WorkDirectoryData>(str) ?? new WorkDirectoryData();
                    pathFromJsonFile = obj.Path;
                    skipAskingFromJsonFile = obj.SkipAsking;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error reading path file");
            }
            if (startupOptions.CommandArgumentAskPath)
            {
                skipAskingFromJsonFile = false;
            }
            var data = new WorkDirectoryData
            {
                Path = startupOptions.CommandArgumentRecorderPath ?? pathFromJsonFile ?? string.Empty,
                SkipAsking = skipAskingFromJsonFile,
            };
            this.startupOptions.Data.SkipAsking = data.SkipAsking;
            this.startupOptions.Data.Path = data.Path;
            return data;
        }

        public void Write(WorkDirectoryData data)
        {
            if (!string.IsNullOrWhiteSpace(startupOptions.CommandArgumentRecorderPath) && startupOptions.CommandArgumentRecorderPath == data.Path)
            {
                logger.LogInformation("Skip writing path file while path was accepted from CLI", this.filePath);
                return;
            }
            try
            {
                logger.LogDebug("Writing path file at {FilePath}", this.filePath);
                var str = JsonConvert.SerializeObject(data);
                Core.Config.ConfigParser.WriteAllTextWithBackup(this.filePath, str);
                this.startupOptions.Data.SkipAsking = data.SkipAsking;
                this.startupOptions.Data.Path = data.Path;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error writing path file at {FilePath}", this.filePath);
            }
        }

        public class WorkDirectoryData
        {
            public string Path { get; set; } = string.Empty;
            public bool SkipAsking { get; set; }
        }
    }
}
