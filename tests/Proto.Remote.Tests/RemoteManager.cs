using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Proto.Remote.Tests.Messages;

namespace Proto.Remote.Tests
{
    public class EchoActor : IActor
    {
        private static readonly ILogger Logger = Log.CreateLogger<EchoActor>();

        private readonly string _host;
        private readonly int _port;

        public EchoActor(string host, int port)
        {
            _host = host;
            _port = port;
        }

        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Started _:
                    Logger.LogDebug($"{context.Self}");
                    break;
                case Ping ping:
                    Logger.LogDebug("Received Ping, replying Pong");
                    context.Respond(new Pong { Message = $"{_host}:{_port} {ping.Message}" });
                    break;
                case Die _:
                    Logger.LogDebug("Received termination request, stopping");
                    context.Stop(context.Self);
                    break;
                default:
                    Logger.LogDebug(context.Message.GetType().Name);
                    break;
            }

            return Actor.Done;
        }
    }
    public class RemoteManager
    {
        public const string RemoteAddress = "localhost:12000";
        static RemoteManager()
        {
            // Log.SetLoggerFactory(LoggerFactory.Create(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug)));
            var props = Props.FromProducer(() => new EchoActor("localhost", 12000));
            system = new ActorSystem();
            var distantSystem = new ActorSystem();
            distantRemote = new SelfHostedRemoteServerOverAspNet(distantSystem, "localhost", 12000, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
                remote.RemoteConfig.EndpointWriterOptions = new EndpointWriterOptions
                {
                    MaxRetries = 2,
                    RetryBackOffms = 10,
                    RetryTimeSpan = TimeSpan.FromSeconds(120)
                };
                
                remote.RemoteKindRegistry.RegisterKnownKind("EchoActor", props);
            });
            distantRemote.Start().GetAwaiter().GetResult();
            distantSystem.Root.SpawnNamed(props, "EchoActorInstance");
            localRemote = new SelfHostedRemoteServerOverAspNet(system, "localhost", 12001, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
                remote.RemoteConfig.EndpointWriterOptions = new EndpointWriterOptions
                {
                    MaxRetries = 2,
                    RetryBackOffms = 10,
                    RetryTimeSpan = TimeSpan.FromSeconds(120)
                };
            });
        }

        private static IRemote localRemote;
        private static IRemote distantRemote;
        private static ActorSystem system;

        private static bool remoteStarted;

        public static (IRemote, ActorSystem) EnsureRemote()
        {
            if (remoteStarted) return (localRemote, system);

            var config =
            localRemote.Start();
            remoteStarted = true;

            return (localRemote, system);


        }
        static string GetLocalIp()
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);

            socket.Connect("8.8.8.8", 65530);
            var endPoint = socket.LocalEndPoint as IPEndPoint;
            return endPoint?.Address.ToString();
        }
    }
}