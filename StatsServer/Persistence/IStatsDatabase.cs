using System;
using avaness.StatsServer.Model;
using Microsoft.AspNetCore.Http;

namespace avaness.StatsServer.Persistence;

public interface IStatsDatabase : IDisposable
{
    void Save();
    void Canary();
    void Consent(ConsentRequest request);
    PluginStats GetStats(string playerHash);
    void Track(TrackRequest request);
    PluginStat Vote(VoteRequest request);
    void CountRequest(HttpRequest endpoint);
    void CountUniquePlayer(string playerHash);
}