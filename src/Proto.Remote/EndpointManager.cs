// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class Endpoint
    {
        public Endpoint(PID writer, PID watcher)
        {
            Writer = writer;
            Watcher = watcher;
        }

        public PID Writer { get; }
        public PID Watcher { get; }
    }

    public abstract class EndpointManager
    {
        private class ConnectionRegistry : ConcurrentDictionary<string, Lazy<Endpoint>> { }

        private static readonly ILogger Logger = Log.CreateLogger(typeof(EndpointManager).FullName);

        private readonly ConnectionRegistry Connections = new ConnectionRegistry();
        private readonly RemoteActorSystemBase Remote;
        private PID endpointSupervisor;
        private Subscription<object> endpointTermEvnSub;
        private Subscription<object> endpointConnEvnSub;

        public EndpointManager(RemoteActorSystemBase remoteActorSystem)
        {
            Remote = remoteActorSystem;

        }
        public abstract EndpointSupervisor GetEndpointSupervisor();
        public void Start()
        {
            Logger.LogDebug("Started EndpointManager");

            var props = Props
                .FromProducer(() => GetEndpointSupervisor())
                .WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy)
                .WithDispatcher(Mailbox.Dispatchers.SynchronousDispatcher);

            endpointSupervisor = Remote.Root.SpawnNamed(props, "EndpointSupervisor");
            endpointTermEvnSub = Remote.EventStream.Subscribe<EndpointTerminatedEvent>(OnEndpointTerminated);
            endpointConnEvnSub = Remote.EventStream.Subscribe<EndpointConnectedEvent>(OnEndpointConnected);
        }

        public void Stop()
        {
            Logger.LogDebug("Stopping EndpointManager");
            Remote.EventStream.Unsubscribe(endpointTermEvnSub.Id);
            Remote.EventStream.Unsubscribe(endpointConnEvnSub.Id);

            Connections.Clear();
            Remote.Root.Stop(endpointSupervisor);
            Logger.LogDebug("Stopped EndpointManager");
        }

        private void OnEndpointTerminated(EndpointTerminatedEvent msg)
        {
            Logger.LogDebug("Endpoint {Address} terminated removing from connections", msg.Address);

            if (!Connections.TryRemove(msg.Address, out var v)) return;

            var endpoint = v.Value;
            Remote.Root.Send(endpoint.Watcher, msg);
            Remote.Root.Send(endpoint.Writer, msg);
        }

        private void OnEndpointConnected(EndpointConnectedEvent msg)
        {
            var endpoint = EnsureConnected(msg.Address);
            Remote.Root.Send(endpoint.Watcher, msg);
            endpoint.Writer.SendSystemMessage(Remote, msg);
        }

        public void RemoteTerminate(RemoteTerminate msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            Remote.Root.Send(endpoint.Watcher, msg);
        }

        public void RemoteWatch(RemoteWatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            Remote.Root.Send(endpoint.Watcher, msg);
        }

        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            var endpoint = EnsureConnected(msg.Watchee.Address);
            Remote.Root.Send(endpoint.Watcher, msg);
        }

        public void RemoteDeliver(RemoteDeliver msg)
        {
            var endpoint = EnsureConnected(msg.Target.Address);

            Logger.LogDebug(
                "Forwarding message {Message} from {From} for {Address} through EndpointWriter {Writer}",
                msg.Message?.GetType(), msg.Sender?.Address, msg.Target?.Address, endpoint.Writer
            );
            Remote.Root.Send(endpoint.Writer, msg);
        }

        private Endpoint EnsureConnected(string address)
        {
            var conn = Connections.GetOrAdd(
                address, v =>
                    new Lazy<Endpoint>(
                        () =>
                        {
                            Logger.LogDebug("Requesting new endpoint for {Address}", v);

                            var endpoint = Remote.Root.RequestAsync<Endpoint>(endpointSupervisor, v).Result;

                            Logger.LogDebug("Created new endpoint for {Address}", v);

                            return endpoint;
                        }
                    )
            );
            return conn.Value;
        }
    }
}
