// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Utils;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Mailbox;

namespace Proto.Remote.AspNetCore
{
    internal static class FakeApp
    {
        internal static void Main(string[] args) { }
    }

    public static class Extensions
    {
        public static IServiceCollection AddRemote(this IServiceCollection services,
            string hostname,
            int port,
            RemoteConfig remoteConfig = null,
            Action<ActorPropsRegistry> configureActorPropsRegistry = null,
            Action<RemoteKindRegistry> configureRemoteKindRegistry = null,
            Action<Serialization> configureSerialization = null)
        {
            services.AddSingleton<IRemoteActorSystem>(sp =>
            {
                var remoteActorSystem = new HostedRemoteActorSystem(hostname, port, remoteConfig);
                var remoteKindRegistry = new RemoteKindRegistry();
                configureRemoteKindRegistry?.Invoke(remoteActorSystem.RemoteKindRegistry);
                configureSerialization?.Invoke(remoteActorSystem.Serialization);
                return remoteActorSystem;
            });
            services.AddSingleton<EndpointReader>();
            return services;
        }
    }

    public class RemoteHostedServer : IHostedService
    {
        public RemoteHostedServer()
        {

        }
        public Task StartAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }

    public interface IRemoteActorSystem : IActorSystem
    {
        Serialization Serialization { get; }
        RemoteKindRegistry RemoteKindRegistry { get; }
        Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout);
        Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout);
        Task StartAsync(CancellationToken cancellationToken);
        Task StopAsync(CancellationToken cancellationToken);
    }

    public interface IInternalRemoteActorAsystem : IRemoteActorSystem
    {
        EndpointManager EndpointManager { get; }
        IRemoteConfig RemoteConfig { get; }
        void SendMessage(PID pid, object msg, int serializerId);
    }
    public class SelfHostedRemoteActorSystem : RemoteActorSystemBase
    {
        private IHost host;
        private Task task;

        public SelfHostedRemoteActorSystem(string hostname, int port, IRemoteConfig config = null) : base(hostname, port, config)
        {
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        }
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await base.StartAsync(cancellationToken);
            if (host != null) throw new InvalidOperationException("Already started");
            host = Host.CreateDefaultBuilder().UseConsoleLifetime().ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.ConfigureKestrel(serverOptions =>
                {
                    serverOptions.Listen(IPAddress.Any, Port, listenOptions =>
                    {
                        listenOptions.Protocols = HttpProtocols.Http2;
                    });
                });
                webBuilder.ConfigureLogging(builder =>
                {
                    builder
                        .SetMinimumLevel(LogLevel.Information)
                        .AddConsole()
                        .AddFilter("Grpc.AspNetCore.Server", LogLevel.Critical)
                        .AddFilter("Microsoft", LogLevel.Critical);
                });
                webBuilder.ConfigureServices(serviceCollection =>
                {
                    serviceCollection.AddGrpc();
                    serviceCollection.AddSingleton<IInternalRemoteActorAsystem>(sp =>
                    {
                        Log.SetLoggerFactory(sp.GetRequiredService<ILoggerFactory>());
                        return this;
                    });
                    serviceCollection.AddSingleton<IActorSystem>(sp =>
                    {
                        return this;
                    });
                    serviceCollection.AddSingleton<EndpointReader>(sp => this.EndpointReader);
                });
                webBuilder.Configure((context, app) =>
                {
                    Console.Write("Starting");
                    var serverAddressesFeature = app.ServerFeatures.Get<IServerAddressesFeature>();
                    var address = serverAddressesFeature.Addresses.FirstOrDefault();

                    app.UseRouting();

                    Console.WriteLine(string.Join(", ", serverAddressesFeature.Addresses));
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGrpcService<EndpointReader>();
                    });
                });

            })
            .Build();
            await host.StartAsync(cancellationToken);
        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {

            Console.WriteLine("stopping base");
            await base.StopAsync(cancellationToken);
            Console.WriteLine("stopping host");
            await host.StopAsync(cancellationToken);
            Console.WriteLine("Base stopped");
            host.Dispose();
            Console.WriteLine("Host disposed");
        }
    }
    public class EndpointReader : Remoting.RemotingBase
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointReader>();

        private bool _suspended;
        private readonly IInternalRemoteActorAsystem _system;

        public EndpointReader(IInternalRemoteActorAsystem system)
        {
            _system = system;
        }

        public override Task<ConnectResponse> Connect(ConnectRequest request, ServerCallContext context)
        {
            if (_suspended)
            {
                Logger.LogWarning("Attempt to connect to the suspended reader has been rejected");

                throw new RpcException(Status.DefaultCancelled, "Suspended");
            }

            Logger.LogDebug("Accepted connection request from {Remote} to {Local}", context.Peer, context.Host);

            return Task.FromResult(
                new ConnectResponse
                {
                    DefaultSerializerId = Serialization.DefaultSerializerId
                }
            );
        }

        public override Task Receive(
            IAsyncStreamReader<MessageBatch> requestStream,
            IServerStreamWriter<Unit> responseStream, ServerCallContext context
        )
        {
            var writeTask = Task.Run(async () =>
                {
                    while (true)
                    {
                        if (_suspended)
                        {
                            await responseStream.WriteAsync(new Unit() { Alive = false });
                            break;
                        }
                        else
                            await responseStream.WriteAsync(new Unit() { Alive = true });
                        await Task.Delay(100);
                    }

                }, context.CancellationToken);

            var targets = new PID[100];

            return requestStream.ForEachAsync(
                batch =>
                {
                    Logger.LogDebug("Received a batch of {Count} messages from {Remote}", batch.TargetNames.Count, context.Peer);

                    if (_suspended)
                        return Actor.Done;

                    //only grow pid lookup if needed
                    if (batch.TargetNames.Count > targets.Length)
                    {
                        targets = new PID[batch.TargetNames.Count];
                    }

                    for (var i = 0; i < batch.TargetNames.Count; i++)
                    {
                        targets[i] = new PID(_system.ProcessRegistry.Address, batch.TargetNames[i]);
                    }

                    var typeNames = batch.TypeNames.ToArray();

                    foreach (var envelope in batch.Envelopes)
                    {
                        var target = targets[envelope.Target];
                        var typeName = typeNames[envelope.TypeId];
                        var message = _system.Serialization.Deserialize(typeName, envelope.MessageData, envelope.SerializerId);

                        switch (message)
                        {
                            case Terminated msg:
                                {
                                    Logger.LogDebug("Forwarding remote endpoint termination request for {Who}", msg.Who);

                                    var rt = new RemoteTerminate(target, msg.Who);
                                    _system.EndpointManager.RemoteTerminate(rt);

                                    break;
                                }
                            case SystemMessage sys:
                                Logger.LogDebug("Forwarding remote system message {@Message}", sys);

                                target.SendSystemMessage(_system, sys);
                                break;
                            default:
                                {
                                    Proto.MessageHeader header = null;

                                    if (envelope.MessageHeader != null)
                                    {
                                        header = new Proto.MessageHeader(envelope.MessageHeader.HeaderData);
                                    }

                                    Logger.LogDebug("Forwarding remote user message {@Message}", message);
                                    var localEnvelope = new Proto.MessageEnvelope(message, envelope.Sender, header);
                                    _system.Root.Send(target, localEnvelope);
                                    break;
                                }
                        }
                    }

                    return Actor.Done;
                }
            );
        }

        public void Suspend(bool suspended)
        {
            Logger.LogDebug("EndpointReader suspended");
            _suspended = suspended;
        }
    }
    internal class HostedRemoteActorSystem : RemoteActorSystemBase, IHostedService
    {
        public HostedRemoteActorSystem(string hostname, int port, IRemoteConfig config) : base(hostname, port, config)
        {
        }
    }
    public abstract class RemoteActorSystemBase : ActorSystem, IInternalRemoteActorAsystem
    {
        private static readonly ILogger Logger = Log.CreateLogger(typeof(RemoteActorSystemBase).FullName);
        private PID _activatorPid;
        public Serialization Serialization { get; }
        public EndpointManager EndpointManager { get; }
        public IRemoteConfig RemoteConfig { get; }
        public RemoteKindRegistry RemoteKindRegistry { get; }
        public EndpointReader EndpointReader { get; }
        public string Hostname { get; }
        public int Port { get; }

        public RemoteActorSystemBase(string hostname, int port, IRemoteConfig config = null)
        {
            RemoteConfig = config ?? new RemoteConfig();
            Serialization = new Serialization();
            EndpointManager = new EndpointManager(this);
            EndpointReader = new EndpointReader(this);
            ProcessRegistry.RegisterHostResolver(pid => new RemoteProcess(this, pid));
            ProcessRegistry.SetAddress(RemoteConfig.AdvertisedHostname ?? hostname, RemoteConfig.AdvertisedPort ?? port);
            Hostname = hostname;
            Port = port;
        }

        private PID ActivatorForAddress(string address) => new PID(address, "activator");

        public Task<ActorPidResponse> SpawnAsync(string address, string kind, TimeSpan timeout) => SpawnNamedAsync(address, "", kind, timeout);

        public async Task<ActorPidResponse> SpawnNamedAsync(string address, string name, string kind, TimeSpan timeout)
        {
            var activator = ActivatorForAddress(address);

            var res = await Root.RequestAsync<ActorPidResponse>(
                activator, new ActorPidRequest
                {
                    Kind = kind,
                    Name = name
                }, timeout
            );

            return res;
        }
        public virtual Task StartAsync(CancellationToken cancellationToken)
        {
            EndpointManager.Start();
            SpawnActivator();
            Logger.LogDebug("Starting Proto.Actor server on {Host}:{Port} ({Address})", Hostname, Port, ProcessRegistry.Address);
            return Task.CompletedTask;
        }
        public virtual Task StopAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("Suspending EndpointReader");
            EndpointReader.Suspend(true);
            Console.WriteLine("Stopping EndpointManager");
            EndpointManager.Stop();
            Console.WriteLine("Stopping Activator");
            StopActivator();
            Console.WriteLine("Activator stopped");
            return Task.CompletedTask;
        }

        void IInternalRemoteActorAsystem.SendMessage(PID pid, object msg, int serializerId)
        {
            var (message, sender, header) = Proto.MessageEnvelope.Unwrap(msg);

            var env = new RemoteDeliver(header, message, pid, sender, serializerId);
            this.EndpointManager.RemoteDeliver(env);
        }

        private void SpawnActivator()
        {
            var props = Props.FromProducer(() => new Activator(this)).WithGuardianSupervisorStrategy(Supervision.AlwaysRestartStrategy);
            _activatorPid = Root.SpawnNamed(props, "activator");
        }

        private void StopActivator() => Root.Stop(_activatorPid);
    }
    public class Activator : IActor
    {
        private readonly IRemoteActorSystem _remoteActorSystem;
        public Activator(IRemoteActorSystem remoteActorSystem)
        {
            _remoteActorSystem = remoteActorSystem;
        }
        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case ActorPidRequest msg:
                    var props = _remoteActorSystem.RemoteKindRegistry.GetKnownKind(msg.Kind);
                    var name = msg.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = _remoteActorSystem.ProcessRegistry.NextId();
                    }

                    try
                    {
                        var pid = _remoteActorSystem.Root.SpawnNamed(props, name);
                        var response = new ActorPidResponse { Pid = pid };
                        context.Respond(response);
                    }
                    catch (ProcessNameExistException ex)
                    {
                        var response = new ActorPidResponse
                        {
                            Pid = ex.Pid,
                            StatusCode = (int)ResponseStatusCode.ProcessNameAlreadyExist
                        };
                        context.Respond(response);
                    }
                    catch (ActivatorException ex)
                    {
                        var response = new ActorPidResponse
                        {
                            StatusCode = ex.Code
                        };
                        context.Respond(response);

                        if (!ex.DoNotThrow)
                            throw;
                    }
                    catch
                    {
                        var response = new ActorPidResponse
                        {
                            StatusCode = (int)ResponseStatusCode.Error
                        };
                        context.Respond(response);

                        throw;
                    }
                    break;
            }
            return Actor.Done;
        }
    }
    internal class RemoteProcess : Process
    {
        private readonly PID _pid;
        private readonly IInternalRemoteActorAsystem _remoteActorSystem;

        public RemoteProcess(IInternalRemoteActorAsystem remoteActorSystem, PID pid) : base(remoteActorSystem)
        {
            _remoteActorSystem = remoteActorSystem;
            _pid = pid;
        }

        protected override void SendUserMessage(PID _, object message) => Send(message);

        protected override void SendSystemMessage(PID _, object message) => Send(message);

        private void Send(object msg)
        {
            switch (msg)
            {
                case Watch w:
                    {
                        var rw = new RemoteWatch(w.Watcher, _pid);
                        _remoteActorSystem.EndpointManager.RemoteWatch(rw);
                        break;
                    }
                case Unwatch uw:
                    {
                        var ruw = new RemoteUnwatch(uw.Watcher, _pid);
                        _remoteActorSystem.EndpointManager.RemoteUnwatch(ruw);
                        break;
                    }
                default:
                    _remoteActorSystem.SendMessage(_pid, msg, -1);
                    break;
            }
        }
    }
    public class EndpointManager
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

        public void Start()
        {
            Logger.LogDebug("Started EndpointManager");

            var props = Props
                .FromProducer(() => new EndpointSupervisor(Remote))
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
    public class EndpointSupervisor : IActor, ISupervisorStrategy
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointSupervisor>();

        private readonly int _maxNrOfRetries;
        private readonly Random _random = new Random();
        private readonly RemoteActorSystemBase Remote;
        private readonly TimeSpan? _withinTimeSpan;
        private CancellationTokenSource _cancelFutureRetries;

        private int _backoff;
        private string _address;

        public EndpointSupervisor(RemoteActorSystemBase remote)
        {
            Remote = remote;
            _maxNrOfRetries = remote.RemoteConfig.EndpointWriterOptions.MaxRetries;
            _withinTimeSpan = remote.RemoteConfig.EndpointWriterOptions.RetryTimeSpan;
            _backoff = remote.RemoteConfig.EndpointWriterOptions.RetryBackOffms;
        }

        public Task ReceiveAsync(IContext context)
        {
            if (context.Message is string address)
            {
                _address = address;
                var watcher = SpawnWatcher(address, context, Remote);
                var writer = SpawnWriter(address, context, Remote);
                _cancelFutureRetries = new CancellationTokenSource();
                context.Respond(new Endpoint(writer, watcher));
            }

            return Actor.Done;
        }

        public void HandleFailure(
            ISupervisor supervisor, PID child, RestartStatistics rs, Exception reason,
            object message
        )
        {
            if (ShouldStop(rs))
            {
                Logger.LogWarning(
                    "Stopping connection to address {Address} after retries expired because of {Reason}",
                    _address, reason
                );

                _cancelFutureRetries.Cancel();
                supervisor.StopChildren(child);
                Remote.ProcessRegistry.Remove(child); //TODO: work out why this hangs around in the process registry

                var terminated = new EndpointTerminatedEvent { Address = _address };
                Remote.EventStream.Publish(terminated);
            }
            else
            {
                _backoff *= 2;
                var noise = _random.Next(_backoff);
                var duration = TimeSpan.FromMilliseconds(_backoff + noise);

                Task.Delay(duration)
                    .ContinueWith(
                        t =>
                        {
                            Logger.LogWarning(
                                "Restarting {Actor} after {Duration} because of {Reason}",
                                child.ToShortString(), duration, reason
                            );
                            supervisor.RestartChildren(reason, child);
                        }, _cancelFutureRetries.Token
                    );
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

        private static PID SpawnWatcher(string address, ISpawnerContext context, RemoteActorSystemBase system)
        {
            var watcherProps = Props.FromProducer(() => new EndpointWatcher(system, address));
            var watcher = context.Spawn(watcherProps);
            return watcher;
        }

        private static PID SpawnWriter(string address, ISpawnerContext context, RemoteActorSystemBase system)
        {
            var writerProps =
                Props.FromProducer(
                        () => new EndpointWriter(system, address)
                    )
                    .WithMailbox(() => new EndpointWriterMailbox(system, system.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize));
            var writer = context.Spawn(writerProps);
            return writer;
        }
    }
    internal class EndpointWriter : IActor
    {
        private int _serializerId;
        private readonly string _address;
        private readonly ILogger _logger = Log.CreateLogger<EndpointWriter>();
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

        public async Task ReceiveAsync(IContext context)
        {
            // _logger.LogInformation(context.Message.ToString());
            switch (context.Message)
            {
                case Started _:
                    _logger.LogDebug("Starting Endpoint Writer");
                    await StartedAsync();
                    break;
                case Stopped _:
                    await StoppedAsync();
                    _logger.LogDebug($"Stopped EndpointWriter at {_address}");
                    break;
                case Restarting _:
                    await RestartingAsync();
                    break;
                case EndpointTerminatedEvent _:
                    context.Stop(context.Self);
                    break;
                case RemoteDeliver remoteDeliver:
                    {
                        _logger.LogInformation($"{remoteDeliver.Message}");
                        MessageBatch batch = BuildBatch(new RemoteDeliver[] { remoteDeliver });
                        await SendEnvelopesAsync(batch, context);
                    }
                    break;
                case IEnumerable<RemoteDeliver> m:
                    {
                        MessageBatch batch = BuildBatch(m);
                        await SendEnvelopesAsync(batch, context);
                    }
                    break;
            }
        }

        private MessageBatch BuildBatch(IEnumerable<RemoteDeliver> m)
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
            _logger.LogTrace($"EndpointWriter sending {envelopes.Count} Envelopes for {_address}");
            return batch;
        }

        private async Task SendEnvelopesAsync(MessageBatch batch, IContext context)
        {
            if (_streamWriter == null)
            {
                _logger.LogError($"gRPC Failed to send to address {_address}, reason No Connection available");
                return;
            }
            try
            {
                _logger.LogTrace($"Writing batch to {_address}");
                await _streamWriter.WriteAsync(batch);
            }
            catch (Exception x)
            {
                context.Stash();
                _logger.LogError($"gRPC Failed to send to address {_address}, reason {x.Message}");
                throw;
            }
        }

        //shutdown channel before restarting
        private Task RestartingAsync() => ShutDownChannel();

        private Task StoppedAsync() => ShutDownChannel();

        private async Task ShutDownChannel()
        {
            try
            {
                await _stream?.RequestStream?.CompleteAsync();
                await _channel.ShutdownAsync();
                _channel?.Dispose();
            }
            catch (System.Exception)
            {
            }
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
            _logger.LogInformation($"Connecting to address {address}");

            _channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpClient = httpClient });
            _client = new Remoting.RemotingClient(_channel);

            _logger.LogDebug($"Created channel and client for address {_address}");

            var res = await _client.ConnectAsync(new ConnectRequest());
            _serializerId = res.DefaultSerializerId;

            var callOptions = new CallOptions();
            _stream = _client.Receive(callOptions);
            _streamWriter = _stream.RequestStream;

            _logger.LogInformation($"Connected client for address {_address}");

            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await foreach (var server in _stream.ResponseStream.ReadAllAsync())
                    {
                        if (!server.Alive)
                        {
                            _logger.LogInformation($"Lost connection to address {_address}");
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
                    _logger.LogInformation($"Lost connection to address {_address}");
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

            _logger.LogInformation($"Connected to address {_address}");
        }
    }
    public class EndpointWatcher : IActor
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointWatcher>();

        private readonly Behavior _behavior;
        private readonly Dictionary<string, HashSet<PID>> _watched = new Dictionary<string, HashSet<PID>>();
        private readonly string _address; //for logging
        private readonly ActorSystem _system;
        private readonly Remote _remote;

        public EndpointWatcher(RemoteActorSystemBase system, string address)
        {
            _system = system;
            _address = address;
            _behavior = new Behavior(ConnectedAsync);
        }

        public Task ReceiveAsync(IContext context) => _behavior.ReceiveAsync(context);

        private Task ConnectedAsync(IContext context)
        {
            switch (context.Message)
            {
                case RemoteTerminate msg:
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
                        msg.Watcher.SendSystemMessage(_system, t);
                        break;
                    }
                case EndpointTerminatedEvent _:
                    {
                        Logger.LogDebug("Handle terminated address {Address}", _address);

                        foreach (var (id, pidSet) in _watched)
                        {
                            var watcherPid = new PID(_system.ProcessRegistry.Address, id);
                            var watcherRef = _system.ProcessRegistry.Get(watcherPid);

                            if (watcherRef == _system.DeadLetter) continue;

                            foreach (var t in pidSet.Select(
                                pid => new Terminated
                                {
                                    Who = pid,
                                    AddressTerminated = true
                                }
                            ))
                            {
                                //send the address Terminated event to the Watcher
                                watcherPid.SendSystemMessage(_system, t);
                            }
                        }

                        _watched.Clear();
                        _behavior.Become(TerminatedAsync);
                        context.Stop(context.Self);
                        break;
                    }
                case RemoteUnwatch msg:
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
                        _remote.SendMessage(msg.Watchee, w, -1);
                        break;
                    }
                case RemoteWatch msg:
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
                        _remote.SendMessage(msg.Watchee, w, -1);
                        break;
                    }
                case Stopped _:
                    {
                        Logger.LogDebug("Stopped EndpointWatcher at {Address}", _address);
                        break;
                    }
            }

            return Actor.Done;
        }

        private Task TerminatedAsync(IContext context)
        {
            switch (context.Message)
            {
                case RemoteWatch msg:
                    {
                        msg.Watcher.SendSystemMessage(
                            _system,
                            new Terminated
                            {
                                AddressTerminated = true,
                                Who = msg.Watchee
                            }
                        );
                        break;
                    }
                case EndpointConnectedEvent _:
                    {
                        Logger.LogDebug("Handle restart address {Address}", _address);
                        _behavior.Become(ConnectedAsync);
                        break;
                    }
                case RemoteUnwatch _:
                case EndpointTerminatedEvent _:
                case RemoteTerminate _:
                    {
                        //pass 
                        break;
                    }
            }

            return Actor.Done;
        }
    }
}
