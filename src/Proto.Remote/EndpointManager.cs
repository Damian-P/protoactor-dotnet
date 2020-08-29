﻿// -----------------------------------------------------------------------
//   <copyright file="EndpointManager.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using Proto.Mailbox;

namespace Proto.Remote
{
    public class EndpointManager
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointManager>();

        private readonly ConnectionRegistry _connections = new ConnectionRegistry();
        private readonly IRemote _remote;
        private readonly ActorSystem _system;
        private Subscription<object>? _endpointConnEvnSub;
        private PID? _endpointSupervisor;
        private Subscription<object>? _endpointTermEvnSub;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        public EndpointManager(IRemote remote, ActorSystem system)
        {
            _remote = remote;
            _system = system;
        }

        public void Start()
        {
            Logger.LogDebug("[EndpointManager] Started");

            var props = Props
                .FromProducer(() => new EndpointSupervisor(_remote, _system))
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy)
                .WithDispatcher(Dispatchers.SynchronousDispatcher);

            _endpointSupervisor = _system.Root.SpawnNamed(props, "EndpointSupervisor");
            _endpointTermEvnSub = _system.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated);
            _endpointConnEvnSub = _system.EventStream.Subscribe<EndpointConnectedEvent>(OnEndpointConnected);
        }

        public void Stop()
        {
            if (_cancellationTokenSource.IsCancellationRequested) return;
            _cancellationTokenSource.Cancel();

            _system.EventStream.Unsubscribe(_endpointTermEvnSub);
            _system.EventStream.Unsubscribe(_endpointConnEvnSub);

            _connections.Clear();
            _system.Root.Stop(_endpointSupervisor);
            Logger.LogDebug("[EndpointManager] Stopped");
        }

        private void OnEndpointTerminated(EndpointTerminatedEvent msg)
        {
            Logger.LogDebug("[EndpointManager] Endpoint {Address} terminated removing from connections", msg.Address);

            if (!_connections.TryRemove(msg.Address, out var v))
            {
                return;
            }

            var endpoint = v.Value;
            _system.Root.Send(endpoint.Watcher, msg);
            _system.Root.Send(endpoint.Writer, msg);
        }

        private void OnEndpointConnected(EndpointConnectedEvent msg)
        {
            var endpoint = EnsureConnected(msg.Address);
            _system.Root.Send(endpoint.Watcher, msg);
            endpoint.Writer.SendSystemMessage(_system, msg);
        }

        public void RemoteTerminate(RemoteTerminate msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _system.Root.Send(endpoint.Watcher, msg);
        }

        public void RemoteWatch(RemoteWatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _system.Root.Send(endpoint.Watcher, msg);
        }

        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            _system.Root.Send(endpoint.Watcher, msg);
        }

        public void RemoteDeliver(RemoteDeliver msg)
        {
            if (string.IsNullOrWhiteSpace(msg.Target.Address))
                throw new ArgumentOutOfRangeException("Target");

            var endpoint = EnsureConnected(msg.Target.Address);

            Logger.LogDebug(
                "[EndpointManager] Forwarding message {Message} from {From} for {Address} through EndpointWriter {Writer}",
                msg.Message?.GetType(), msg.Sender?.Address, msg.Target?.Address, endpoint.Writer
            );
            _system.Root.Send(endpoint.Writer, msg);
        }

        private Endpoint EnsureConnected(string address)
        {
            var conn = _connections.GetOrAdd(
                address, v =>
                    new Lazy<Endpoint>(
                        () =>
                        {
                            Logger.LogDebug("[EndpointManager] Requesting new endpoint for {Address}", v);

                            var endpoint = _system.Root.RequestAsync<Endpoint>(_endpointSupervisor!, v).Result;

                            Logger.LogDebug("[EndpointManager] Created new endpoint for {Address}", v);

                            return endpoint;
                        }
                    )
            );
            return conn.Value;
        }

        private class ConnectionRegistry : ConcurrentDictionary<string, Lazy<Endpoint>>
        {
        }
    }
}