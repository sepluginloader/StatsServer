using System;
using System.IO;
using System.IO.Compression;
using avaness.StatsServer.Tools;
using Microsoft.Extensions.Logging;

// ReSharper disable TemplateIsNotCompileTimeConstantProblem

namespace avaness.StatsServer.Persistence
{
    /// <summary>
    /// Background service to periodically saving the database to disk and
    /// making a backups in ZIP format each day
    /// </summary>
    public class PersistenceService : PeriodicTimerService
    {
        private readonly IStatsDatabase statsDatabase;

        public PersistenceService(ILogger<PersistenceService> logger, IStatsDatabase statsDatabase) : base(logger)
        {
            Name = GetType().Name;
            Period = Config.SavePeriod;

            this.statsDatabase = statsDatabase;
        }

        public override void Dispose()
        {
            statsDatabase.Dispose();
            base.Dispose();
        }

        protected override void DoWork(object state)
        {
            statsDatabase.Save();
            statsDatabase.Canary();
            Backup();
        }

        private void Backup()
        {
            // Daily backup archive file
            var zipPath = Path.Combine(Config.BackupDir, $"PluginLoaderStatsData.{Tools.Tools.FormatDateIso8601(DateTime.Today)}.zip");

            // Make
            if (File.Exists(zipPath))
                return;

            Directory.CreateDirectory(Config.BackupDir);

            try
            {
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                var dataDir = Config.DataDir;
                foreach (var filePath in Directory.EnumerateFiles(dataDir, "*", SearchOption.AllDirectories))
                {
                    var relativePath = filePath.Substring(dataDir.Length);
                    zip.CreateEntryFromFile(filePath, relativePath, CompressionLevel.Optimal);
                }
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to create database backup archive {zipPath} from data directory {Config.DataDir}");
                try
                {
                    File.Delete(zipPath);
                }
                catch (Exception)
                {
                    // ignored
                }
            }
        }
    }
}