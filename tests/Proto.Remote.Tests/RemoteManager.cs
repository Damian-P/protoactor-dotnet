using System;

namespace Proto.Remote.Tests
{
    public class RemoteManager
    {
        public static string RemoteAddress = "127.0.0.1:12000";

        static RemoteManager()
        {
            var service = new ProtoService(12000, "127.0.0.1");
            service.StartAsync().GetAwaiter().GetResult();
        }

        public static (IRemote, ActorSystem) EnsureRemote()
        {
            var system = new ActorSystem();
            var remote = new SelfHostedRemote(system, "127.0.0.1", 0, remoteConfiguration =>
            {
                remoteConfiguration.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
                remoteConfiguration.RemoteConfig.EndpointWriterOptions = new EndpointWriterOptions
                {
                    MaxRetries = 2,
                    RetryBackOffms = 10,
                    RetryTimeSpan = TimeSpan.FromSeconds(120)
                };
            });

            remote.Start();

            return (remote, system);
        }
    }
}