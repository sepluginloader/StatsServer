using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using avaness.StatsServer.Model;
using Microsoft.AspNetCore.Http;

namespace avaness.StatsServer.Persistence
{
    public class StatsDatabase : IStatsDatabase
    {
        // Players who consented to data processing: playerHash => date
        private readonly Dictionary<string, string> playerConsents;
        
        // Usage statistics and votes for each plugin: pluginId => stats
        private readonly Dictionary<string, PluginStatData> pluginStatsData;

        // Voting tokens sent to each of the players on starting the game
        private readonly Dictionary<string, VotingToken> votingTokens;
        private DateTime nextVotingTokenCleanup = DateTime.Now;
        private const int VotingTokenExpirationDays = 7;
        private const int VotingTokenCleanupPeriodHours = 1;

        // Usage statistics
        private readonly Dictionary<string, Dictionary<string, int>> requestCounts;
        private readonly Dictionary<string, string> playersLastSeen;
        private readonly Dictionary<string, int> uniquePlayerCounts;

        public StatsDatabase()
        {
            playerConsents = Tools.Tools.LoadFromJson<Dictionary<string, string>>(Config.PlayerConsentsPath) ?? new Dictionary<string, string>();
            var pluginMap = Tools.Tools.LoadFromJson<Dictionary<string, string>>(Config.PluginMapPath) ?? new Dictionary<string, string>();
            pluginStatsData = LoadPluginStatsData(pluginMap);

            votingTokens = Tools.Tools.LoadFromJson<Dictionary<string, VotingToken>>(Config.VotingTokensPath) ?? new Dictionary<string, VotingToken>();
            requestCounts = Tools.Tools.LoadFromJson<Dictionary<string, Dictionary<string, int>>>(Config.RequestCountsPath) ?? new Dictionary<string, Dictionary<string, int>>();
            playersLastSeen = Tools.Tools.LoadFromJson<Dictionary<string, string>>(Config.PlayersLastSeenPath) ?? new Dictionary<string, string>();
            uniquePlayerCounts = Tools.Tools.LoadFromJson<Dictionary<string, int>>(Config.UniquePlayerCountsPath) ?? new Dictionary<string, int>();
            
            Canary();
        }

        public void Dispose()
        {
            Save();
        }

        public void Save()
        {
            Directory.CreateDirectory(Config.PluginStatsDir);

            lock (playerConsents)
                Tools.Tools.SaveAsJsonFile(Config.PlayerConsentsPath, playerConsents);

            lock (pluginStatsData)
            {
                SavePluginMap();
                SavePluginStatsData();
            }

            lock (votingTokens)
            {
                CleanupExpiredVotingTokens();
                Tools.Tools.SaveAsJsonFile(Config.VotingTokensPath, votingTokens);
            }

            lock (requestCounts)
                Tools.Tools.SaveAsJsonFile(Config.RequestCountsPath, requestCounts);

            lock (playersLastSeen)
            {
                Tools.Tools.SaveAsJsonFile(Config.PlayersLastSeenPath, playersLastSeen);
                Tools.Tools.SaveAsJsonFile(Config.UniquePlayerCountsPath, uniquePlayerCounts);
            }
        }

        private void SavePluginMap()
        {
            var pluginMap = pluginStatsData.ToDictionary(p => p.Value.FileName, p => p.Value.Id);
            if (pluginMap.Count != pluginStatsData.Count)
                throw new InvalidDataException("Plugin statistics database file name collision (this can only happen with near zero probability)");

            Tools.Tools.SaveAsJsonFile(Config.PluginMapPath, pluginMap);
        }

        private void SavePluginStatsData()
        {
            foreach (var pluginStat in pluginStatsData.Values)
                pluginStat.Save();
        }
        
        [SuppressMessage("ReSharper", "InconsistentlySynchronizedField")]
        public void Canary()
        {
            using var log = new StreamWriter(Config.CanaryPath);

            log.Write($"{DateTime.Now:O}: Canary request received{Environment.NewLine}");
            log.Flush();

            // Make sure the server is not deadlocked
            TimeAcquiringLock(log, pluginStatsData, nameof(pluginStatsData));
            TimeAcquiringLock(log, votingTokens, nameof(votingTokens));
            TimeAcquiringLock(log, requestCounts, nameof(requestCounts));
            TimeAcquiringLock(log, playersLastSeen, nameof(playersLastSeen));
        }

        private static void TimeAcquiringLock(StreamWriter log, object obj, string name)
        {
            double duration;

            log.Write($"{DateTime.Now:O}: lock ({name}) acquiring...{Environment.NewLine}");

            var started = DateTime.Now;
            lock (obj)
            {
                duration = (DateTime.Now - started).TotalMilliseconds;
            }

            log.Write($"{DateTime.Now:O}: lock ({name}) acquired in {duration:0.000}ms{Environment.NewLine}");
        }

        private void CleanupExpiredVotingTokens()
        {
            var now = DateTime.Now;
            if (now < nextVotingTokenCleanup)
                return;

            nextVotingTokenCleanup = now.AddHours(VotingTokenCleanupPeriodHours);

            var cutoff = now.AddDays(-VotingTokenExpirationDays);

            var expiredVotingTokens = votingTokens
                .Where(p => p.Value.Created < cutoff)
                .Select(p => p.Key)
                .ToList();

            foreach (var playerHash in expiredVotingTokens)
                votingTokens.Remove(playerHash);
        }

