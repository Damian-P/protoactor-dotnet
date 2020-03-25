// -----------------------------------------------------------------------
//   <copyright file="EndpointActor.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Utils;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class EndpointActor : IActor
    {
        private readonly ActorSystem _actorSystem;
        private readonly Serialization _serialization;
        protected readonly RemoteConfig _remoteConfig;
        private readonly IChannelProvider _channelProvider;
        private readonly Dictionary<string, HashSet<PID>> _watched = new Dictionary<string, HashSet<PID>>();
        protected readonly string _address;
        protected static readonly ILogger Logger = Log.CreateLogger<EndpointActor>();
        private int _serializerId;
        private Remoting.RemotingClient _client;
        private AsyncDuplexStreamingCall<MessageBatch, Unit> _stream;
        private IClientStreamWriter<MessageBatch> _streamWriter;
        private ChannelBase _channel;


        public EndpointActor(ActorSystem actorSystem, Serialization serialization, RemoteConfig remoteConfig, IChannelProvider channelProvider, string address)
        {
            _actorSystem = actorSystem;
            _remoteConfig = remoteConfig;
            _channelProvider = channelProvider;
            _serialization = serialization;
            _address = address;
        }

        public async Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Started _:
                    await ConnectAsync().ConfigureAwait(false);
                    break;
                case Stopping _:
                    Logger.LogDebug("Stopping EndpointActor at {Address}", _address);
                    break;
                case Stopped _:
                    await ShutDownChannel().ConfigureAwait(false);
                    Logger.LogDebug("Stopped EndpointActor at {Address}", _address);
                    break;
                case Restarting _:
                    await ShutDownChannel().ConfigureAwait(false);
                    break;
                case EndpointTerminatedEvent _:
                    HandleTerminatedEndpoint(context);
                    break;
                case RemoteUnwatch remoteUnwatch:
                    HandleRemoteUnwatch(context, remoteUnwatch);
                    break;
                case RemoteWatch remoteWatch:
                    HandleRemoteWatch(context, remoteWatch);
                    break;
                case RemoteTerminate remoteTerminate:
                    HandleRemoteTerminate(context, remoteTerminate);
                    break;
                case IEnumerable<RemoteDeliver> remoteMessages:
                    await Deliver(context, remoteMessages).ConfigureAwait(false);
                    break;
            }
        }

        private async Task SendEnvelopesAsync(MessageBatch batch, IContext context)
        {
            if (_streamWriter == null)
            {
                Logger.LogError("gRPC Failed to send to address {Address}, reason No Connection available", _address);
                return;
            }
            try
            {
                Logger.LogDebug("Writing batch to {Address}", _address);
                await _streamWriter.WriteAsync(batch);
            }
            catch (Exception x)
            {
                context.Stash();
                Logger.LogError("gRPC Failed to send to address {Address}, reason {Message}", _address, x.Message);
                throw;
            }
        }

        protected async Task ShutDownChannel()
        {
            if (_stream != null)
                await _stream.RequestStream.CompleteAsync().ConfigureAwait(false);
            if (_channel != null)
            {
                await _channel.ShutdownAsync().ConfigureAwait(false);
            }
        }


        private async Task ConnectAsync()
        {
            Logger.LogDebug("Connecting to address {Address}", _address);

            _channel = _channelProvider.GetChannel(_remoteConfig, _address);
            _client = new Remoting.RemotingClient(_channel);

            Logger.LogDebug($"Created channel and client for address {_address}");

            var res = await _client.ConnectAsync(new ConnectRequest());
            _serializerId = res.DefaultSerializerId;


            _stream = _client.Receive(_remoteConfig.CallOptions);
            _streamWriter = _stream.RequestStream;

            Logger.LogDebug("Connected client for address {Address}", _address);

            var _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await _stream.ResponseStream.ForEachAsync(async i =>
                            {
                                if (!i.Alive)
                                {
                                    Logger.LogInformation("Lost connection to address {Address}", _address);
                                    await _stream.RequestStream.CompleteAsync();
                                    NotifyEndpointTermination();
                                }
                            }
                        ).ConfigureAwait(false);
                    }
                    catch (Exception x)
                    {
                        Logger.LogError(x, "Lost connection to address {Address}, reason {Message}", _address, x.Message
                        );
                        NotifyEndpointTermination();
                        throw;
                    }
                }
            );

            Logger.LogDebug("Created reader for address {Address}", _address);

            var connected = new EndpointConnectedEvent
            {
                Address = _address
            };
            _actorSystem.EventStream.Publish(connected);
            Logger.LogDebug("Connected to address {Address}", _address);
        }

        private void HandleRemoteTerminate(IContext context, RemoteTerminate msg)
        {
            if (_watched.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Remove(msg.Watchee);

                if (pidSet.Count == 0)
                {
                    _watched.Remove(msg.Watcher.Id);
                }
            }

            //create a terminated event for the Watched actor
            var t = new Terminated { Who = msg.Watchee };

            //send the address Terminated event to the Watcher
            msg.Watcher.SendSystemMessage(context.System, t);
        }

        private void HandleRemoteWatch(IContext context, RemoteWatch msg)
        {
            if (_watched.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Add(msg.Watchee);
            }
            else
            {
                _watched[msg.Watcher.Id] = new HashSet<PID> { msg.Watchee };
            }

            var w = new Watch(msg.Watcher);
            SendMessage(context, msg.Watchee, w, -1);
        }

        private void HandleRemoteUnwatch(IContext context, RemoteUnwatch msg)
        {
            if (_watched.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Remove(msg.Watchee);

                if (pidSet.Count == 0)
                {
                    _watched.Remove(msg.Watcher.Id);
                }
            }

            var w = new Unwatch(msg.Watcher);
            SendMessage(context, msg.Watchee, w, -1);
        }

        private void HandleTerminatedEndpoint(IContext context)
        {
            Logger.LogDebug("Handle terminated address {Address}", _address);
            foreach (var (id, pidSet) in _watched)
            {
                var watcherPid = new PID(_actorSystem.ProcessRegistry.Address, id);
                var watcherRef = _actorSystem.ProcessRegistry.Get(watcherPid);

                if (watcherRef == _actorSystem.DeadLetter) continue;

                foreach (var t in pidSet.Select(
                    pid => new Terminated
                    {
                        Who = pid,
                        AddressTerminated = true
                    }
                ))
                {
                    //send the address Terminated event to the Watcher
                    watcherPid.SendSystemMessage(_actorSystem, t);
                }
            }

            _watched.Clear();
            context.System.Root.Stop(context.Self);
        }

        private async Task Deliver(IContext context, IEnumerable<RemoteDeliver> remoteMessages)
        {
            var envelopes = new List<MessageEnvelope>();
            var typeNames = new Dictionary<string, int>();
            var targetNames = new Dictionary<string, int>();
            var typeNameList = new List<string>();
            var targetNameList = new List<string>();

            foreach (var rd in remoteMessages)
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

                MessageHeader header = null;

                if (rd.Header != null && rd.Header.Count > 0)
                {
                    header = new MessageHeader();
                    header.HeaderData.Add(rd.Header.ToDictionary());
                }

                var bytes = _serialization.Serialize(rd.Message, serializerId);

                var envelope = new MessageEnvelope
                {
                    MessageData = bytes,
                    Sender = rd.Sender,
                    Target = targetId,
                    TypeId = typeId,
                    SerializerId = serializerId,
                    MessageHeader = header
                };

                envelopes.Add(envelope);
            }

            var batch = new MessageBatch();
            batch.TargetNames.AddRange(targetNameList);
            batch.TypeNames.AddRange(typeNameList);
            batch.Envelopes.AddRange(envelopes);

            Logger.LogDebug(
                "EndpointWriter sending {Count} envelopes for {Address}",
                envelopes.Count, _address
            );

            await SendEnvelopesAsync(batch, context).ConfigureAwait(false);
        }

        protected void NotifyEndpointTermination()
        {
            var terminated = new EndpointTerminatedEvent
            {
                Address = _address
            };
            _actorSystem.EventStream.Publish(terminated);
        }

        public void SendMessage(IContext context, PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header, message, pid, sender, serializerId);

            context.System.Root.Send(context.Self, env);
        }
    }
}