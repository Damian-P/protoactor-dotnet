// -----------------------------------------------------------------------
//   <copyright file="EndpointManager.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Linq;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using System.Runtime.Serialization;

namespace Proto.Remote
{
    public class EndpointManager
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointManager>();
        public const string Address = "EndpointManager";

        private class ConnectionRegistry : ConcurrentDictionary<string, Lazy<PID>>
        {
        }

        private readonly ConnectionRegistry _connections = new ConnectionRegistry();
        private readonly Remote _remote;
        private Subscription<object> _endpointTermEvnSub;
        private Subscription<object> _endpointConnEvnSub;

        public EndpointManager(Remote remote)
        {
            this._remote = remote;
        }

        public void Start()
        {
            _endpointTermEvnSub = _remote.System.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated);
            _endpointConnEvnSub = _remote.System.EventStream.Subscribe<EndpointConnectedEvent>(OnEndpointConnected);
            Logger.LogDebug("Started EndpointManager");
        }

        public void Stop()
        {
            foreach (var (address, connection) in _connections)
            {
                connection.Value.SendSystemMessage(_remote.System, new EndpointTerminatedEvent {Address = address});
            }

            _connections.Clear();
            _remote.System.EventStream.Unsubscribe(_endpointTermEvnSub.Id);
            _remote.System.EventStream.Unsubscribe(_endpointConnEvnSub.Id);
            Logger.LogDebug("Stopped EndpointManager");
        }

        private void OnEndpointTerminated(EndpointTerminatedEvent msg)
        {
            Logger.LogDebug("Endpoint {Address} terminated removing from connections", msg.Address);

            if (!_connections.TryRemove(msg.Address, out var v)) return;

            var endpoint = v.Value;
            endpoint.SendSystemMessage(_remote.System, msg);
        }

        private void OnEndpointConnected(EndpointConnectedEvent msg)
        {
            var endpoint = EnsureConnected(msg.Address);
            endpoint.SendSystemMessage(_remote.System, msg);
        }

        public void RemoteTerminate(RemoteTerminate msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _remote.System.Root.Send(endpoint, msg);
        }

        public void RemoteWatch(RemoteWatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _remote.System.Root.Send(endpoint, msg);
        }

        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _remote.System.Root.Send(endpoint, msg);
        }

        public void RemoteDeliver(RemoteDeliver msg)
        {
            var endpoint = EnsureConnected(msg.Target.Address);

            Logger.LogDebug(
                "Forwarding message {Message} from {From} for {Address} through EndpointWriter {Writer}",
                msg.Message?.GetType(), msg.Sender?.Address, msg.Target?.Address, endpoint
            );
            _remote.System.Root.Send(endpoint, msg);
        }

        private PID EnsureConnected(string address)
        {
            var conn = _connections.GetOrAdd(
                address, v =>
                    new Lazy<PID>(
                        () =>
                        {
                            Logger.LogDebug("Requesting new endpoint for {Address}", v);

                            var endpoint = SpawnEndpointActor(_remote, address);

                            Logger.LogDebug("Created new endpoint for {Address}", v);

                            return endpoint;
                        }
                    )
            );
            return conn.Value;
        }

        private static PID SpawnEndpointActor(Remote remote, string address)
        {
            var endpointActorProps =
                Props.FromProducer(
                        () => new EndpointActor(
                            remote,
                            address
                        )
                    )
                    .WithMailbox(() => new EndpointWriterMailbox(remote.System,
                            remote.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize
                        )
                    ).WithGuardianSupervisorStrategy(new EndpointSupervisorStrategy(address, remote));
            var writer = remote.System.Root.Spawn(endpointActorProps);
            return writer;
        }
    }
}