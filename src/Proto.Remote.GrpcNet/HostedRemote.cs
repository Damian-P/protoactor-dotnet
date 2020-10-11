// -----------------------------------------------------------------------
//   <copyright file="HostedRemote.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class HostedRemote : IRemote
    {
        private readonly ILogger _logger = Log.CreateLogger<HostedRemote>();
        private readonly Remote _remote;
        private readonly ActorSystem _system;
        private readonly RemoteConfig _remoteConfig;
        public IServerAddressesFeature? ServerAddressesFeature { get; set; }
        public Serialization Serialization { get; }
        public RemoteKindRegistry RemoteKindRegistry { get; }
        private readonly string _hostname;
        private readonly int _port;

        public HostedRemote(string hostname, int port, Remote remote, Serialization serialization, RemoteKindRegistry remoteKindRegistry, ActorSystem system, RemoteConfig remoteConfig)
        {
            _hostname = hostname;
            _port = port;
            _remote = remote;
            _system = system;
            _remoteConfig = remoteConfig;
            Serialization = serialization;
            RemoteKindRegistry = remoteKindRegistry;
        }
        public bool Started { get; private set; }
        public void Start()
        {
            if (Started) return;
            Started = true;
            var uri = ServerAddressesFeature!.Addresses.Where(a => a.Contains(_hostname)).Select(address => new Uri(address)).FirstOrDefault();
            var boundPort = uri?.Port ?? _port;
            
            _system.SetAddress(_remoteConfig.AdvertisedHostname ?? _hostname,
                    _remoteConfig.AdvertisedPort ?? boundPort 
                );
            _remote.Start();
            _logger.LogInformation("Starting Proto.Actor server on {Host}:{Port} ({Address})", _hostname, boundPort,
                _system.Address
            );

        }

        public async Task ShutdownAsync(bool graceful = true)
        {
            try
            {
                if (!Started) return;
                else Started = false;
                if (graceful)
                {
                    await _remote.ShutdownAsync(graceful);
                }
                _logger.LogDebug(
                    "Proto.Actor server stopped on {Address}. Graceful: {Graceful}",
                    _system.Address, graceful
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex, "Proto.Actor server stopped on {Address} with error: {Message}",
                    _system.Address, ex.Message
                );
                throw;
            }
        }


        public Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout)
        {
            return _remote.SpawnAsync(address, kind, timeout);
        }

        public Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout)
        {
            return _remote.SpawnNamedAsync(address, name, kind, timeout);
        }

        public void SendMessage(PID pid, object msg, int serializerId)
        {
            _remote.SendMessage(pid, msg, serializerId);
        }
    }
}