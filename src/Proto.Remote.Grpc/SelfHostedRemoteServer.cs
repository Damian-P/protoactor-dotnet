// -----------------------------------------------------------------------
//   <copyright file="SelfHostedRemoteServerOverGrpc.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Proto.Remote.Grpc;

namespace Proto.Remote
{
    public class SelfHostedRemoteServerOverGrpc : Remote
    {
        private Server _server;

        public SelfHostedRemoteServerOverGrpc(ActorSystem system, string hostname, int port,
            Action<IRemoteConfiguration> configure = null)
            : base(system, new ChannelProvider(), hostname, port, configure)
        {
        }

        public override void Start()
        {
            base.Start();
            var endpointReader = new EndpointReader(_system, EndpointManager, Serialization);
            _server = new Server
            {
                Services = { Remoting.BindService(endpointReader) },
                Ports = { new ServerPort(_hostname, _port, RemoteConfig.ServerCredentials) }
            };

            _server.Start();

            var boundPort = _server.Ports.Single().BoundPort;

            _system.ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname
                                               ?? _hostname, RemoteConfig.AdvertisedPort ?? boundPort
            );

            Logger.LogDebug("Starting Proto.Actor server on {Host}:{Port} ({Address})", _hostname, boundPort,
                _system.ProcessRegistry.Address
            );
        }

        public override async Task Stop(bool graceful = true)
        {
            try
            {
                if (graceful)
                {
                    await base.Stop(graceful);
                    await _server.ShutdownAsync();
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