        private static Dictionary<string, PluginStatData> LoadPluginStatsData(Dictionary<string, string> pluginMap)
        {
            var pluginStatsData = new Dictionary<string, PluginStatData>();
            foreach (var pluginId in pluginMap.Values)
            {
                var pluginStatData = PluginStatData.Load(pluginId) ?? new PluginStatData(pluginId);
                Debug.Assert(pluginStatData.Id == pluginId);
                pluginStatData.CleanupExpiredUses();
                pluginStatsData[pluginId] = pluginStatData;
            }

            return pluginStatsData;
        }

        public void Consent(ConsentRequest request)
        {
            if (!Tools.Tools.ValidatePlayerHash(request.PlayerHash))
                return;

            lock (playerConsents)
            {
                if (request.Consent)
                {
                    playerConsents[request.PlayerHash] = Tools.Tools.FormatDateIso8601(DateTime.Today);
                    return;
                }

                if (!playerConsents.ContainsKey(request.PlayerHash))
                {
                    return;
                }

                playerConsents.Remove(request.PlayerHash);
            }
            
            ForgetPlayer(request.PlayerHash);
        }

        private void ForgetPlayer(string playerHash)
        {
            lock (votingTokens)
            {
                votingTokens.Remove(playerHash);
            }

            lock (pluginStatsData)
            {
                foreach (var pluginStatData in pluginStatsData.Values)
                    pluginStatData.ForgetPlayer(playerHash);
            }

            lock (playersLastSeen)
            {
                playersLastSeen.Remove(playerHash);
            }
        }

        public PluginStats GetStats(string playerHash)
        {
            var pluginStats = new PluginStats();

            if (!string.IsNullOrEmpty(playerHash))
            {
                if (!Tools.Tools.ValidatePlayerHash(playerHash))
                    return null;

                bool consent;
                lock (playerConsents)
                {
                    consent = playerConsents.ContainsKey(playerHash);
                }

                if (consent)
                {
                    var votingToken = new VotingToken();

                    lock (votingTokens)
                    {
                        votingTokens[playerHash] = votingToken;
                    }

                    pluginStats.VotingToken = votingToken.Guid.ToString();

                    var today = Tools.Tools.FormatDateIso8601(DateTime.Today);
                    lock (playersLastSeen)
                    {
                        playersLastSeen[playerHash] = today;
                    }
                }
            }

            lock (pluginStatsData)
            {
                foreach (var pluginStatData in pluginStatsData.Values)
                    pluginStats.Stats[pluginStatData.Id] = pluginStatData.GetStat(playerHash);
            }

            return pluginStats;
        }

        public void Track(TrackRequest request)
        {
            if (request == null)
                return;

            lock (playerConsents)
                if (!playerConsents.ContainsKey(request.PlayerHash))
                    return;

            var today = Tools.Tools.FormatDateIso8601(DateTime.Today);

            lock (pluginStatsData)
            {
                foreach (var pluginId in request.EnabledPluginIds)
                {
                    if (!pluginStatsData.TryGetValue(pluginId, out var pluginStat))
                        pluginStat = pluginStatsData[pluginId] = new PluginStatData(pluginId);

                    pluginStat.ReportUse(request.PlayerHash, today);
                }
            }
        }

        public PluginStat Vote(VoteRequest request)
        {
            if (request == null)
                return null;

            lock (playerConsents)
                if (!playerConsents.ContainsKey(request.PlayerHash))
                    return null;

            // FIXME: Consider logging the failure cases when null is returned below (they may be a precursor to a voting attack)

            // Allow voting only if the player can present a valid token (makes it harder to spoof votes)
            lock (votingTokens)
            {
                if (!votingTokens.TryGetValue(request.PlayerHash, out var votingToken))
                    return null;

                if (request.VotingToken != votingToken.Guid.ToString())
                    return null;
            }

            lock (pluginStatsData)
            {
                if (!pluginStatsData.TryGetValue(request.PluginId, out var pluginStat))
                    pluginStat = pluginStatsData[request.PluginId] = new PluginStatData(request.PluginId);

                // Allow voting only if the player has used the plugin recently (sanity and fraud check)
                if (!pluginStat.Players.ContainsKey(request.PlayerHash))
                    return null;

                var stat = pluginStat.SetVote(request.PlayerHash, request.Vote);
                return stat;
            }
        }

        public void CountRequest(HttpRequest request)
        {
            var path = request.Path;
            var today = Tools.Tools.FormatDateIso8601(DateTime.Today);

            lock (requestCounts)
            {
                if (!requestCounts.TryGetValue(path, out var counts))
                {
                    requestCounts[path] = counts = new();
                }

                counts[today] = counts.GetValueOrDefault(today) + 1;
            }
        }

        public void CountUniquePlayer(string playerHash)
        {
            lock (playersLastSeen)
            {
                var today = Tools.Tools.FormatDateIso8601(DateTime.Today);
                playersLastSeen[playerHash] = today;
                uniquePlayerCounts[today] = playersLastSeen.Values.Count(v => v == today);
            }
        }
    }
}