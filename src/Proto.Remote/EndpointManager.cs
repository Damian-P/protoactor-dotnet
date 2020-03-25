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
        private CancellationTokenSource cancellationTokenSource;
        public CancellationToken CancellationToken { get { return cancellationTokenSource.Token; } }
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
            cancellationTokenSource = new CancellationTokenSource();
            _endpointTermEvnSub = _actorSystem.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated);
            _endpointConnEvnSub = _actorSystem.EventStream.Subscribe<EndpointConnectedEvent>(OnEndpointConnected);
            Logger.LogDebug("Started EndpointManager");
        }

        public async Task StopAsync()
        {
            cancellationTokenSource.Cancel();
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
        }

        private void OnEndpointConnected(EndpointConnectedEvent msg)
        {
            var endpoint = EnsureConnected(msg.Address);
            endpoint.SendSystemMessage(_actorSystem, msg);
        }

        public void RemoteTerminate(RemoteTerminate msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _actorSystem.Root.Send(endpoint, msg);
        }

        public void RemoteWatch(RemoteWatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _actorSystem.Root.Send(endpoint, msg);
        }

        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _actorSystem.Root.Send(endpoint, msg);
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
            var endpointActor = _actorSystem.Root.SpawnNamed(endpointActorProps, $"endpoint-{address}");
            return endpointActor;
        }
    }
}