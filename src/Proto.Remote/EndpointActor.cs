// -----------------------------------------------------------------------
//   <copyright file="EndpointActor.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace Proto.Remote
{
    public class EndpointActor : IActor
    {
        private readonly ILogger Logger = Log.CreateLogger<EndpointActor>();
        private readonly IChannelProvider _channelProvider;
        private readonly RemoteConfig _remoteConfig;
        private readonly Behavior _behavior;
        private readonly EndpointManager _endpointManager;
        private readonly Serialization _serialization;
        private ChannelBase? _channel;
        private AsyncDuplexStreamingCall<MessageBatch, Unit>? _stream;
        private Remoting.RemotingClient? _client;
        private int _serializerId;
        private readonly Dictionary<string, HashSet<PID>> _watchedActors = new Dictionary<string, HashSet<PID>>();
        private readonly string _address;
        public EndpointActor(string address, ActorSystem system, EndpointManager endpointManager,
            IChannelProvider channelProvider, RemoteConfig remoteConfig, Serialization serialization)
        {
            _address = address;
            _endpointManager = endpointManager;
            _channelProvider = channelProvider;
            _remoteConfig = remoteConfig;
            _serialization = serialization;
            _behavior = new Behavior(ConnectingAsync);
        }
        private static Task Ignore => Actor.Done;
        public Task ReceiveAsync(IContext context) => _behavior.ReceiveAsync(context);
        private Task ConnectingAsync(IContext context) =>
            context.Message switch
            {
                Started _ => ConnectAsync(context),
                Stopped _ => ShutDownChannel(),
                Restarting _ => ShutDownChannel(),
                _ => Ignore
            };
        private Task ConnectedAsync(IContext context) =>
            context.Message switch
            {
                RemoteTerminate msg => RemoteTerminate(context, msg),
                EndpointErrorEvent msg => EndpointError(msg),
                RemoteUnwatch msg => RemoteUnwatch(msg),
                RemoteWatch msg => RemoteWatch(msg),
                Restarting _ => EndpointTerminated(context),
                Stopped _ => EndpointTerminated(context),
                IEnumerable<RemoteDeliver> m => RemoteDeliver(m, context),
                _ => Ignore
            };
        private async Task ConnectAsync(IContext context)
        {
            Logger.LogDebug("[EndpointActor] Connecting to address {Address}", _address);
            try
            {
                _channel = _channelProvider.GetChannel(_address);
            }
            catch (Exception e)
            {
                Logger.LogError(e, "[EndpointActor] Error connecting to {_address}.", _address);
                throw e;
            }

            _client = new Remoting.RemotingClient(_channel);

            Logger.LogDebug("[EndpointActor] Created channel and client for address {Address}", _address);

            var res = await _client.ConnectAsync(new ConnectRequest());
            _serializerId = res.DefaultSerializerId;
            _stream = _client.Receive(_remoteConfig.CallOptions);

            Logger.LogDebug("[EndpointActor] Connected client for address {Address}", _address);

            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await _stream.ResponseStream.MoveNext();
                        Logger.LogDebug("[EndpointActor] {Address} disconnected", _address);
                        var terminated = new EndpointTerminatedEvent
                        {
                            Address = _address
                        };
                        context.System.EventStream.Publish(terminated);
                    }
                    catch (Exception x)
                    {
                        Logger.LogError(x, "[EndpointActor] Lost connection to address {Address}", _address);
                        var endpointError = new EndpointErrorEvent
                        {
                            Address = _address,
                            Exception = x
                        };
                        context.System.EventStream.Publish(endpointError);
                    }
                }
            );

            Logger.LogDebug("[EndpointActor] Created reader for address {Address}", _address);

            var connected = new EndpointConnectedEvent
            {
                Address = _address
            };
            context.System.EventStream.Publish(connected);

            Logger.LogDebug("[EndpointActor] Connected to address {Address}", _address);
            _behavior.Become(ConnectedAsync);
        }
        private async Task ShutDownChannel()
        {
            if (_stream != null)
                await _stream.RequestStream.CompleteAsync();
            if (_channel != null)
            {
                await _channel.ShutdownAsync();
            }
        }
        private Task EndpointError(EndpointErrorEvent evt)
        {
            throw evt.Exception;
        }
        private Task EndpointTerminated(IContext context)
        {
            Logger.LogDebug("[EndpointActor] Handle terminated address {Address}", _address);

            foreach (var (id, pidSet) in _watchedActors)
            {
                var watcherPid = new PID(context.System.Address, id);
                var watcherRef = context.System.ProcessRegistry.Get(watcherPid);

                if (watcherRef == context.System.DeadLetter)
                {
                    continue;
                }

                foreach (var t in pidSet.Select(
                    pid => new Terminated
                    {
                        Who = pid,
                        AddressTerminated = true
                    }
                ))
                {
                    //send the address Terminated event to the Watcher
                    watcherPid.SendSystemMessage(context.System, t);
                }
            }
            _watchedActors.Clear();
            return ShutDownChannel();
        }
        private Task RemoteTerminate(IContext context, RemoteTerminate msg)
        {
            if (_watchedActors.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Remove(msg.Watchee);

                if (pidSet.Count == 0)
                {
                    _watchedActors.Remove(msg.Watcher.Id);
                }
            }

            //create a terminated event for the Watched actor
            var t = new Terminated { Who = msg.Watchee };

            //send the address Terminated event to the Watcher
            msg.Watcher.SendSystemMessage(context.System, t);
            return Actor.Done;
        }
        private Task RemoteUnwatch(RemoteUnwatch msg)
        {
            if (_watchedActors.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Remove(msg.Watchee);

                if (pidSet.Count == 0)
                {
                    _watchedActors.Remove(msg.Watcher.Id);
                }
            }

            var w = new Unwatch(msg.Watcher);
            _endpointManager.SendMessage(msg.Watchee, w, -1);
            return Actor.Done;
        }
        private Task RemoteWatch(RemoteWatch msg)
        {
            if (_watchedActors.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Add(msg.Watchee);
            }
            else
            {
                _watchedActors[msg.Watcher.Id] = new HashSet<PID> { msg.Watchee };
            }

            var w = new Watch(msg.Watcher);
            _endpointManager.SendMessage(msg.Watchee, w, -1);
            return Actor.Done;
        }
        private Task RemoteDeliver(IEnumerable<RemoteDeliver> m, IContext context)
        {
            var envelopes = new List<MessageEnvelope>();
            var typeNames = new Dictionary<string, int>();
            var targetNames = new Dictionary<string, int>();
            var typeNameList = new List<string>();
            var targetNameList = new List<string>();

            foreach (var rd in m)
            {
                var targetName = rd.Target.Id;
                var serializerId = rd.SerializerId == -1 ? _serializerId : rd.SerializerId;

                if (!targetNames.TryGetValue(targetName, out var targetId))
                {
                    targetId = targetNames[targetName] = targetNames.Count;
                    targetNameList.Add(targetName);
                }

                var typeName = _serialization.GetTypeName(rd.Message, serializerId);

                if (!typeNames.TryGetValue(typeName, out var typeId))
                {
                    typeId = typeNames[typeName] = typeNames.Count;
                    typeNameList.Add(typeName);
                }

                MessageHeader? header = null;

                if (rd.Header != null && rd.Header.Count > 0)
                {
                    header = new MessageHeader();
                    header.HeaderData.Add(rd.Header.ToDictionary());
                }

                var bytes = _serialization.Serialize(rd.Message, _serializerId);

                var envelope = new MessageEnvelope
                {
                    MessageData = bytes,
                    Sender = rd.Sender,
                    Target = targetId,
                    TypeId = typeId,
                    SerializerId = _serializerId,
                    MessageHeader = header
                };

                envelopes.Add(envelope);
            }

            var batch = new MessageBatch();
            batch.TargetNames.AddRange(targetNameList);
            batch.TypeNames.AddRange(typeNameList);
            batch.Envelopes.AddRange(envelopes);

            Logger.LogDebug("[EndpointActor] Sending {Count} envelopes for {Address}", envelopes.Count, _address);

            return SendEnvelopesAsync(batch, context);
        }
        private async Task SendEnvelopesAsync(MessageBatch batch, IContext context)
        {
            if (_stream == null || _stream.RequestStream == null)
            {
                Logger.LogError(
                    "[EndpointActor] gRPC Failed to send to address {Address}, reason No Connection available"
                    , _address
                );
                return;
            }
            try
            {
                Logger.LogDebug("[EndpointActor] Writing batch to {Address}", _address);

                await _stream.RequestStream.WriteAsync(batch).ConfigureAwait(false);
            }
            catch (Exception x)
            {
                Logger.LogError(x, "[EndpointActor] gRPC Failed to send to address {Address}, reason {Message}", _address,
                    x.Message
                );
                context.Stash();
                throw;
            }
        }
    }
}