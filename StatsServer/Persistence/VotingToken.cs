using System;

namespace avaness.StatsServer.Persistence;

public class VotingToken
{
    public DateTime Created { get; set; }
    public Guid Guid { get; set; }

    public VotingToken()
    {
        Created = DateTime.Now;
        Guid = Guid.NewGuid();
    }
}