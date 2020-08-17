﻿// -----------------------------------------------------------------------
//   <copyright file="ConsulProvider.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Consul;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Proto.Cluster.Data;
using Proto.Cluster.Events;

namespace Proto.Cluster.Consul
{
    public class ConsulLeader
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public Guid MemberId { get; set; }

        public Guid[] BannedMembers { get; set; }
    }

    //TLDR;
    //this class has a very simple responsibility, poll consul for status updates.
    //then transform these statuses to MemberStatus messages and pass on to the 
    //cluster MemberList instance
    //
    //Helper functionality: register, deregister and refresh TTL

    [PublicAPI]
    public class ConsulProvider : IClusterProvider
    {
        private ILogger _logger;
        private readonly TimeSpan _blockingWaitTime;
        private readonly ConsulClient _client;

        private readonly TimeSpan
            _deregisterCritical; //this is how long the service exists in consul before disappearing when unhealthy, min 1 min

        private readonly TimeSpan _serviceTtl; //this is how long the service is healthy without a ttl refresh
        private readonly TimeSpan _refreshTtl; //this is the refresh rate of TTL, should be smaller than the above
        private string _host;

        private Cluster _cluster;
        private string _consulServiceName; //name of the custer, in consul this means the name of the service
        private volatile bool _deregistered;
        private string _consulServiceInstanceId; //the specific instance id of this node in consul


        private string[] _kinds;
        private MemberList _memberList;
        private int _port;
        private bool _shutdown;
        private string _consulSessionId;
        private string _consulLeaderKey;

        public ConsulProvider(ConsulProviderOptions options) : this(options, config => { })
        {
        }

        public ConsulProvider(ConsulProviderOptions options, Action<ConsulClientConfiguration> consulConfig)
        {
            _serviceTtl = options!.ServiceTtl!.Value;
            _refreshTtl = options!.RefreshTtl!.Value;
            _deregisterCritical = options!.DeregisterCritical!.Value;
            _blockingWaitTime = options!.BlockingWaitTime!.Value;
            _client = new ConsulClient(consulConfig);
        }

        public ConsulProvider(IOptions<ConsulProviderOptions> options) : this(options.Value, config => { })
        {
        }

        public ConsulProvider(IOptions<ConsulProviderOptions> options, Action<ConsulClientConfiguration> consulConfig) :
            this(options.Value, consulConfig)
        {
        }

        public async Task StartAsync(Cluster cluster, string clusterName, string host, int port, string[] kinds,
            MemberList memberList)
        {
            _cluster = cluster;
            _consulServiceInstanceId = $"{clusterName}-{_cluster.Id}@{host}:{port}";
            _consulServiceName = clusterName;
            _host = host;
            _port = port;
            _kinds = kinds;

            _memberList = memberList;

            _logger = Log.CreateLogger($"ConsulProvider-{_cluster.Id}");

            await RegisterMemberAsync();

            StartUpdateTtlLoop();
            StartMonitorMemberStatusChangesLoop();
            StartLeaderElectionLoop();
        }


        public async Task ShutdownAsync(bool graceful)
        {
            _logger.LogInformation("Shutting down consul provider");
            //flag for shutdown. used in thread loops
            _shutdown = true;
            if (!graceful)
            {
                return;
            }

            //DeregisterService
            await DeregisterServiceAsync();

            _deregistered = true;
        }

        public async Task UpdateClusterState(ClusterState state)
        {
            var json = JsonConvert.SerializeObject(new ConsulLeader
                {
                    Host = _host,
                    Port = _port,
                    MemberId = _cluster.Id,
                    BannedMembers = state.BannedMembers
                }
            );
            var kvp = new KVPair(_consulLeaderKey)
            {
                Key = _consulLeaderKey,
                Session = _consulSessionId,
                Value = Encoding.UTF8.GetBytes(json)
            };

            var res = await _client.KV.Acquire(kvp);
            

        }

        private void StartMonitorMemberStatusChangesLoop()
        {
            Task.Run(async () =>
                {
                    var waitIndex = 0ul;
                    while (!_shutdown)
                    {
                        var statuses = await _client.Health.Service(_consulServiceName, null, false, new QueryOptions
                            {
                                WaitIndex = waitIndex,
                                WaitTime = _blockingWaitTime
                            }
                        );
                        if (_deregistered)
                        {
                            break;
                        }

                        _logger.LogDebug("Got status updates from Consul");

                        waitIndex = statuses.LastIndex;

                        var memberStatuses =
                            statuses
                                .Response
                                .Where(v => IsAlive(v.Checks)) //only include members that are alive
                                .Select(v => new MemberInfo(
                                        Guid.Parse(v.Service.Meta["id"]),
                                        v.Service.Address,
                                        v.Service.Port,
                                        v.Service.Tags
                                    )
                                )
                                .ToArray();

                        //why is this not updated via the ClusterTopologyEvents?
                        //because following events is messy
                        _memberList.UpdateClusterTopology(memberStatuses);
                        var res = new ClusterTopologyEvent(memberStatuses);
                        _cluster.System.EventStream.Publish(res);
                    }
                }
            );
        }


        private void StartUpdateTtlLoop()
        {
            Task.Run(async () =>
                {
                    while (!_shutdown)
                    {
                        await _client.Agent.PassTTL("service:" + _consulServiceInstanceId, "");
                        await Task.Delay(_refreshTtl);
                    }

                    _logger.LogInformation("Exiting TTL loop");
                }
            );
        }

        private void StartLeaderElectionLoop()
        {
            Task.Run(async () =>
                {
                    try
                    {
                        var leaderKey = $"{_consulServiceName}/leader";
                        var se = new SessionEntry
                        {
                            Behavior = SessionBehavior.Delete,
                            Name = leaderKey
                        };
                        var sessionRes = await _client.Session.Create(se);
                        var sessionId = sessionRes.Response;
                        
                        //this is used so that leader can update shared cluster state
                        _consulSessionId = sessionId;
                        _consulLeaderKey = leaderKey;
                        
                        var json = JsonConvert.SerializeObject(new ConsulLeader
                            {
                                Host = _host,
                                Port = _port,
                                MemberId = _cluster.Id,
                                BannedMembers = new Guid[]{}
                            }
                        );
                        var kvp = new KVPair(leaderKey)
                        {
                            Key = leaderKey,
                            Session = sessionId,
                            Value = Encoding.UTF8.GetBytes(json)
                        };

                        var acquire = await _client.KV.Acquire(kvp);
                        


                        //don't await this, it will block forever
                        _client.Session.RenewPeriodic(TimeSpan.FromSeconds(10), sessionId, CancellationToken.None
                        );

                        var waitIndex = 0ul;
                        while (!_shutdown)
                        {
                            var res = await _client.KV.Get(leaderKey, new QueryOptions()
                                {
                                    Consistency = ConsistencyMode.Default,
                                    WaitIndex = waitIndex,
                                    WaitTime = TimeSpan.FromSeconds(20)
                                }
                            );
                            var value = res.Response.Value;
                            var json2 = Encoding.UTF8.GetString(value);
                            var leader = JsonConvert.DeserializeObject<ConsulLeader>(json2);
                            waitIndex = res.LastIndex;

                            _memberList.UpdateLeader(new LeaderInfo(leader.MemberId,leader.Host,leader.Port, leader.BannedMembers));
                        }
                    }
                    catch (Exception x)
                    {
                        _logger.LogCritical("Leader Election Failed {x}", x);
                    }
                }
            );
        }

        //register this cluster in consul.
        private async Task RegisterMemberAsync()
        {
            var s = new AgentServiceRegistration
            {
                ID = _consulServiceInstanceId,
                Name = _consulServiceName,
                Tags = _kinds.ToArray(),
                Address = _host,
                Port = _port,
                Check = new AgentServiceCheck
                {
                    DeregisterCriticalServiceAfter = _deregisterCritical,
                    TTL = _serviceTtl,
                },
                Meta = new Dictionary<string, string>
                {
                    //register a unique ID for the current process
                    //if a node with host X and port Y, joins, then leaves, then joins again.
                    //we need a way to distinguish the new node from the old node.
                    //this is what this ID is for
                    {"id", _cluster.Id.ToString()}
                }
            };
            await _client.Agent.ServiceRegister(s);
        }


        //unregister this cluster from consul
        private async Task DeregisterServiceAsync()
        {
            await _client.Agent.ServiceDeregister(_consulServiceInstanceId);
            _logger.LogInformation("Deregistered service");
        }

        private static bool IsAlive(HealthCheck[] serviceChecks) =>
            serviceChecks.All(c => c.Status == HealthStatus.Passing);
    }
}