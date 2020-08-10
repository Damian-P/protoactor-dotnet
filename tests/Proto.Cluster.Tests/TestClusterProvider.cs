﻿using System.Threading.Tasks;

namespace Proto.Cluster.Tests
{
    public class TestClusterProvider : IClusterProvider
    {
        public Task StartAsync(Cluster cluster, string clusterName, string h, int p, string[] kinds, MemberList memberList)
        {
            return Task.FromResult(0);
        }

        public void MonitorMemberStatusChanges(Cluster cluster)
        {
        }

        public Task UpdateMemberStatusValueAsync(Cluster cluster)
        {
            return Task.FromResult(0);
        }

        public Task DeregisterMemberAsync(Cluster cluster)
        {
            return Task.FromResult(0);
        }

        public Task ShutdownAsync(bool graceful)
        {
            return Task.FromResult(0);
        }
    }
}