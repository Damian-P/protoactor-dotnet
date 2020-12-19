// -----------------------------------------------------------------------
//   <copyright file="EndpointManager.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class EndpointManager
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointManager>();
        private readonly ConcurrentDictionary<string, IEndpoint> _connections = new();
        private readonly ConcurrentDictionary<string, DateTime> _blackList = new();
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly ActorSystem _system;
        private readonly EventStreamSubscription<object> _endpointTerminatedEvnSub;
        private readonly RemoteConfigBase _remoteConfig;
        private readonly IChannelProvider _channelProvider;
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;
        public PID? ActivatorPid { get; private set; }

        public EndpointManager(ActorSystem system, RemoteConfigBase remoteConfig, IChannelProvider channelProvider)
        {
            _system = system;
            _system.ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(_system, this, pid));
            _remoteConfig = remoteConfig;
            _channelProvider = channelProvider;
            _endpointTerminatedEvnSub = _system.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated);
        }

        public void Start() => SpawnActivator();

        public async Task StopAsync()
        {
            if (CancellationToken.IsCancellationRequested) return;
            Logger.LogDebug("Stopping");

            _system.EventStream.Unsubscribe(_endpointTerminatedEvnSub);

            _cancellationTokenSource.Cancel();

            await Task.WhenAll(_connections.Values.Select(async endpoint => await endpoint.DisposeAsync().ConfigureAwait(false)));

            _connections.Clear();

            StopActivator();

            Logger.LogDebug("Stopped");
        }

        private async Task OnEndpointTerminated(EndpointTerminatedEvent evt)
        {
            if (_remoteConfig.BlackListingDuration.HasValue && _blackList.TryAdd(evt.Address, DateTime.UtcNow))
            {
                Logger.LogDebug("Blacklisting {address} for {duration} s", evt.Address, _remoteConfig.BlackListingDuration.Value.TotalSeconds);
                _ = Task.Run(async () => {

                    await Task.Delay(_remoteConfig.BlackListingDuration.Value, CancellationToken).ConfigureAwait(false);
                    _blackList.TryRemove(evt.Address, out var _);
                    Logger.LogDebug("Removed {address} from blacklist after {duration} s", evt.Address, _remoteConfig.BlackListingDuration.Value.TotalSeconds);
                }, CancellationToken).ConfigureAwait(false);
            }
            if (_connections.TryRemove(evt.Address, out var endpoint))
            {
                await endpoint.DisposeAsync().ConfigureAwait(false);
            }
        }

        public IEndpoint? GetEndpoint(string address)
        {
            if (CancellationToken.IsCancellationRequested || IsBlackListed(address)) return null;
            return _connections.GetOrAdd(address, v => {
                Logger.LogDebug("Requesting new endpoint for {Address}", v);
                var endpoint = new Endpoint(_system, v, _remoteConfig, _channelProvider);
                Logger.LogDebug("Created new endpoint for {Address}", v);
                return endpoint;
            });
        }

        private bool IsBlackListed(string address) => _blackList.ContainsKey(address);

        private void SpawnActivator()
        {
            var props = Props.FromProducer(() => new Activator(_remoteConfig, _system))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            ActivatorPid = _system.Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => _system.Root.Stop(ActivatorPid);
    }
}