using System;
using System.Threading.Tasks;
using Messages;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Remote;
using Log = Proto.Log;
using ProtosReflection = Messages.ProtosReflection;

namespace TestApp
{
    public class HelloGrain : IHelloGrain
    {
        public Task<HelloResponse> SayHello(HelloRequest request) => Task.FromResult(new HelloResponse { Message = "" });
    }

    public static class Worker
    {
        public static async Task Start(string port, string seqPort)
        {
            const string clusterName = "test";

            var log = LoggerFactory.Create(x => x.AddSeq($"http://localhost:{seqPort}").SetMinimumLevel(LogLevel.Debug));
            Log.SetLoggerFactory(log);

            Console.WriteLine("Starting worker");

            var system = new ActorSystem();
            system.AddRemoteOverGrpc("127.0.0.1", int.Parse(port), remote =>
            {
                remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            });
            system.AddClustering(clusterName, new ConsulProvider(new ConsulProviderOptions { DeregisterCritical = TimeSpan.FromSeconds(2) }), cluster =>
            {
                var grains = cluster.AddGrains();
                grains.HelloGrainFactory(() => new HelloGrain());
            });

            await system.StartCluster();

            Console.WriteLine("Started worked on " + system.ProcessRegistry.Address);

            Console.ReadLine();

            await system.StopCluster();
        }
    }
}