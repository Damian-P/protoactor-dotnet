// -----------------------------------------------------------------------
//   <copyright file="Remote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Health.V1;
using Grpc.HealthCheck;
using JetBrains.Annotations;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{

    [PublicAPI]
    public class SelfHostedRemote : IRemote
    {
        private static readonly ILogger Logger = Log.CreateLogger<SelfHostedRemote>();
        private EndpointManager _endpointManager = null!;
        private EndpointReader _endpointReader = null!;
        private HealthServiceImpl _healthCheck = null!;
        private Server _server = null!;
        private readonly GrpcRemoteConfig _config;

        public SelfHostedRemote(ActorSystem system, GrpcRemoteConfig config)
        {
            System = system;
            Config = config;
            _config = config;
        }

        public RemoteConfig Config { get; }
        public ActorSystem System { get; }
        public Task StartAsync()
        {
            var channelProvider = new ChannelProvider(_config);
            _endpointManager = new EndpointManager(System, Config, channelProvider);
            _endpointReader = new EndpointReader(System, _endpointManager, Config.Serialization);
            _healthCheck = new HealthServiceImpl();
            _server = new Server
            {
                Services =
                {
                    Remoting.BindService(_endpointReader),
                    Health.BindService(_healthCheck)
                },
                Ports = { new ServerPort(Config.Host, Config.Port, _config.ServerCredentials) }
            };
            _server.Start();

            var boundPort = _server.Ports.Single().BoundPort;
            System.SetAddress(Config.AdvertisedHostname ?? Config.Host, Config.AdvertisedPort ?? boundPort
            );
            _endpointManager.Start();

            Logger.LogDebug("Starting Proto.Actor server on {Host}:{Port} ({Address})", Config.Host, boundPort,
                System.Address
            );

            return Task.CompletedTask;
        }

        public async Task ShutdownAsync(bool graceful = true)
        {
            try
            {
                if (graceful)
                {
                    _endpointManager.Stop();
                    await _server.ShutdownAsync();
                }
                else
                {
                    await _server.KillAsync();
                }

                Logger.LogDebug(
                    "Proto.Actor server stopped on {Address}. Graceful: {Graceful}",
                    System.Address, graceful
                );
            }
            catch (Exception ex)
            {
                await _server.KillAsync();

                Logger.LogError(
                    ex, "Proto.Actor server stopped on {Address} with error: {Message}",
                    System.Address, ex.Message
                );
            }
        }

        // Only used in tests ?
        public void SendMessage(PID pid, object msg, int serializerId)
        {
            _endpointManager.SendMessage(pid, msg, serializerId);
        }
    }
}