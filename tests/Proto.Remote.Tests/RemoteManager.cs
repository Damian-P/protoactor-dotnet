using System;
using System.Net;
using System.Net.Sockets;

namespace Proto.Remote.Tests
{
    public class RemoteManager
    {
        public const string RemoteAddress = "localhost:12000";
        static RemoteManager()
        {
            system = new ActorSystem();
            remote = new SelfHostedRemote(system, "localhost", 12001, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
            });
        }

        private static readonly Remote remote;
        private static readonly ActorSystem system;

        private static bool remoteStarted;

        public static (Remote, ActorSystem) EnsureRemote()
        {
            if (remoteStarted) return (remote, system);

            var config = new RemoteConfig
            {
                EndpointWriterOptions = new EndpointWriterOptions
                {
                    MaxRetries = 2,
                    RetryBackOffms = 10,
                    RetryTimeSpan = TimeSpan.FromSeconds(120)
                }
            };

            var service = new ProtoService(12000, "localhost");
            service.StartAsync().Wait();

            remote.Start();

            remoteStarted = true;

            return (remote, system);
        }
    }
}