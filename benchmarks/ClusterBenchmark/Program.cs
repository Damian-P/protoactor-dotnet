﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClusterExperiment1.Messages;
using k8s;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Proto;
using Proto.Cluster;
using Proto.Cluster.Consul;
using Proto.Cluster.Kubernetes;
using Proto.Cluster.MongoIdentityLookup;
using Proto.Remote;

namespace ClusterExperiment1
{
    public static class Program
    {
        private static async Task RunFollower()
        {
            var logger = SetupLogger();

            var cluster = SpawnMember();

            Thread.Sleep(Timeout.Infinite);

        }

        private static async Task RunLeader()
        {
            var logger = SetupLogger();

            var cluster = await SpawnClient();

            await Task.Delay(1000);

            _ = Task.Run(async () =>
                {
                    var rnd = new Random();
                    while (true)
                    {
                        try
                        {
                            var id = "myactor" + rnd.Next(0, 1000);
                            var res = await cluster.RequestAsync<HelloResponse>(id, "hello", new HelloRequest(),
                                new CancellationTokenSource(TimeSpan.FromSeconds(15)).Token
                            );

                            if (res == null)
                            {
                                logger.LogError("Null response");
                            }
                            else
                            {
                                Console.Write(".");
                            }
                        }
                        catch (Exception)
                        {
                            logger.LogError("Request timeout");
                        }
                    }
                }
            );


            Thread.Sleep(Timeout.Infinite);
        }

        private static ILogger SetupLogger()
        {
            Log.SetLoggerFactory(LoggerFactory.Create(l => l.AddConsole(o =>
                        {
                            o.IncludeScopes = false;
                            o.UseUtcTimestamp = false;
                            o.TimestampFormat = "hh:mm:ss:fff - ";
                        }
                    ).SetMinimumLevel(LogLevel.Information)
                )
            );
            var logger = Log.CreateLogger(nameof(Program));
            return logger;
        }


        public static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                await RunLeader();
            }
            else
            {
                await RunFollower();
            }
        }

        private static async Task<Cluster> SpawnClient()
        {
            var system = new ActorSystem();
            var clusterProvider = ClusterProvider();
            var port = Environment.GetEnvironmentVariable("PROTOPORT") ?? "0";
            var p = int.Parse(port);
            var host = Environment.GetEnvironmentVariable("PROTOHOST") ?? "127.0.0.1";
            var advertiseHostname = Environment.GetEnvironmentVariable("PROTOHOSTPUBLIC");
            var remote = new SelfHostedRemote(system, host, p, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(MessagesReflection.Descriptor);
                remote.RemoteConfig.AdvertisedHostname = advertiseHostname!;
            });
            var identity = GetIdentityLookup();
            var cluster = new Cluster(system, remote);

            var config = GetClusterConfig(clusterProvider, identity);
            await cluster.StartClientAsync(config);
            return cluster;
        }

        private static Cluster SpawnMember()
        {
            var system = new ActorSystem();
            var clusterProvider = ClusterProvider();
            var port = Environment.GetEnvironmentVariable("PROTOPORT") ?? "0";
            var p = int.Parse(port);
            var host = Environment.GetEnvironmentVariable("PROTOHOST") ?? "127.0.0.1";
            var advertiseHostname = Environment.GetEnvironmentVariable("PROTOHOSTPUBLIC");
            var remote = new SelfHostedRemote(system, host, p, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(MessagesReflection.Descriptor);
                remote.RemoteKindRegistry.RegisterKnownKind("hello", Props.FromProducer(() => new HelloActor()));
                remote.RemoteConfig.AdvertisedHostname = advertiseHostname!;
            });
            var identity = GetIdentityLookup();
            var cluster = new Cluster(system, remote);

            var config = GetClusterConfig(clusterProvider, identity);
            _ = cluster.StartMemberAsync(config);
            return cluster;
        }

        private static ClusterConfig GetClusterConfig(IClusterProvider clusterProvider, MongoIdentityLookup identity)
        {
            return new ClusterConfig("mycluster", clusterProvider).WithIdentityLookup(identity);
        }

        private static IClusterProvider ClusterProvider()
        {
            try
            {
                Console.WriteLine("Running with InClusterConfig");
                // var namespaceFile = Path.Combine(
                //     $"{Path.DirectorySeparatorChar}var",
                //     "run",
                //     "secrets",
                //     "kubernetes.io",
                //     "serviceaccount",
                //     "namespace"
                // );
                // Console.WriteLine(namespaceFile);
                //
                // KubernetesClientConfiguration.InClusterConfig();
                //
                // var cachedNamespace = File.ReadAllText(namespaceFile);

                var kubernetesConfig =
                    KubernetesClientConfiguration
                        .InClusterConfig(); //   KubernetesClientConfiguration.BuildConfigFromConfigFile(cachedNamespace);
                var kubernetes = new Kubernetes(kubernetesConfig);
                return new KubernetesProvider(kubernetes);
            }
            catch
            {
                return new ConsulProvider(new ConsulProviderOptions());
            }
        }

        private static MongoIdentityLookup GetIdentityLookup()
        {
            var db = GetMongo();
            var identity = new MongoIdentityLookup("mycluster", db);
            return identity;
        }

        static IMongoDatabase GetMongo()
        {
            var connectionString = Environment.GetEnvironmentVariable("MONGO") ?? "mongodb://127.0.0.1:27017/ProtoMongo";
            var url = MongoUrl.Create(connectionString);
            var settings = MongoClientSettings.FromUrl(url);
            var client = new MongoClient(settings);
            var database = client.GetDatabase("ProtoMongo");
            return database;
        }
    }



    public class HelloActor : IActor
    {
        //   private readonly ILogger _log = Log.CreateLogger<HelloActor>();

        public Task ReceiveAsync(IContext ctx)
        {
            if (ctx.Message is Started)
            {
                //just to highlight when this happens
                Console.Write("#");
                //_log.LogInformation("I started " + ctx.Self);
            }

            if (ctx.Message is HelloRequest)
            {
                ctx.Respond(new HelloResponse());
            }

            if (ctx.Message is Stopped)
            {
                //just to highlight when this happens
                //    _log.LogWarning("I stopped" + ctx.Self);
            }

            return Actor.Done;
        }
    }
}