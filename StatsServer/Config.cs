using System;
using System.IO;

namespace avaness.StatsServer
{
    public static class Config
    {
        private static readonly string UserDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static readonly string BackupDir;
        public static readonly string DataDir;
        public static readonly int SavePeriod;

        public static string PlayerConsentsPath => Path.Combine(DataDir, "PlayerConsents.json");
        public static string PluginStatsDir => Path.Combine(DataDir, "PluginStats");
        public static string PluginMapPath => Path.Combine(DataDir, "Plugins.json");
        public static string VotingTokensPath => Path.Combine(DataDir, "VotingTokens.json");
        public static string RequestCountsPath => Path.Combine(DataDir, "RequestCounts.json");
        public static string PlayersLastSeenPath => Path.Combine(DataDir, "PlayersLastSeen.json");
        public static string UniquePlayerCountsPath => Path.Combine(DataDir, "UniquePlayerCounts.json");
        public static string CanaryPath => Path.Combine(DataDir, "Canary.txt");
        
        static Config()
        {
            BackupDir = Environment.GetEnvironmentVariable("PL_BACKUP_DIR")
                        ?? Path.Combine(UserDir, ".StatsServer", "Backup");

            DataDir = Environment.GetEnvironmentVariable("PL_DATA_DIR")
                      ?? Path.Combine(UserDir, ".StatsServer", "Data");

            var periodText = Environment.GetEnvironmentVariable("PL_SAVE_PERIOD");
            SavePeriod = int.TryParse(periodText, out var period) ? period : 10;
        }
    }
}