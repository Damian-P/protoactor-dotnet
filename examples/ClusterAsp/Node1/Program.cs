// -----------------------------------------------------------------------
//   <copyright file="Program.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;

namespace Node1
{
    static class Program
    {
        static async Task Main(string[] args)
        {
            var hostBuilder = new HostBuilder()
                .ConfigureServices(c =>
                    {
                        c.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information));
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
                            Log.SetLoggerFactory(loggerFactory);
                            var cluster = new Cluster(actorSystem, serialization);
                            return cluster;
                        });
                        c.AddHostedService<ClusterHostedService>();
                    });
            await hostBuilder.RunConsoleAsync();
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
            cluster.System.EventStream.Subscribe(msg => Console.WriteLine($"EventStream: {msg}"));
            await cluster.Start("MyCluster", Environment.MachineName, 12000, new ConsulProvider(new ConsulProviderOptions(), c => c.Address = new Uri("http://consul:8500/")));
            // var i = 2;
            // while (i-- > 0)
            // {
            //     var actorName = Guid.NewGuid().ToString();
            //     var (pid, sc) = await cluster.GetAsync(actorName, "ParentActor", hostApplicationLifetime.ApplicationStopping);

            //     while (sc != ResponseStatusCode.OK)
            //     {
            //         (pid, sc) = await cluster.GetAsync(actorName, "ParentActor", hostApplicationLifetime.ApplicationStopping);
            //         await Task.Delay(100);
            //     }
            //     cluster.System.Root.Send(pid, new HelloRequest());
            // }
        }

        private void OnApplicationStopping()
        {
            // cluster.Shutdown().GetAwaiter().GetResult();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            await cluster.Shutdown();
        }
    }
}