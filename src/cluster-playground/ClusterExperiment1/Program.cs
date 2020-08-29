using System;
using System.Threading;
using System.Threading.Tasks;
using ClusterExperiment1.Messages;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Remote;

namespace ClusterExperiment1
{
    public static class Program
    {
        private static async Task RunFollower()
        {
            Log.SetLoggerFactory(LoggerFactory.Create(l => l.AddConsole().SetMinimumLevel(LogLevel.Information)));
            var workers = new System.Collections.Concurrent.ConcurrentStack<Cluster>();
            var logger = Log.CreateLogger(nameof(Program));

            var system = new ActorSystem();
            var remote = system.AddRemote("localhost", 8090, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(MessagesReflection.Descriptor);
            });
            var consulProvider = new ConsulProvider(new ConsulProviderOptions());
            var requestNode = system.AddClustering(new ClusterConfig("mycluster", "127.0.0.1", 8090, consulProvider).WithPidCache(false));

            await requestNode.StartAsync();

            workers.Push(await SpawnMember(0));

            await Task.Delay(1000);

            _ = Task.Run(async () =>
                {
                    var rnd = new Random();
                    while (true)
                    {
                        try
                        {
                            var id = "myactor" + rnd.Next(0, 10);
                            //    Console.WriteLine($"Sending request {id}");
                            var res = await requestNode.RequestAsync<HelloResponse>(id, "hello", new HelloRequest(),
                                CancellationToken.None
                            );

                            if (res == null)
                            {
                                logger.LogError("Got void response");
                                await Task.Delay(1000);
                            }
                            else
                            {
                                Console.Write(".");
                                //      Console.WriteLine("Got response");
                            }

                            //await Task.Delay(0);
                        }
                        catch (Exception e)
                        {
                            logger.LogError(e, "An error occured");
                        }
                    }
                }
            );
            var run = true;
            while (run)
            {
                switch (Console.ReadKey().KeyChar)
                {
                    case '-':
                        {
                            if (workers.TryPop(out Cluster removedNode))
                                await removedNode.ShutdownAsync();
                        }
                        break;
                    case '*':
                        {
                            if (workers.TryPop(out Cluster removedNode))
                                await removedNode.ShutdownAsync(false);
                        }
                        break;
                    case '+':
                        workers.Push(await SpawnMember(0));
                        break;
                    case 'e':
                        run = false;
                        break;
                    default:
                        break;
                }
            }
            await requestNode.ShutdownAsync();
            foreach (var node in workers)
            {
                await node.ShutdownAsync();
            }
        }


        private static async Task<Cluster> SpawnMember(int port)
        {
            var helloProps = Props.FromProducer(() => new HelloActor());

            var system = new ActorSystem();
            var remote = system.AddRemote("localhost", port, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(MessagesReflection.Descriptor);
                remote.RemoteKindRegistry.RegisterKnownKind("hello", helloProps);
            });
            var consulProvider = new ConsulProvider(new ConsulProviderOptions());
            var clusterNode = system.AddClustering(new ClusterConfig("mycluster", "localhost", port, consulProvider).WithPidCache(false));

            await clusterNode.StartAsync();
            return clusterNode;
        }
    }

    public class HelloActor : IActor
    {
     //   private readonly ILogger _log = Log.CreateLogger<HelloActor>();

        public Task ReceiveAsync(IContext ctx)
        {
            if (ctx.Message is Started)
            {
                Console.Write("#");
                //just to highlight when this happens
             //   _log.LogError("I started " + ctx.Self);
            }

            if (ctx.Message is HelloRequest)
            {
                ctx.Respond(new HelloResponse());
            }

            if (ctx.Message is Stopped)
            {
                //just to highlight when this happens
            //    _log.LogError("IM STOPPING!! " + ctx.Self);
            }

            return Actor.Done;
        }
    }
}