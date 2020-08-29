using System;
using System.Threading;
using System.Threading.Tasks;
using ClusterExperiment1.Messages;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Cluster.Testing;
using Proto.Remote;

namespace ClusterExperiment1
{
    public class Program
    {
        public static async Task Main()
        {
            Log.SetLoggerFactory(LoggerFactory.Create(l => l
                        .AddConsole()
                        .AddFilter("Proto.Remote", LogLevel.Information)
                        .AddFilter("Microsoft", LogLevel.Critical)
                        .AddFilter("Grpc", LogLevel.Critical)
                        .AddFilter("Proto.Cluster.Partition.PartitionIdentityLookup", LogLevel.Information)
                        .SetMinimumLevel(LogLevel.Debug)
                        ));
            var logger = Log.CreateLogger(nameof(Program));

            var workers = new System.Collections.Concurrent.ConcurrentQueue<Cluster>();
            var requesters = new System.Collections.Concurrent.ConcurrentQueue<Cluster>();
            var port = 8090;
            requesters.Enqueue(SpawnMember(port++, false));
            for (int i = 0; i < 1; i++)
            {
                workers.Enqueue(SpawnMember(port++));
            }


            _ = Task.Run(async () =>
            {
                var rnd = new Random();
                int i = 1;
                while (true)
                {
                    try
                    {
                        if (i > 1000) i = 1;
                        var id = "myactor" + i++;
                        if (requesters.TryPeek(out var node))
                        {
                            var res = await node.RequestAsync<HelloResponse>(id, "hello", new HelloRequest(),
                                                        // new CancellationTokenSource(2000).Token
                                                        CancellationToken.None
                                                    );

                            if (res == null)
                            {
                                Console.Write("_");
                            }
                            else
                            {
                                Console.Write(".");
                            }
                        }
                    }
                    catch (System.Exception e)
                    {
                        logger.LogError(e, "");
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
                            if (workers.TryDequeue(out Cluster removedNode))
                            {
                                await removedNode.ShutdownAsync();
                            }
                        }
                        break;
                    case '*':
                        {
                            if (workers.TryDequeue(out Cluster removedNode))
                            {
                                await removedNode.ShutdownAsync(false);
                            }
                        }
                        break;
                    case '+':
                        Console.WriteLine(
                       "-----------------------------------------------------------------------------------------"
                   );
                        workers.Enqueue(SpawnMember(port++));
                        break;
                    case '/':
                        {
                            if (requesters.TryDequeue(out var node))
                            {
                                await node.ShutdownAsync();
                            }
                            requesters.Enqueue(SpawnMember(port++, false));
                        }
                        break;
                    case 'e':
                        run = false;
                        break;
                    default:
                        break;
                }
            }
            while (workers.TryDequeue(out var node))
                await node.ShutdownAsync();
            while (requesters.TryDequeue(out var node))
                await node.ShutdownAsync();
        }
        private static InMemAgent agent = new InMemAgent();
        private static Cluster SpawnMember(int port, bool isWorker = true)
        {
            var system2 = new ActorSystem();
            var consul2 = new TestProvider(new TestProviderOptions(), agent);
            var helloProps = Props.FromProducer(() => new HelloActor());
            var remote = system2.AddRemote("localhost", port, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(MessagesReflection.Descriptor);
                if (isWorker)
                    remote.RemoteKindRegistry.RegisterKnownKind("hello", helloProps);
            });
            var cluster2 = system2.AddClustering(new ClusterConfig("mycluster", "127.0.0.1", port, consul2).WithPidCache(false));
            _ = cluster2.StartAsync().ConfigureAwait(false);
            return cluster2;
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
            }

            if (ctx.Message is HelloRequest)
            {
                ctx.Respond(new HelloResponse());
            }

            if (ctx.Message is Stopped)
            {
                Console.Write("T");
            }

            return Actor.Done;
        }
    }
}