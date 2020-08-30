// -----------------------------------------------------------------------
//   <copyright file="HostedRemote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class HostedRemote : IRemote
    {
        private readonly ILogger Logger;
        public bool IsStarted { get; private set; }
        private PID _activatorPid;
        private readonly ActorSystem _actorSystem;
        public EndpointManager EndpointManager { get; }

        public RemoteConfig RemoteConfig { get; } = new RemoteConfig();

        public RemoteKindRegistry RemoteKindRegistry { get; } = new RemoteKindRegistry();

        public Serialization Serialization { get; } = new Serialization();

        public HostedRemote(ActorSystem actorSystem, ILogger<HostedRemote> logger, IChannelProvider channelProvider)
        {
            actorSystem.Plugins.AddPlugin<IRemote>(this);
            _actorSystem = actorSystem;
            EndpointManager = new EndpointManager(this, actorSystem, channelProvider);
            Logger = logger;
        }

        public Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout) =>
            SpawnNamedAsync(address, "", kind, timeout);

        public async Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout)
        {
            var activator = ActivatorForAddress(address);

            var res = await _actorSystem.Root.RequestAsync<ActorPidResponse>(
                activator, new ActorPidRequest
                {
                    Kind = kind,
                    Name = name
                }, timeout
            );

            return res;
        }

        private void SpawnActivator()
        {
            var props = Props.FromProducer(() => new Activator(RemoteKindRegistry, _actorSystem))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            _activatorPid = _actorSystem.Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => _actorSystem.Root.Stop(_activatorPid);

        private PID ActivatorForAddress(string address) => new PID(address, "activator");

        public void Start()
        {
            if (IsStarted) return;
            if (RemoteConfig.ServerCredentials == ServerCredentials.Insecure)
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            Logger.LogDebug("Starting Proto.Actor server ({Address})", _actorSystem.ProcessRegistry.Address
            );
            _actorSystem.ProcessRegistry.RegisterHostResolver(
                pid => new RemoteProcess(this, _actorSystem, EndpointManager, pid)
            );
            _actorSystem.ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname ?? throw new ArgumentException("AdvertisedHostname missing"),
                RemoteConfig.AdvertisedPort ?? throw new ArgumentException("AdvertisedPort missing")
            );
            IsStarted = true;
            EndpointManager.Start();
            SpawnActivator();
        }

        public Task ShutdownAsync(bool graceful = true)
        {
            if (!IsStarted) return Task.CompletedTask;
            IsStarted = false;
            Logger.LogDebug("Stopping Proto.Actor server ({Address})", _actorSystem.ProcessRegistry.Address
            );
            if (graceful)
            {
                EndpointManager.Stop();
                StopActivator();
            }

            Logger.LogDebug("Stopped Proto.Actor server ({Address})", _actorSystem.ProcessRegistry.Address
            );
            return Task.CompletedTask;
        }

        public void SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header, message, pid, sender, serializerId);
            EndpointManager.RemoteDeliver(env);
        }
    }
}