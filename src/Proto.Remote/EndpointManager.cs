// -----------------------------------------------------------------------
//   <copyright file="EndpointManager.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto.Mailbox;

namespace Proto.Remote
{
    public class EndpointManager
    {
        private readonly ILogger _logger = Log.CreateLogger<EndpointManager>();
        private readonly ConcurrentDictionary<string, PID> _connections = new ConcurrentDictionary<string, PID>();
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly ActorSystem _system;
        private readonly IChannelProvider _channelProvider;
        private readonly Subscription<object> _endpointTermEvnSub;
        private readonly Subscription<object> _endpointConnectedEvnSub;
        private readonly Subscription<object> _endpointErrorEvnSub;
        private readonly RemoteConfig _remoteConfig;
        private readonly Serialization _serialization;
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;
        private readonly object _synLock = new object();

        public EndpointManager(ActorSystem system, RemoteConfig remoteConfig, Serialization serialization, IChannelProvider channelProvider)
        {
            _system = system;
            _remoteConfig = remoteConfig;
            _serialization = serialization;
            _channelProvider = channelProvider;
            _endpointTermEvnSub = _system.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated, Dispatchers.DefaultDispatcher);
            _endpointErrorEvnSub = _system.EventStream.Subscribe<EndpointErrorEvent>(OnEndpointError);
            _endpointConnectedEvnSub = _system.EventStream.Subscribe<EndpointConnectedEvent>(OnEndpointConnected);
        }

        private void OnEndpointError(EndpointErrorEvent evt)
        {
            lock (_synLock)
            {
                var endpoint = GetEndpoint(evt.Address);
                endpoint.SendSystemMessage(_system, evt);
            }
        }

        private void OnEndpointConnected(EndpointConnectedEvent evt)
        {
            lock (_synLock)
            {
                var endpoint = GetEndpoint(evt.Address);
                endpoint.SendSystemMessage(_system, evt);
            }
        }

        private void OnEndpointTerminated(EndpointTerminatedEvent evt)
        {
            _logger.LogInformation("[EndpointManager] {@Event}", evt);
            lock (_synLock)
            {
                if (_connections.TryRemove(evt.Address, out var endpoint))
                {
                    _system.Root.StopAsync(endpoint).GetAwaiter().GetResult();
                }
            }
        }

        public void RemoteTerminate(RemoteTerminate msg)
        {
            lock (_synLock)
            {
                var endpoint = GetEndpoint(msg.Watchee.Address);
                _system.Root.Send(endpoint, msg);
            }
        }

        public void RemoteWatch(RemoteWatch msg)
        {
            lock (_synLock)
            {
                var endpoint = GetEndpoint(msg.Watchee.Address);
                _system.Root.Send(endpoint, msg);
            }
        }

        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            lock (_synLock)
            {
                var endpoint = GetEndpoint(msg.Watchee.Address);
                _system.Root.Send(endpoint, msg);
            }
        }

        public void RemoteDeliver(RemoteDeliver msg)
        {
            if (string.IsNullOrWhiteSpace(msg.Target.Address))
                throw new ArgumentOutOfRangeException("Target");
            lock (_synLock)
            {
                var endpoint = GetEndpoint(msg.Target.Address);

                _logger.LogDebug(
                    "[EndpointManager] Forwarding message {Message} from {From} for {Address} through EndpointWriter {Writer}",
                    msg.Message?.GetType(), msg.Sender?.Address, msg.Target?.Address, endpoint
                );
                _system.Root.Send(endpoint, msg);
            }
        }

        private PID GetEndpoint(string address)
            => _connections.GetOrAdd(address, v =>
            {
                _logger.LogDebug("[EndpointManager] Requesting new endpoint for {Address}", v);
                var props = Props
                    .FromProducer(() => new EndpointActor(v, _system, this, _channelProvider, _remoteConfig, _serialization))
                    .WithMailbox(() => new EndpointWriterMailbox(_system, _remoteConfig.EndpointWriterOptions.EndpointWriterBatchSize))
                    .WithGuardianSupervisorStrategy(new EndpointSupervisorStrategy(v, _remoteConfig, _system));
                var endpointActorPid = _system.Root.SpawnNamed(props, $"endpoint-{v}");
                _logger.LogDebug("[EndpointManager] Created new endpoint for {Address}", v);
                return endpointActorPid;
            });

        public void SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);
            var env = new RemoteDeliver(header!, message, pid, sender!, serializerId);
            RemoteDeliver(env);
        }
        public void Stop()
        {
            lock (_synLock)
            {
                if (CancellationToken.IsCancellationRequested) return;
                _cancellationTokenSource.Cancel();
                _system.EventStream.Unsubscribe(_endpointTermEvnSub);
                _system.EventStream.Unsubscribe(_endpointConnectedEvnSub);
                _system.EventStream.Unsubscribe(_endpointErrorEvnSub);
                foreach (var endpoint in _connections.Values)
                {
                    _system.Root.StopAsync(endpoint).GetAwaiter().GetResult();
                }
                _connections.Clear();
                _logger.LogDebug("[EndpointManager] Stopped");
            }
        }
    }
}