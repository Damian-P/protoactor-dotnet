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

        internal ClusterConfig Config { get; private set; }

        public ActorSystem System { get; }

        public IRemote Remote { get; }

        public Cluster(ActorSystem system, string clusterName, IClusterProvider cp)
            : this(system, new ClusterConfig(clusterName, cp))
        {
        }

        public Cluster(ActorSystem system, ClusterConfig config)
        {
            System = system;
            Remote = system.Plugins.GetPlugin<IRemote>();
            System.Plugins.AddPlugin(this);
            Config = config;
            PidCache = new PidCache();
            MemberList = new MemberList(this);
            PidCacheUpdater = new PidCacheUpdater(this, PidCache);
        }

        public Guid Id { get; } = Guid.NewGuid();

        internal MemberList MemberList { get; }
        internal PidCache PidCache { get; }
        internal PidCacheUpdater PidCacheUpdater { get; }

        private IIdentityLookup? IdentityLookup { get; set; }

        public async Task StartAsync()
        {


            //default to partition identity lookup
            IdentityLookup = Config.IdentityLookup ?? new PartitionIdentityLookup();
            Remote.Start();
            Remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            _logger = Log.CreateLogger($"Cluster-{LoggerId}");
            _logger.LogInformation("Starting");

            var kinds = Remote.RemoteKindRegistry.GetKnownKinds();
            IdentityLookup.Setup(this, kinds);



            if (Config.UsePidCache)
            {
                PidCacheUpdater.Setup();
            }

            var (host, port) = System.ProcessRegistry.GetAddress();

            Provider = Config.ClusterProvider;

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

        internal IClusterProvider Provider { get; set; }

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

        public Task<(PID?, ResponseStatusCode)> GetAsync(string identity, string kind) =>
            GetAsync(identity, kind, CancellationToken.None);

        public Task<(PID?, ResponseStatusCode)> GetAsync(string identity, string kind, CancellationToken ct)
        {
            if (Config.UsePidCache)
            {
                //Check Cache
                if (PidCache.TryGetCache(identity, out var pid))
                {
                    return Task.FromResult<(PID?, ResponseStatusCode)>((pid, ResponseStatusCode.OK));
                }
            }

            return IdentityLookup!.GetAsync(identity, kind, ct);
        }

        public async Task<T> RequestAsync<T>(string identity, string kind, object message, CancellationToken ct)
        {
            for (int i = 0; i < 10; i++)
            {
                var (pid, status) = await GetAsync(identity, kind, ct);
                if (status != ResponseStatusCode.OK && status != ResponseStatusCode.ProcessNameAlreadyExist)
                {
                    continue;
                }

                var res = await System.Root.RequestAsync<T>(pid, message, ct);
                if (res != null)
                {
                    return res;
                }

                await Task.Delay(i * 10, ct);
            }

            return default!;
        }

        public string LoggerId => System.ProcessRegistry.Address;
    }
}