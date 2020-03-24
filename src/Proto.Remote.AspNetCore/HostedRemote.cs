// -----------------------------------------------------------------------
//   <copyright file="HostedRemote.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class HostedRemote : IRemote
    {
        private readonly ILogger Logger;
        public bool IsStarted { get; private set; }
        private PID _activatorPid;
        private readonly ActorSystem _actorSystem;
        private readonly EndpointManager _endpointManager;
        public RemotingConfiguration RemotingConfiguration { get; }
        public HostedRemote(ActorSystem actorSystem, EndpointManager endpointManager, RemotingConfiguration remotingConfiguration, ILogger<HostedRemote> logger)
        {
            actorSystem.ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(actorSystem, endpointManager, pid));
            actorSystem.Plugins.Add(typeof(IRemote), this);
            actorSystem.ProcessRegistry.SetAddress(remotingConfiguration.RemoteConfig.AdvertisedHostname, remotingConfiguration.RemoteConfig.AdvertisedPort.Value);

            _actorSystem = actorSystem;
            _endpointManager = endpointManager;
            RemotingConfiguration = remotingConfiguration;
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
            var props = Props.FromProducer(() => new Activator(RemotingConfiguration.RemoteKindRegistry, _actorSystem))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            _activatorPid = _actorSystem.Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => _actorSystem.Root.Stop(_activatorPid);

        private PID ActivatorForAddress(string address) => new PID(address, "activator");

        public Task Start()
        {
            if (IsStarted) return Task.CompletedTask;
            Logger.LogInformation("Starting Proto.Actor");
            IsStarted = true;
            _endpointManager.Start();
            SpawnActivator();
            return Task.CompletedTask;
        }

        public async Task Stop(bool graceful = true)
        {
            if (graceful)
            {
                await _endpointManager.StopAsync();
                StopActivator();
            }

        }
        public void SendMessage(PID pid, object msg, int serializerId)
            => _endpointManager.SendMessage(pid, msg, serializerId);
    }
}