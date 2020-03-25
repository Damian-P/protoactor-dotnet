// -----------------------------------------------------------------------
//   <copyright file="HostedRemote.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class HostedRemote : IRemote, IRemoteInternals
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

        public Task Start()
        {
            if (IsStarted) return Task.CompletedTask;
            if (RemoteConfig.ServerCredentials == ServerCredentials.Insecure)
                AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            Logger.LogInformation("Starting Proto.Actor server ({Address})", _actorSystem.ProcessRegistry.Address
            );
            _actorSystem.ProcessRegistry.RegisterHostResolver(
                pid => new RemoteProcess(this, _actorSystem, EndpointManager, pid)
            );
            _actorSystem.ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname,
                RemoteConfig.AdvertisedPort.Value
            );
            IsStarted = true;
            EndpointManager.Start();
            SpawnActivator();
            return Task.CompletedTask;
        }

        public async Task Stop(bool graceful = true)
        {
            if (!IsStarted) return;
            IsStarted = false;
            Logger.LogInformation("Stopping Proto.Actor server ({Address})", _actorSystem.ProcessRegistry.Address
            );
            if (graceful)
            {
                await EndpointManager.StopAsync();
                StopActivator();
            }

            Logger.LogInformation("Stopped Proto.Actor server ({Address})", _actorSystem.ProcessRegistry.Address
            );
        }

        public void SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header, message, pid, sender, serializerId);
            EndpointManager.RemoteDeliver(env);
        }
    }
}