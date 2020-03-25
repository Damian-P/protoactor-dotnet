// -----------------------------------------------------------------------
//   <copyright file="EndpointManager.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Proto.Remote
{
    public class EndpointManager
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointManager>();
        public const string Address = "EndpointManager";

        private class ConnectionRegistry : ConcurrentDictionary<string, Lazy<PID>>
        {
        }
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public CancellationToken CancellationToken { get { return _cancellationTokenSource.Token; } }
        private readonly ConnectionRegistry _connections = new ConnectionRegistry();
        private readonly ActorSystem _actorSystem;
        private readonly RemoteConfig _remoteConfig;
        private readonly Serialization _serialization;
        private Subscription<object> _endpointTermEvnSub;
        private Subscription<object> _endpointConnEvnSub;
        private readonly IChannelProvider _channelProvider;

        public EndpointManager(ActorSystem actorSystem, RemoteConfig remoteConfig, Serialization serialization, IChannelProvider channelProvider)
        {
            _actorSystem = actorSystem;
            _remoteConfig = remoteConfig;
            _serialization = serialization;
            _channelProvider = channelProvider;
        }

        public void Start()
        {
            _endpointTermEvnSub = _actorSystem.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated);
            _endpointConnEvnSub = _actorSystem.EventStream.Subscribe<EndpointConnectedEvent>(OnEndpointConnected);
            _endpointConnEvnSub = _actorSystem.EventStream.Subscribe<EndpointCrashedEvent>(OnEndpointCrashed);
            Logger.LogDebug("Started EndpointManager");
        }

        public async Task StopAsync()
        {
            if (_cancellationTokenSource.IsCancellationRequested) return;
            _cancellationTokenSource.Cancel();
            _actorSystem.EventStream.Unsubscribe(_endpointTermEvnSub.Id);
            _actorSystem.EventStream.Unsubscribe(_endpointConnEvnSub.Id);
            foreach (var (address, connection) in _connections)
            {
                connection.Value.SendSystemMessage(_actorSystem, new EndpointTerminatedEvent { Address = address });
                await _actorSystem.Root.StopAsync(connection.Value);
            }
            _connections.Clear();

            Logger.LogDebug("Stopped EndpointManager");
        }

        private void OnEndpointTerminated(EndpointTerminatedEvent msg)
        {
            Logger.LogDebug("Endpoint {Address} terminated removing from connections", msg.Address);

            if (!_connections.TryRemove(msg.Address, out var v)) return;

            var endpoint = v.Value;
            endpoint.SendSystemMessage(_actorSystem, msg);
            _actorSystem.Root.Stop(endpoint);
        }

        private void OnEndpointConnected(EndpointConnectedEvent msg)
        {
            var endpoint = EnsureConnected(msg.Address);
            endpoint.SendSystemMessage(_actorSystem, msg);
        }

        private void OnEndpointCrashed(EndpointCrashedEvent msg)
        {
            Logger.LogWarning("Endpoint {Address} crashed", msg.Address);
        }

        public void RemoteTerminate(RemoteTerminate msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            endpoint.SendSystemMessage(_actorSystem, msg);
        }

        public void RemoteWatch(RemoteWatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            endpoint.SendSystemMessage(_actorSystem, msg);
        }

        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            endpoint.SendSystemMessage(_actorSystem, msg);
        }

        public void SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header, message, pid, sender, serializerId);
            RemoteDeliver(env);
        }

        private void RemoteDeliver(RemoteDeliver msg)
        {
            var endpoint = EnsureConnected(msg.Target.Address);

            Logger.LogDebug(
                "Forwarding message {Message} from {From} for {Address} through EndpointWriter {Writer}",
                msg.Message?.GetType(), msg.Sender?.Address, msg.Target?.Address, endpoint
            );
            _actorSystem.Root.Send(endpoint, msg);
        }

        private PID EnsureConnected(string address)
        {
            var conn = _connections.GetOrAdd(
                address, v =>
                    new Lazy<PID>(
                        () =>
                        {
                            Logger.LogDebug("Requesting new endpoint for {Address}", v);

                            var endpoint = SpawnEndpointActor(address);

                            Logger.LogDebug("Created new endpoint for {Address}", v);

                            return endpoint;
                        }
                    )
            );
            return conn.Value;
        }

        private PID SpawnEndpointActor(string address)
        {
            var endpointActorProps =
                Props.FromProducer(() =>
                        new EndpointActor(_actorSystem, _serialization, _remoteConfig, _channelProvider, address)
                    )
                    .WithMailbox(() => new EndpointWriterMailbox(_actorSystem,
                            _remoteConfig.EndpointWriterOptions.EndpointWriterBatchSize
                        )
                    )
                    .WithGuardianSupervisorStrategy(new EndpointSupervisorStrategy(address, _actorSystem, _remoteConfig.EndpointWriterOptions));
            var endpointActor = _actorSystem.Root.SpawnPrefix(endpointActorProps, $"endpoint-for-{address}");
            return endpointActor;
        }
    }
}