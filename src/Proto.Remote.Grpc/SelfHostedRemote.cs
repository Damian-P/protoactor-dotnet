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
        public bool Started { get; private set; }
        public RemoteConfig Config { get; }
        public ActorSystem System { get; }
        public Task StartAsync()
        {
            lock (this)
            {
                if (Started)
                    return Task.CompletedTask;
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
                Started = true;
                return Task.CompletedTask;
            }
        }

        public Task ShutdownAsync(bool graceful = true)
        {
            lock (this)
            {
                if (!Started)
                    return Task.CompletedTask;
                try
                {
                    if (graceful)
                    {
                        _endpointManager.Stop();
                        _server.ShutdownAsync().GetAwaiter().GetResult();
                    }
                    else
                    {
                        _server.KillAsync().GetAwaiter().GetResult();
                    }

                    Logger.LogDebug(
                        "Proto.Actor server stopped on {Address}. Graceful: {Graceful}",
                        System.Address, graceful
                    );
                }
                catch (Exception ex)
                {
                    Logger.LogError(
                        ex, "Proto.Actor server stopped on {Address} with error: {Message}",
                        System.Address, ex.Message
                    );
                    _server.KillAsync().GetAwaiter().GetResult();
                }
                Started = false;
                return Task.CompletedTask;
            }
        }

        // Only used in tests ?
        public void SendMessage(PID pid, object msg, int serializerId)
        {
            _endpointManager.SendMessage(pid, msg, serializerId);
        }
    }
}