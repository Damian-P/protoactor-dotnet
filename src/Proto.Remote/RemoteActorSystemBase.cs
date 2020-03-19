// -----------------------------------------------------------------------
//   <copyright file="RemoteActorSystemBase.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public abstract class RemoteActorSystemBase : ActorSystem, IInternalRemoteActorAsystem
    {
        private static readonly ILogger Logger = Log.CreateLogger(typeof(RemoteActorSystemBase).FullName);
        private PID _activatorPid;
        public Serialization Serialization { get; }
        public EndpointManager EndpointManager { get; protected set; }
        public IRemoteConfig RemoteConfig { get; set; }
        public RemoteKindRegistry RemoteKindRegistry { get; }
        public EndpointReader EndpointReader { get; }
        public string Hostname { get; }
        public int Port { get; }

        public RemoteActorSystemBase(string hostname, int port, IRemoteConfig remoteConfig)
        {
            RemoteConfig = remoteConfig ?? new RemoteConfigBase();
            Serialization = new Serialization();
            EndpointReader = new EndpointReader(this);
            ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(this, pid));
            ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname ?? hostname, RemoteConfig.AdvertisedPort ?? port
            );
            Hostname = hostname;
            Port = port;
        }

        private PID ActivatorForAddress(string address) => new PID(address, "activator");

        public Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout) =>
            SpawnNamedAsync(address, "", kind, timeout);

        public async Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout)
        {
            var activator = ActivatorForAddress(address);

            var res = await Root.RequestAsync<ActorPidResponse>(
                activator, new ActorPidRequest
                {
                    Kind = kind,
                    Name = name
                }, timeout
            );

            return res;
        }

        public virtual Task StartAsync(CancellationToken cancellationToken = default)
        {
            EndpointManager.Start();
            SpawnActivator();
            Logger.LogDebug("Starting Proto.Actor server on {Host}:{Port} ({Address})", Hostname, Port,
                ProcessRegistry.Address
            );
            return Task.CompletedTask;
        }

        public virtual Task StopAsync(CancellationToken cancellationToken = default)
        {
            EndpointReader.Suspend(true);
            EndpointManager.Stop();
            StopActivator();
            return Task.CompletedTask;
        }

        void IInternalRemoteActorAsystem.SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header, message, pid, sender, serializerId);
            this.EndpointManager.RemoteDeliver(env);
        }

        private void SpawnActivator()
        {
            var props = Props.FromProducer(() => new Activator(this))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            _activatorPid = Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => Root.Stop(_activatorPid);
    }
}