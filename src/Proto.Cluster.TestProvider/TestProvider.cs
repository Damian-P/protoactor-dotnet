﻿// -----------------------------------------------------------------------
//   <copyright file="ConsulProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Timer = System.Timers.Timer;

namespace Proto.Cluster.Testing
{
    public class TestProvider : IClusterProvider
    {
        private readonly TestProviderOptions _options;
        private Timer _ttlReportTimer;
        private string _id;
        private string _clusterName;
        private ActorSystem _system;
        private static readonly ILogger Logger = Log.CreateLogger<TestProvider>();
        private readonly InMemAgent _agent;
        private IMemberStatusValueSerializer _statusValueSerializer;
        private MemberList _memberList;


        public TestProvider(TestProviderOptions options,InMemAgent agent)
        {
            _options = options;
            _agent = agent;
            agent.StatusUpdate += AgentOnStatusUpdate;
        }

        private void AgentOnStatusUpdate(object sender, EventArgs e)
        {
            NotifyStatuses(0);
        }


        public Task StartAsync(Cluster cluster,
            string clusterName, string address, int port, string[] kinds, IMemberStatusValue? statusValue,
            IMemberStatusValueSerializer statusValueSerializer, MemberList memberList)
        {
            _id = $"{clusterName}@{address}:{port}";
            _clusterName = clusterName;
            _system = cluster.System;
            _statusValueSerializer = statusValueSerializer;
            _memberList = memberList;

            StartTTLTimer();
            
            _agent.RegisterService(new AgentServiceRegistration
            {
                Address= address,
                ID = _id,
                Kinds = kinds,
                Port = port,
            });
            
            return Actor.Done;
        }

        private async Task NotifyStatuses(ulong index)
        {
            var statuses = _agent.GetServicesHealth();

            Logger.LogDebug("TestAgent response: {@Response}", (object) statuses);

            var memberStatuses =
                statuses.Select(
                        x => new MemberStatus(
                            x.ID, 
                            x.Host, 
                            x.Port, 
                            x.Kinds,
                            x.Alive,
                            _statusValueSerializer.Deserialize(x.StatusValue)
                        )
                    )
                    .ToList();

            _memberList.UpdateClusterTopology(memberStatuses);
        }

        private void StartTTLTimer()
        {
            _ttlReportTimer = new Timer(_options.RefreshTtl.TotalMilliseconds);
            _ttlReportTimer.Elapsed += (sender, args) => { RefreshTTL(); };
            _ttlReportTimer.Enabled = true;
            _ttlReportTimer.AutoReset = true;
            _ttlReportTimer.Start();
        }

        private void RefreshTTL()
        {
            _agent.RefreshServiceTTL(_id);
        }

        public Task DeregisterMemberAsync(Cluster cluster)
        {
            Logger.LogDebug("Unregistering service {Service}", _id);

            _ttlReportTimer.Stop();
            _agent.DeregisterService(_id);
            return Task.CompletedTask;
        }

        public Task ShutdownAsync(Cluster cluster)
        {
            return DeregisterMemberAsync(cluster);
        }
    }
}