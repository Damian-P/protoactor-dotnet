// -----------------------------------------------------------------------
//   <copyright file="Program.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;

namespace Node2
{
    public class HelloGrain : IHelloGrain
    {
        public Task<HelloResponse> SayHello(HelloRequest request)
        {
            return Task.FromResult(new HelloResponse
            {
                Message = "Hello from typed grain"
            });
        }
    }
    class Program
    {
        static async Task Main(string[] args)
        {
            var system = new ActorSystem();
            system.AddRemotingOverGrpc("node2", 12000, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            });
            var cluster = new Cluster(system, "MyCluster", new ConsulProvider(new ConsulProviderOptions(), c => c.Address = new Uri("http://consul:8500/")));
            var grains = new Grains(cluster);


            grains.HelloGrainFactory(() => new HelloGrain());

            await cluster.Start();

            Console.CancelKeyPress += async (e, y) =>
            {
                Console.WriteLine("Shutting Down...");
                await cluster.Shutdown();
            };
            await Task.Delay(-1);
        }
    }
}