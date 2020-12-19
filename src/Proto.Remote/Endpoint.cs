// -----------------------------------------------------------------------
//   <copyright file="Endpoint.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Channel = System.Threading.Channels.Channel;

namespace Proto.Remote
{
    public interface IEndpoint: IAsyncDisposable
    {
        void RemoteTerminate(RemoteTerminate msg);
        void RemoteUnwatch(RemoteUnwatch msg);
        void RemoteWatch(RemoteWatch msg);
        void SendMessage(PID pid, object msg, int serializerId);
    }

    public class Endpoint : IEndpoint
    {
        private readonly ILogger _logger = Log.CreateLogger<Endpoint>();
        private readonly Channel<RemoteDeliver> _remoteDelivers = Channel.CreateUnbounded<RemoteDeliver>();
        private readonly ConcurrentStack<IEnumerable<RemoteDeliver>> _stashedMessages = new();
        private readonly ActorSystem _system;
        private readonly RemoteConfigBase _remoteConfig;
        private readonly ConcurrentDictionary<string, HashSet<PID>> _watchedActors = new();
        private readonly string _address;
        private readonly IChannelProvider _channelProvider;
        private readonly CancellationTokenSource _tokenSource;
        private readonly CancellationToken _token;
        private ChannelBase? _channel;
        private AsyncDuplexStreamingCall<MessageBatch, Unit>? _stream;
        private int _serializerId = -1;
        private bool _connected = false;
        private readonly TimeSpan _backoff;
        private readonly int _maxNrOfRetries;
        private readonly Random _random = new();
        private readonly TimeSpan? _withinTimeSpan;
        private readonly Task _runner;
        private bool _disposed;
        public Endpoint(ActorSystem system, string address, RemoteConfigBase remoteConfig, IChannelProvider channelProvider)
        {
            _system = system;
            _address = address;
            _remoteConfig = remoteConfig;
            _channelProvider = channelProvider;
            _maxNrOfRetries = remoteConfig.EndpointWriterOptions.MaxRetries;
            _withinTimeSpan = remoteConfig.EndpointWriterOptions.RetryTimeSpan;
            _backoff = remoteConfig.EndpointWriterOptions.RetryBackOff;
            _tokenSource = new CancellationTokenSource();
            _token = _tokenSource.Token;
            _runner = Task.Run(Run);
        }
        public async ValueTask DisposeAsync()
        {
            lock (this)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _logger.LogDebug("Stopping {Address}", _address);
            TerminateEndpoint();
            FlushRemainingRemoteDeliveries();
            _tokenSource.Cancel();
            _remoteDelivers.Writer.Complete();
            await _runner;
            if (_stream != null)
                await _stream.RequestStream.CompleteAsync().ConfigureAwait(false);
            if (_channel != null)
                await _channel.ShutdownAsync().ConfigureAwait(false);
            _logger.LogDebug("Stopped {Address}", _address);
        }
        private void FlushRemainingRemoteDeliveries()
        {
            var droppedMessageCount = 0;
            while (_stashedMessages.TryPop(out var messages))
            {
                foreach (var rd in messages)
                {
                    OnRemoteDeliveryFlushed(rd);
                    droppedMessageCount++;
                }
            }
            while (_remoteDelivers.Reader.TryRead(out var rd))
            {
                OnRemoteDeliveryFlushed(rd);
                droppedMessageCount++;
            }
            if (droppedMessageCount > 0)
                _logger.LogInformation("Dropped {count} messages for {address}", droppedMessageCount, _address);
        }
        private void TerminateEndpoint()
        {
            foreach (var (id, pidSet) in _watchedActors)
            {
                var watcherPid = PID.FromAddress(_system.Address, id);
                var watcherRef = _system.ProcessRegistry.Get(watcherPid);

                if (watcherRef == _system.DeadLetter)
                {
                    continue;
                }

                foreach (var t in pidSet.Select(
                    pid => new Terminated
                    {
                        Who = pid,
                        Why = TerminatedReason.AddressTerminated
                    }
                ))
                {
                    //send the address Terminated event to the Watcher
                    watcherPid.SendSystemMessage(_system, t);
                }
            }
            _watchedActors.Clear();
        }
        private void OnRemoteDeliveryFlushed(RemoteDeliver rd)
        {
            if (rd.Message is Watch watch)
            {
                watch.Watcher.SendSystemMessage(_system, new Terminated
                {
                    Why = TerminatedReason.AddressTerminated,
                    Who = rd.Target
                });
                return;
            }
            _system.EventStream.Publish(new DeadLetterEvent(rd.Target, rd.Message, rd.Sender));
            if (rd.Sender is not null)
                _system.Root.Send(rd.Sender, rd.Message is PoisonPill
                ? new Terminated { Who = rd.Target, Why = TerminatedReason.AddressTerminated }
                : new DeadLetterResponse { Target = rd.Target }
            );
        }
        private async Task Run()
        {
            var rs = new RestartStatistics(0, null);
            while (!_token.IsCancellationRequested)
            {
                try
                {
                    if (!_connected)
                        await Connect();
                    rs.Reset();
                    while (_stashedMessages.TryPop(out var stashedMessages))
                        await Send(stashedMessages).ConfigureAwait(false);
                    var messages = new List<RemoteDeliver>();
                    while (await _remoteDelivers.Reader.WaitToReadAsync(_token).ConfigureAwait(false))
                    {
                        while (_remoteDelivers.Reader.TryRead(out var remoteDeliver))
                        {
                            messages.Add(remoteDeliver);
                            if (messages.Count > _remoteConfig.EndpointWriterOptions.EndpointWriterBatchSize) break;
                        }
                        await Send(messages).ConfigureAwait(false);
                        messages.Clear();
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogDebug("RemoteDeliver channel closed for {address}", _address);
                }
                catch (Exception e)
                {
                    if (ShouldStop(rs))
                    {
                        _logger.LogError("Stopping connection to address {Address} after retries expired because of {Reason}", _address, e.GetType().Name);
                        var terminated = new EndpointTerminatedEvent { Address = _address! };
                        _system.EventStream.Publish(terminated);
                        return;
                    }
                    else
                    {
                        var backoff = rs.FailureCount * (int) _backoff.TotalMilliseconds;
                        var noise = _random.Next(500);
                        var duration = TimeSpan.FromMilliseconds(backoff + noise);
                        await Task.Delay(duration);
                        _logger.LogWarning("Restarting endpoint connection {Actor} after {Duration} because of {Reason}", _address, duration, e.GetType().Name);
                    }
                }
            }
        }
        private bool ShouldStop(RestartStatistics rs)
        {
            if (_maxNrOfRetries == 0)
            {
                return true;
            }

            rs.Fail();

            if (rs.NumberOfFailures(_withinTimeSpan) > _maxNrOfRetries)
            {
                rs.Reset();
                return true;
            }

            return false;
        }
        private async Task Send(IEnumerable<RemoteDeliver> messages)
        {
            var batch = new MessageBatch();
            var typeNames = new Dictionary<string, int>();
            var targetNames = new Dictionary<string, int>();

            foreach (var rd in messages)
            {
                var targetName = rd.Target.Id;
                var serializerId = rd.SerializerId == -1 ? _serializerId : rd.SerializerId;

                if (!targetNames.TryGetValue(targetName, out var targetId))
                {
                    targetId = targetNames[targetName] = targetNames.Count;
                    batch.TargetNames.Add(targetName);
                }

                var typeName = _remoteConfig.Serialization.GetTypeName(rd.Message, serializerId);

                if (!typeNames.TryGetValue(typeName, out var typeId))
                {
                    typeId = typeNames[typeName] = typeNames.Count;
                    batch.TypeNames.Add(typeName);
                }

                MessageHeader? header = null;

                if (rd.Header != null && rd.Header.Count > 0)
                {
                    header = new MessageHeader();
                    header.HeaderData.Add(rd.Header.ToDictionary());
                }

                var bytes = _remoteConfig.Serialization.Serialize(rd.Message, serializerId);

                var envelope = new MessageEnvelope
                {
                    MessageData = bytes,
                    Sender = rd.Sender,
                    Target = targetId,
                    TypeId = typeId,
                    SerializerId = serializerId,
                    MessageHeader = header
                };

                batch.Envelopes.Add(envelope);
            }

            try
            {
                if (_stream is null)
                    throw new ArgumentNullException("Endpoint not ready");
                await _stream.RequestStream.WriteAsync(batch).ConfigureAwait(false);
            }
            catch (Exception x)
            {
                _logger.LogError(x, "Failed to send to address {Address}, reason {Message}", _address,
                    x.Message
                );
                _stashedMessages.Append(messages);
                throw;
            }
        }
        private async Task Connect()
        {
            _logger.LogDebug("Connecting to address {Address}", _address);
            _channel = _channelProvider.GetChannel(_address);
            var client = new Remoting.RemotingClient(_channel);

            _logger.LogDebug("Created channel and client for address {Address}", _address);

            var res = await client.ConnectAsync(new ConnectRequest(), cancellationToken: _token);
            _serializerId = res.DefaultSerializerId;
            _stream = client.Receive(_remoteConfig.CallOptions);

            _logger.LogDebug("Connected client for address {Address}", _address);

            _ = Task.Run(
                async () => {
                    try
                    {
                        await _stream.ResponseStream.MoveNext().ConfigureAwait(false);
                        _logger.LogDebug("{Address} disconnected", _address);
                        var terminated = new EndpointTerminatedEvent
                        {
                            Address = _address
                        };
                        _system.EventStream.Publish(terminated);
                        _connected = false;

                    }
                    catch (Exception x)
                    {
                        _logger.LogError("Lost connection to address {Address}", _address);
                        var endpointError = new EndpointErrorEvent
                        {
                            Address = _address,
                            Exception = x
                        };
                        _system.EventStream.Publish(endpointError);
                        _connected = false;
                    }
                }
            , _token);

            _logger.LogDebug("Created reader for address {Address}", _address);

            var connected = new EndpointConnectedEvent
            {
                Address = _address
            };
            _system.EventStream.Publish(connected);

            _connected = true;

            _logger.LogDebug("Connected to address {Address}", _address);
        }
        public void RemoteTerminate(RemoteTerminate msg)
        {
            if (_watchedActors.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Remove(msg.Watchee);

                if (pidSet.Count == 0)
                {
                    _watchedActors.TryRemove(msg.Watcher.Id, out _);
                }
            }

            //create a terminated event for the Watched actor
            var t = new Terminated { Who = msg.Watchee };

            //send the address Terminated event to the Watcher
            msg.Watcher.SendSystemMessage(_system, t);
        }
        public void RemoteWatch(RemoteWatch msg)
        {
            if (_disposed)
            {
                msg.Watcher.SendSystemMessage(_system, new Terminated
                {
                    Why = TerminatedReason.AddressTerminated,
                    Who = msg.Watchee
                });
                return;
            }
            if (_watchedActors.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Add(msg.Watchee);
            }
            else
            {
                _watchedActors[msg.Watcher.Id] = new HashSet<PID> { msg.Watchee };
            }

            var w = new Watch(msg.Watcher);
            SendMessage(msg.Watchee, w, -1);
        }
        public void RemoteUnwatch(RemoteUnwatch msg)
        {
            if (_disposed)
            {
                return;
            }
            if (_watchedActors.TryGetValue(msg.Watcher.Id, out var pidSet))
            {
                pidSet.Remove(msg.Watchee);

                if (pidSet.Count == 0)
                {
                    _watchedActors.TryRemove(msg.Watcher.Id, out _);
                }
            }

            var w = new Unwatch(msg.Watcher);
            SendMessage(msg.Watchee, w, -1);
        }
        public void SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);
            if (_disposed)
            {
                _system.EventStream.Publish(new DeadLetterEvent(pid, message, sender));
                if (sender is not null)
                    _system.Root.Send(sender, msg is PoisonPill
                    ? new Terminated { Who = pid, Why = TerminatedReason.AddressTerminated }
                    : new DeadLetterResponse { Target = pid }
                );
                return;
            }
            var env = new RemoteDeliver(header!, message, pid, sender!, serializerId);
#if DEBUG
            _logger.LogDebug("Forwarding message {Message} from {From} to {Target}",
                env.Message, env.Sender, env.Target
            );
#endif
            _ = _remoteDelivers.Writer.TryWrite(env);
        }
    }
}