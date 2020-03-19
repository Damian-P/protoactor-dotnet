// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Proto.Remote.AspNetCore
{
    internal class EndpointWriter : IActor
    {
        private readonly ILogger Logger = Log.CreateLogger<EndpointWriter>();

        private int _serializerId;
        private readonly string _address;
        private GrpcChannel _channel;
        private Remoting.RemotingClient _client;
        private AsyncDuplexStreamingCall<MessageBatch, Unit> _stream;
        private IClientStreamWriter<MessageBatch> _streamWriter;
        public IRemoteActorSystem RemoteActorSystem { get; }

        public EndpointWriter(IRemoteActorSystem remoteActorSystem, string address)
        {
            _address = address;
            RemoteActorSystem = remoteActorSystem;
        }

        public Task ReceiveAsync(IContext context)
        {
            // _logger.LogInformation(context.Message.ToString());
            switch (context.Message)
            {
                case Started _:
                    Logger.LogDebug("Starting Endpoint Writer");
                    return StartedAsync();
                case Stopped _:
                    return StoppedAsync().ContinueWith(_ => Logger.LogDebug("Stopped EndpointWriter at {Address}", _address));
                case Restarting _:
                    return RestartingAsync();
                case EndpointTerminatedEvent _:
                    context.Stop(context.Self);
                    return Actor.Done;
                case IEnumerable<RemoteDeliver> messages:
                    return Deliver(messages, context);
                default:
                    return Actor.Done;
            }
        }

        private Task Deliver(IEnumerable<RemoteDeliver> m, IContext context)
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

                var typeName = RemoteActorSystem.Serialization.GetTypeName(rd.Message, serializerId);
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

                var bytes = RemoteActorSystem.Serialization.Serialize(rd.Message, serializerId);
                var envelope = new MessageEnvelope
                {
                    MessageData = bytes,
                    Sender = rd.Sender,
                    Target = targetId,
                    TypeId = typeId,
                    SerializerId = serializerId,
                    MessageHeader = header,
                };

                envelopes.Add(envelope);
            }

            var batch = new MessageBatch();
            batch.TargetNames.AddRange(targetNameList);
            batch.TypeNames.AddRange(typeNameList);
            batch.Envelopes.AddRange(envelopes);

            Logger.LogDebug($"EndpointWriter sending {envelopes.Count} rnvelopes for {_address}");
            return SendEnvelopesAsync(batch, context);
        }

        private async Task SendEnvelopesAsync(MessageBatch batch, IContext context)
        {
            if (_streamWriter == null)
            {
                Logger.LogError($"gRPC Failed to send to address {_address}, reason No Connection available");
                return;
            }
            try
            {
                Logger.LogTrace($"Writing batch to {_address}");
                await _streamWriter.WriteAsync(batch);
            }
            catch (Exception x)
            {
                context.Stash();
                Logger.LogError($"gRPC Failed to send to address {_address}, reason {x.Message}");
                throw;
            }
        }

        //shutdown channel before restarting
        private Task RestartingAsync() => ShutDownChannel();

        //shutdown channel before stopping
        private Task StoppedAsync() => ShutDownChannel();

        private async Task ShutDownChannel()
        {
            await _stream?.RequestStream?.CompleteAsync();
            await _channel.ShutdownAsync();
            _channel?.Dispose();
        }
        private async Task StartedAsync()
        {
            //TODO Remove this code for Production
            var httpClientHandler = new HttpClientHandler
            {
                // Return `true` to allow certificates that are untrusted/invalid
                ServerCertificateCustomValidationCallback =
                                                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            var httpClient = new HttpClient(httpClientHandler);
            var address = $"{_address}";
            Logger.LogInformation($"Connecting to address {address}");

            _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpClient = httpClient });
            _client = new Remoting.RemotingClient(_channel);

            Logger.LogDebug($"Created channel and client for address {_address}");

            var res = await _client.ConnectAsync(new ConnectRequest());
            _serializerId = res.DefaultSerializerId;

            var callOptions = new CallOptions();
            _stream = _client.Receive(callOptions);
            _streamWriter = _stream.RequestStream;

            Logger.LogInformation($"Connected client for address {_address}");

            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await foreach (var server in _stream.ResponseStream.ReadAllAsync())
                    {
                        if (!server.Alive)
                        {
                            Logger.LogInformation($"Lost connection to address {_address}");
                            var terminated = new EndpointTerminatedEvent
                            {
                                Address = _address
                            };
                            RemoteActorSystem.EventStream.Publish(terminated);
                        }
                    };
                }
                catch (Exception)
                {
                    Logger.LogInformation($"Lost connection to address {_address}");
                    var terminated = new EndpointTerminatedEvent
                    {
                        Address = _address
                    };
                    RemoteActorSystem.EventStream.Publish(terminated);
                }
            });

            var connected = new EndpointConnectedEvent
            {
                Address = _address
            };
            RemoteActorSystem.EventStream.Publish(connected);

            Logger.LogInformation($"Connected to address {_address}");
        }
    }
}
