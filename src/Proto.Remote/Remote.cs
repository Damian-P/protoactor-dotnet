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
    public interface IRemote
    {
        RemoteConfig Config { get; }
        ActorSystem System { get; }
        // Only used in tests ?
        void SendMessage(PID pid, object msg, int serializerId);
        Task ShutdownAsync(bool graceful = true);
        Task StartAsync();
    }

    [PublicAPI]
    public class Remote : IRemote
    {
        private static readonly ILogger Logger = Log.CreateLogger<Remote>();

        private readonly ActorSystem _system;
        private EndpointManager _endpointManager = null!;
        private EndpointReader _endpointReader = null!;
        private HealthServiceImpl _healthCheck = null!;
        private Server _server = null!;

        public Remote(ActorSystem system,RemoteConfig config)
        {
            _system = system;
            Config = config;
        }

        public RemoteConfig Config { get; private set; }
        public ActorSystem System { get => _system; }
        public Task StartAsync()
        {
            var config = Config;
            _endpointManager = new EndpointManager(_system, Config);
            _endpointReader = new EndpointReader(_system, _endpointManager, config.Serialization);
            _healthCheck = new HealthServiceImpl();
            _system.ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(_system, _endpointManager, pid));

            _server = new Server
            {
                Services =
                {
                    Remoting.BindService(_endpointReader),
                    Health.BindService(_healthCheck)
                },
                Ports = { new ServerPort(config.Host, config.Port, config.ServerCredentials) }
            };
            _server.Start();

            var boundPort = _server.Ports.Single().BoundPort;
            _system.SetAddress(config.AdvertisedHostname ?? config.Host, config.AdvertisedPort ?? boundPort
            );
            _endpointManager.Start();

            Logger.LogDebug("Starting Proto.Actor server on {Host}:{Port} ({Address})", config.Host, boundPort,
                _system.Address
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
                    _system.Address, graceful
                );
            }
            catch (Exception ex)
            {
                await _server.KillAsync();

                Logger.LogError(
                    ex, "Proto.Actor server stopped on {Address} with error: {Message}",
                    _system.Address, ex.Message
                );
            }
        }

        // Only used in tests ?
        public void SendMessage(PID pid, object msg, int serializerId)
        {
            _endpointManager.SendMessage(pid, msg, serializerId);
        }
    }

    public static class Extensions
    {
        /// <summary>
        ///     Spawn a remote actor with auto-generated name
        /// </summary>
        /// <param name="address">Remote node address</param>
        /// <param name="kind">Actor kind, must be known on the remote node</param>
        /// <param name="timeout">Timeout for the confirmation to be received from the remote node</param>
        /// <returns></returns>
        public static Task<ActorPidResponse> SpawnAsync(this IRemote remote, string address, string kind, TimeSpan timeout) =>
            SpawnNamedAsync(remote, address, "", kind, timeout);

        /// <summary>
        ///     Spawn a remote actor with a name
        /// </summary>
        /// <param name="address">Remote node address</param>
        /// <param name="name">Remote actor name</param>
        /// <param name="kind">Actor kind, must be known on the remote node</param>
        /// <param name="timeout">Timeout for the confirmation to be received from the remote node</param>
        /// <returns></returns>
        public static async Task<ActorPidResponse> SpawnNamedAsync(this IRemote remote, string address, string name, string kind, TimeSpan timeout)
        {
            var activator = ActivatorForAddress(address);

            var res = await remote.System.Root.RequestAsync<ActorPidResponse>(
                activator, new ActorPidRequest
                {
                    Kind = kind,
                    Name = name
                }, timeout
            );

            return res;

            static PID ActivatorForAddress(string address) => PID.FromAddress(address, "activator");
        }
    }
}