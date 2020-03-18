// -----------------------------------------------------------------------
//   <copyright file="Program.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Messages;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;
using Microsoft.Extensions.DependencyInjection;
using System.Threading;

namespace Node2
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = new HostBuilder()
                .ConfigureServices(c =>
                    {
                        c.AddTransient<IParentActor, ParentActor>();
                        c.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Warning));
                        c.AddProtoActor();
                        c.AddSingleton(sp =>
                        {
                            var serialization = new Serialization();
                            serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
                            return serialization;
                        });
                        c.AddSingleton(sp =>
                        {
                            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
                            var serialization = sp.GetRequiredService<Serialization>();
                            var actorSystem = sp.GetRequiredService<ActorSystem>();
                            var actorFactory = sp.GetRequiredService<IActorFactory>();
                            Log.SetLoggerFactory(loggerFactory);
                            var cluster = new Cluster(actorSystem, serialization);
                            cluster.Remote.RegisterKnownKind("ParentActor",
                                Props.FromProducer(() => (IActor)ActivatorUtilities.GetServiceOrCreateInstance<IParentActor>(sp))
                            );
                            return cluster;
                        });
                        c.AddHostedService<ClusterHostedService>();
                    });
            await hostBuilder.RunConsoleAsync();
        }
        public interface IParentActor : IActor
        {

        }
        public class ParentActor : IParentActor
        {
            private readonly ActorSystem system;
            private readonly ILogger<ParentActor> logger;

            public ParentActor(ActorSystem system, ILogger<ParentActor> logger)
            {
                this.system = system;
                this.logger = logger;
            }
            public Task ReceiveAsync(IContext context)
            {
                switch (context.Message)
                {
                    case HelloRequest helloRequest:
                        logger.LogInformation($".");
                        // context.Respond(new HelloResponse { Message = "Hello from node 2" });
                        break;
                }

                return Actor.Done;
            }
        }

        public class ClusterHostedService : IHostedService
        {
            private readonly Cluster cluster;
            private readonly IHostApplicationLifetime hostApplicationLifetime;

            public ClusterHostedService(Cluster cluster, IHostApplicationLifetime hostApplicationLifetime)
            {
                this.cluster = cluster;
                this.hostApplicationLifetime = hostApplicationLifetime;
            }

            public Task StartAsync(CancellationToken cancellationToken)
            {
                hostApplicationLifetime.ApplicationStarted.Register(() => Task.Run(OnApplicationStarted));
                hostApplicationLifetime.ApplicationStopping.Register(OnApplicationStopping);
                return Task.CompletedTask;
            }

            private async Task OnApplicationStarted()
            {
                await Task.Delay(3000);
                await cluster.Start("MyCluster", Environment.MachineName, 12000, new ConsulProvider(new ConsulProviderOptions(), c => c.Address = new Uri("http://consul:8500/")));
            }

            private void OnApplicationStopping()
            {

            }

            public async Task StopAsync(CancellationToken cancellationToken)
            {
                await cluster.Shutdown();
            }
        }
    }
}