// -----------------------------------------------------------------------
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
    public class Cluster : IProtoPlugin
    {
        private static ILogger _logger = null!;

        public Cluster(ActorSystem system, ClusterConfig clusterConfig)
        {
            Config = clusterConfig;
            System = system;
            Remote = system.Plugins.GetPlugin<IRemote>();
            PidCache = new PidCache();
            PidCacheUpdater = new PidCacheUpdater(this, PidCache);
            //default to partition identity lookup
            IdentityLookup = clusterConfig.IdentityLookup ?? new PartitionIdentityLookup();

            MemberList = new MemberList(this);
            Provider = clusterConfig.ClusterProvider;
        }

        public Cluster(ActorSystem system, string clusterName, IClusterProvider cp)
            : this(system, new ClusterConfig(clusterName, cp))
        {
        }

        public Guid Id { get; } = Guid.NewGuid();

        internal ClusterConfig Config { get; }

        public ActorSystem System { get; }

        public IRemote Remote { get; }


        internal MemberList MemberList { get; }
        internal PidCache PidCache { get; }
        internal PidCacheUpdater PidCacheUpdater { get; }

        private IIdentityLookup IdentityLookup { get; }

        internal IClusterProvider Provider { get; set; }

        public string LoggerId => System.ProcessRegistry.Address;

        public async Task StartAsync()
        {
            Remote.Start();
            Remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            _logger = Log.CreateLogger($"Cluster-{LoggerId}");
            _logger.LogInformation("Starting");

            var (host, port) = System.ProcessRegistry.GetAddress();
            var kinds = Remote.RemoteKindRegistry.GetKnownKinds();
            IdentityLookup.Setup(this, kinds);
            if (Config.UsePidCache)
            {
                PidCacheUpdater.Setup();
            }
            await Provider.StartAsync(
                this,
                Config.Name,
                host,
                port,
                kinds,
                MemberList
            );

            _logger.LogInformation("Started");
        }

        public async Task ShutdownAsync(bool graceful = true)
        {
            _logger.LogInformation("Stopping");
            if (graceful)
            {
                PidCacheUpdater!.Shutdown();
                IdentityLookup!.Shutdown();
            }

            await Config!.ClusterProvider.ShutdownAsync(graceful);
            await Remote.ShutdownAsync(graceful);

            _logger.LogInformation("Stopped");
        }

        public Task<PID?> GetAsync(string identity, string kind) =>
            GetAsync(identity, kind, CancellationToken.None);

        public Task<PID?> GetAsync(string identity, string kind, CancellationToken ct)
        {
            if (Config.UsePidCache)
            {
                //Check Cache
                if (PidCache.TryGetCache(identity, out var pid))
                {
                    return Task.FromResult<PID?>(pid);
                }
            }

            return IdentityLookup!.GetAsync(identity, kind, ct);
        }

        public async Task<T> RequestAsync<T>(string identity, string kind, object message, CancellationToken ct)
        {
            for (var i = 0; i < 20; i++)
            {
                var delay = i * 10;
                var pid = await GetAsync(identity, kind, ct);
                if (pid == null)
                {
                    _logger.LogDebug("Got null pid for {Identity}", identity);
                    await Task.Delay(delay, CancellationToken.None);
                    continue;
                }

                var res = await System.Root.RequestAsync<T>(pid, message, ct);
                if (res != null)
                {
                    return res;
                }

                _logger.LogDebug("Got null response from request to {Identity}", identity);

                await Task.Delay(delay, CancellationToken.None);
            }

            return default!;
        }
    }
}