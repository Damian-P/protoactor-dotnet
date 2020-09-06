using System;

namespace Proto.Remote.Tests
{
    public class RemoteManager
    {
        public static string RemoteAddress = "127.0.0.1:12000";

        static RemoteManager()
        {
            system = new ActorSystem();
            remote = new SelfHostedRemote(system, "127.0.0.1", 12001, remoteConfiguration =>
            {
                remoteConfiguration.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
                remoteConfiguration.RemoteConfig.EndpointWriterOptions = new EndpointWriterOptions
                {
                    MaxRetries = 2,
                    RetryBackOffms = 10,
                    RetryTimeSpan = TimeSpan.FromSeconds(120)
                };
            });
        }

        private static readonly IRemote remote;
        private static readonly ActorSystem system;

        private static bool remoteStarted;

        public static (IRemote, ActorSystem) EnsureRemote()
        {
            if (remoteStarted) return (remote, system);

            var service = new ProtoService(12000, "127.0.0.1");
            service.StartAsync().GetAwaiter().GetResult();

            remote.Start();

            remoteStarted = true;

            return (remote, system);
        }
    }
}