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
using Proto.Remote;

namespace Proto.Cluster
{
    [PublicAPI]
    public class Cluster : IProtoPlugin
    {
        private static readonly ILogger Logger = Log.CreateLogger(typeof(Cluster).FullName);

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


        internal MemberList MemberList { get; }
        internal PidCache PidCache { get; }
        internal PidCacheUpdater PidCacheUpdater { get; }

        private IIdentityLookup? IdentityLookup { get; set; }

        public async Task Start()
        {


            //default to partition identity lookup
            IdentityLookup = Config.IdentityLookup;



            Remote.Start();

            Remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);

            Logger.LogInformation("[Cluster] Starting...");

            var kinds = Remote.RemoteKindRegistry.GetKnownKinds();
            IdentityLookup.Setup(this, kinds);



            if (Config?.UsePidCache ?? false)
            {
                PidCacheUpdater.Setup();
            }

            var (host, port) = System.ProcessRegistry.GetAddress();

            await Config.ClusterProvider.StartAsync(
                this,
                Config.Name,
                host,
                port,
                kinds,
                Config.InitialMemberStatusValue,
                Config.MemberStatusValueSerializer,
                MemberList
            );

            Logger.LogInformation("[Cluster] Started");
        }

        public async Task Shutdown(bool graceful = true)
        {
            Logger.LogInformation("[Cluster] Stopping...");
            if (graceful)
            {
                await Config!.ClusterProvider.ShutdownAsync(this);

                PidCacheUpdater.Stop();
                IdentityLookup.Stop();
            }

            await Remote.Stop(graceful);

            Logger.LogInformation("[Cluster] Stopped");
        }

        public Task<(PID?, ResponseStatusCode)> GetAsync(string identity, string kind) => GetAsync(identity, kind, CancellationToken.None);

        public Task<(PID?, ResponseStatusCode)> GetAsync(string identity, string kind, CancellationToken ct)
        {
            if (Config.UsePidCache)
            {
                //Check Cache
                if (PidCache.TryGetCache(identity, out var pid))
                    return Task.FromResult((pid, ResponseStatusCode.OK));
            }

            return IdentityLookup!.GetAsync(identity, kind, ct);
        }
    }
}
