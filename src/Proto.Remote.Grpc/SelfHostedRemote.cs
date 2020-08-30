// -----------------------------------------------------------------------
//   <copyright file="SelfHostedRemote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class SelfHostedRemote : Remote
    {
        private Server _server = null!;
        private EndpointReader _endpointReader;
        private HealthServiceImpl _healthCheck = null!;

        public SelfHostedRemote(ActorSystem system, string hostname, int port,
            Action<IRemoteConfiguration>? configure = null, Action<List<ChannelOption>>? configureChannelOptions = null)
            : base(system, hostname, port, new ChannelProvider(configureChannelOptions), configure)
        {
            _endpointReader = new EndpointReader(_system, EndpointManager, Serialization);
            _healthCheck = new HealthServiceImpl();

            _server = new Server
            {
                Services =
                {
                    Remoting.BindService(_endpointReader),
                    Health.BindService(_healthCheck)
                },
                Ports = {new ServerPort(hostname, port, RemoteConfig.ServerCredentials)}
            };
        }

        public override void Start()
        {
            if (IsStarted) return;
            base.Start();
            
            _server.Start();

            var boundPort = _server.Ports.Single().BoundPort;
            _system.ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname ?? _hostname,
                RemoteConfig.AdvertisedPort ?? boundPort
            );
            Logger.LogInformation("Starting Proto.Actor server on {Host}:{Port} ({Address})", _hostname, boundPort,
                _system.ProcessRegistry.Address
            );
        }

        public override async Task ShutdownAsync(bool graceful = true)
        {
            try
            {
                await base.ShutdownAsync();
                if (graceful)
                {
                    await base.ShutdownAsync();
                    await _server.KillAsync(); //TODO: was ShutdownAsync but that never returns?
                }
                else
                {
                    await _server.KillAsync();
                }

                Logger.LogDebug(
                    "Proto.Actor server stopped on {Address}. Graceful: {Graceful}",
                    _system.ProcessRegistry.Address, graceful
                );
            }
            catch (Exception ex)
            {
                await _server.KillAsync();

                Logger.LogError(
                    ex, "Proto.Actor server stopped on {Address} with error: {Message}",
                    _system.ProcessRegistry.Address, ex.Message
                );
            }
        }
    }
}