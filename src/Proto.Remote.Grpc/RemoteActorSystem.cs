// -----------------------------------------------------------------------
//   <copyright file="RemoteActorSystem.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace Proto.Remote.Grpc
{
    public class RemoteActorSystem : RemoteActorSystemBase
    {
        private static readonly ILogger Logger = Log.CreateLogger(typeof(RemoteActorSystem).FullName);
        private Server server;

        public new RemoteConfig RemoteConfig { get; }
        public RemoteActorSystem(string hostname, int port, RemoteConfig config = null) : base(hostname, port, config)
        {
            RemoteConfig = config ?? new RemoteConfig();
            EndpointManager = new EndpointManager(this);
        }

        public override async Task StartAsync(CancellationToken cancellationToken = default)
        {
            await base.StartAsync(cancellationToken);
            server = new Server
            {
                Services = { Remoting.BindService(EndpointReader) },
                Ports = { new ServerPort(Hostname, Port, RemoteConfig.ServerCredentials) }
            };
            server.Start();

            var boundPort = server.Ports.Single().BoundPort;
            ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname ?? Hostname, RemoteConfig.AdvertisedPort ?? boundPort);
        }

        public override async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Logger.LogInformation("Stopping base");
            await base.StopAsync(cancellationToken);
            Logger.LogInformation("Stopping server");
            await server.KillAsync();
        }
    }
}