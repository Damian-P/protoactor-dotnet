// -----------------------------------------------------------------------
//   <copyright file="Cluster.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto.Remote;

namespace Proto.Cluster
{
    public class Cluster: IProtoPlugin
    {
        private static readonly ILogger Logger = Log.CreateLogger(typeof(Cluster).FullName);

        internal ClusterConfig Config;
        public ActorSystem System
        {
            get;
        }

        public IRemote Remote
        {
            get;
        }
        public Cluster(ActorSystem system, string clusterName, IClusterProvider cp)
        : this(system, new ClusterConfig(clusterName, cp))
        {

        }
        public Cluster(ActorSystem system, ClusterConfig clusterConfig)
        {
            System = system;
            var remote = system.Plugins.GetPlugin<IRemote>();
            if (remote == null) throw new InvalidOperationException("Remoting is not configured");
            Remote = remote;
            System.Plugins.AddPlugin(this);
            Config = clusterConfig;
            Partition = new Partition(this);
            MemberList = new MemberList(this);
            PidCache = new PidCache(this);
            Remote.RemotingConfiguration.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
        }
        internal Partition Partition { get; }
        internal MemberList MemberList { get; }
        internal PidCache PidCache { get; }

        public async Task Start()
        {
            Logger.LogInformation("Starting Proto.Actor cluster");

            var kinds = this.Remote.RemotingConfiguration.RemoteKindRegistry.GetKnownKinds();
            if (!Remote.IsStarted)
                await Remote.Start();
            Partition.Setup(kinds);
            PidCache.Setup();
            MemberList.Setup();

            var (host, port) = System.ProcessRegistry.GetAddress();

            await Config.ClusterProvider.RegisterMemberAsync(this, Config.Name, host, port, kinds, Config.InitialMemberStatusValue, Config.MemberStatusValueSerializer
            );
            Config.ClusterProvider.MonitorMemberStatusChanges(this);

            Logger.LogInformation("Started cluster");
        }

        public async Task Shutdown(bool graceful = true)
        {
            Logger.LogInformation($"Stopping Cluster at {System.ProcessRegistry.GetAddress().Host}:{System.ProcessRegistry.GetAddress().Port}");
            if (graceful)
            {
                await Config.ClusterProvider.Shutdown(this);

                //This is to wait ownership transferring complete.
                // await Task.Delay(2000);

                MemberList.Stop();
                PidCache.Stop();
                Partition.Stop();
            }

            await Remote.Stop(graceful);

            Logger.LogInformation($"Stopped Cluster at {System.ProcessRegistry.GetAddress().Host}:{System.ProcessRegistry.GetAddress().Port}");
        }

        public Task<(PID, ResponseStatusCode)> GetAsync(string name, string kind) => GetAsync(name, kind, CancellationToken.None);

        public async Task<(PID, ResponseStatusCode)> GetAsync(string name, string kind, CancellationToken ct)
        {
            //Check Cache
            if (PidCache.TryGetCache(name, out var pid))
                return (pid, ResponseStatusCode.OK);

            //Get Pid
            var address = MemberList.GetPartition(name, kind);

            if (string.IsNullOrEmpty(address))
            {
                return (null, ResponseStatusCode.Unavailable);
            }

            var remotePid = Partition.PartitionForKind(address, kind);

            var req = new ActorPidRequest
            {
                Kind = kind,
                Name = name
            };

            Logger.LogDebug("Requesting remote PID from {Partition}:{Remote} {@Request}", address, remotePid, req);
            try
            {
                var resp = ct == CancellationToken.None
                    ? await System.Root.RequestAsync<ActorPidResponse>(remotePid, req, Config.TimeoutTimespan)
                    : await System.Root.RequestAsync<ActorPidResponse>(remotePid, req, ct);
                var status = (ResponseStatusCode)resp.StatusCode;

                switch (status)
                {
                    case ResponseStatusCode.OK:
                        PidCache.TryAddCache(name, resp.Pid);
                        return (resp.Pid, status);
                    default:
                        return (resp.Pid, status);
                }
            }
            catch (TimeoutException e)
            {
                Logger.LogWarning(e, "Remote PID request timeout {@Request}", req);
                return (null, ResponseStatusCode.Timeout);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "Error occured requesting remote PID {@Request}", req);
                return (null, ResponseStatusCode.Error);
            }
        }
    }
}