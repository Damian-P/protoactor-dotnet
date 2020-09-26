﻿﻿// -----------------------------------------------------------------------
//   <copyright file="Cluster.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;
using Proto.Cluster.IdentityLookup;
using Proto.Cluster.Partition;
using Proto.Remote;

namespace Proto.Cluster
{
    [PublicAPI]
    public class Cluster
    {
        private static ILogger _logger = null!;

        public Cluster(ActorSystem system, IRemote remote)
        {
            System = system;
            Remote = remote;
            system.EventStream.Subscribe<ClusterTopology>(e =>
                {
                    foreach (var member in e.Left)
                    {
                        PidCache.RemoveByMember(member);
                    }
                }
            );
        }

        public Guid Id { get; } = Guid.NewGuid();

        public ClusterConfig Config { get; private set; } = null!;

        public ActorSystem System { get; }

        public IRemote Remote { get; }


        public MemberList MemberList { get; private set; } = null!;

        private IIdentityLookup IdentityLookup { get; set; } = null!;

        internal IClusterProvider Provider { get; set; } = null!;

        public string LoggerId => System.Address;

        public Task StartMemberAsync(string clusterName, IClusterProvider cp)
            => StartMemberAsync(new ClusterConfig(clusterName, cp));

        public async Task StartMemberAsync(ClusterConfig config)
        {
            BeginStart(config, false);

            var (host, port) = System.GetAddress();

            Provider = Config.ClusterProvider;

            var kinds = Remote.RemoteKindRegistry.GetKnownKinds();
            await Provider.StartMemberAsync(
                this,
                Config.Name,
                host,
                port,
                kinds,
                MemberList
            );

            _logger.LogInformation("Started as cluster member");
        }

        public async Task StartClientAsync(ClusterConfig config)
        {
            BeginStart(config, true);

            var (host, port) = System.GetAddress();

            Provider = Config.ClusterProvider;

            await Provider.StartClientAsync(
                this,
                Config.Name,
                host,
                port,
                MemberList
            );

            _logger.LogInformation("Started as cluster client");
        }

        private void BeginStart(ClusterConfig config, bool client)
        {
            Config = config;

            //default to partition identity lookup
            IdentityLookup = config.IdentityLookup ?? new PartitionIdentityLookup();
            Remote.Start();
            Remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            _logger = Log.CreateLogger($"Cluster-{LoggerId}");
            _logger.LogInformation("Starting");
            MemberList = new MemberList(this);

            var kinds = Remote.RemoteKindRegistry.GetKnownKinds();
            IdentityLookup.SetupAsync(this, kinds, client);
        }

        public async Task ShutdownAsync(bool graceful = true)
        {
            _logger.LogInformation("Stopping");
            if (graceful)
            {
                await IdentityLookup!.ShutdownAsync();
            }

            await Config!.ClusterProvider.ShutdownAsync(graceful);
            await Remote.ShutdownAsync(graceful);

            _logger.LogInformation("Stopped");
        }

        public Task<PID?> GetAsync(string identity, string kind) =>
            GetAsync(identity, kind, CancellationToken.None);

        public Task<PID?> GetAsync(string identity, string kind, CancellationToken ct) =>
            IdentityLookup!.GetAsync(identity, kind, ct);

        public PidCache PidCache { get; } = new PidCache();

        public async Task<T> RequestAsync<T>(string identity, string kind, object message, CancellationToken ct)
        {
            var key = kind + "." + identity;
            _logger.LogDebug("Requesting {Identity}-{Kind} Message {Message}", identity, kind, message);
            var i = 0;
            while (!ct.IsCancellationRequested)
            {
                var hadPid = PidCache.TryGet(kind, identity, out var cachedPid);
                try
                {
                    if (hadPid)
                    {
                        _logger.LogDebug("Requesting {Identity}-{Kind} Message {Message} - Got PID from cache",
                            identity,
                            kind, message, cachedPid
                        );
                        var res = await System.Root.RequestAsync<T>(cachedPid, message, ct);
                        if (res != null)
                        {
                            return res;
                        }

                        PidCache.TryRemove(kind, identity, cachedPid);
                    }
                }
                catch
                {
                    //YOLO
                    PidCache.TryRemove(kind, identity, cachedPid);
                }



                var delay = i * 20;
                i++;
                var pid = await GetAsync(identity, kind, ct);


                if (pid == null)
                {
                    _logger.LogDebug(
                        "Requesting {Identity}-{Kind} Message {Message} - Did not get any PID from IdentityLookup",
                        identity, kind, message
                    );
                    await Task.Delay(delay, CancellationToken.None);
                    continue;
                }

                _logger.LogDebug("Requesting {Identity}-{Kind} Message {Message} - Got PID {PID} from IdentityLookup",
                    identity, kind, message, pid
                );
                //update cache
                PidCache.TryAdd(kind, identity, pid); //TODO: Responsibility of identity lookup?

                var res2 = await System.Root.RequestAsync<T>(pid, message, ct);
                if (res2 != null) return res2;

                PidCache.TryRemove(kind, identity, cachedPid);
                await Task.Delay(delay, CancellationToken.None);
            }

            return default!;
        }
    }
}