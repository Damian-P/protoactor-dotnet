using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Divergic.Logging.Xunit;
using Microsoft.Extensions.Logging;
using Proto.Cluster.Testing;
using Proto.Remote;
using Proto.Remote.Tests.Messages;
using Xunit;
using Xunit.Abstractions;

namespace Proto.Cluster.Tests
{
    [Trait("Category", "Remote")]
    public class ClusterTests
    {
        private ILogger _logger;

        public ClusterTests(ITestOutputHelper testOutputHelper)
        {
            var factory = LogFactory.Create(testOutputHelper);
            Log.SetLoggerFactory(factory);
            _logger = Log.CreateLogger<ClusterTests>();
        }

        [Fact]
        public void InMemAgentRegisterService()
        {
            var agent = new InMemAgent();
            agent.RegisterService(new AgentServiceRegistration
            {
                ID = "abc",
                Address = "LocalHost",
                Kinds = new[] { "SomeKind" },
                Port = 8080
            });

            var services = agent.GetServicesHealth();

            Assert.True(services.Length == 1, "There should be only one service");

        }

        [Fact]
        public void InMemAgentServiceShouldBeAlive()
        {
            var agent = new InMemAgent();
            agent.RegisterService(new AgentServiceRegistration
            {
                ID = "abc",
                Address = "LocalHost",
                Kinds = new[] { "SomeKind" },
                Port = 8080
            });

            var services = agent.GetServicesHealth();
            var first = services.First();
            Assert.True(first.Alive);
        }

        [Fact]
        public async Task InMemAgentServiceShouldNotBeAlive()
        {
            var agent = new InMemAgent();
            agent.RegisterService(new AgentServiceRegistration
            {
                ID = "abc",
                Address = "LocalHost",
                Kinds = new[] { "SomeKind" },
                Port = 8080
            });

            var services = agent.GetServicesHealth();
            var first = services.First();

            await Task.Delay(TimeSpan.FromSeconds(5));

            Assert.False(first.Alive);
        }

        [Fact]
        public async Task InMemAgentServiceShouldBeAliveAfterTTLRefresh()
        {
            var agent = new InMemAgent();
            agent.RegisterService(new AgentServiceRegistration
            {
                ID = "abc",
                Address = "LocalHost",
                Kinds = new[] { "SomeKind" },
                Port = 8080
            });


            await Task.Delay(TimeSpan.FromSeconds(5));
            agent.RefreshServiceTTL("abc");

            var services = agent.GetServicesHealth();
            var first = services.First();

            Assert.True(first.Alive);
        }


        [Fact]
        public async Task ClusterShouldContainOneAliveNode()
        {
            var agent = new InMemAgent();

            var cluster = await NewCluster(agent, 8080);

            var services = agent.GetServicesHealth();
            var first = services.First();
            Assert.True(first.Alive);
            Assert.True(services.Length == 1);

            await cluster.Shutdown();
        }

        [Fact]
        public async Task ClusterShouldRefreshServiceTTL()
        {
            var agent = new InMemAgent();

            var cluster = await NewCluster(agent, 8080);

            var services = agent.GetServicesHealth();
            var first = services.First();
            var ttl1 = first.TTL;
            SpinWait.SpinUntil(() => ttl1 != first.TTL, TimeSpan.FromSeconds(10));
            Assert.NotEqual(ttl1, first.TTL);
            await cluster.Shutdown();
        }

        [Fact]
        public async Task ClusterShouldContainTwoAliveNodes()
        {
            var agent = new InMemAgent();

            var cluster1 = await NewCluster(agent, 8080);
            var cluster2 = await NewCluster(agent, 8081);

            var services = agent.GetServicesHealth();

            Assert.True(services.Length == 2);
            Assert.True(services.All(m => m.Alive));

            await cluster1.Shutdown();
            await cluster2.Shutdown();
        }

        [Fact]
        public async Task ClusterShouldContainOneAliveAfterShutdownOfNode1()
        {
            var agent = new InMemAgent();

            var cluster1 = await NewCluster(agent, 8080);
            var cluster2 = await NewCluster(agent, 8081);

            await cluster1.Shutdown();

            var services = agent.GetServicesHealth();

            Assert.True(services.Length == 1, "Expected 1 Node");
            Assert.True(services.All(m => m.Alive));


            await cluster2.Shutdown();
        }

        [Fact]
        public async Task ClusterShouldSpawnActors()
        {
            var agent = new InMemAgent();

            var prop = Props.FromFunc(context =>
                {
                    if (context.Message is string _)
                    {
                        context.Respond("Hello");
                    }

                    return Actor.Done;
                }
            );

            var cluster1 = await NewCluster(agent, 8080, ("echo", prop));
            var cluster2 = await NewCluster(agent, 8081, ("echo", prop));



            var (pid, response) = await cluster1.GetAsync("myactor", "echo");

            _logger.LogDebug("Response = {0}", response);
            _logger.LogDebug("PID = {0}", pid);


            Assert.Equal(ResponseStatusCode.OK, response);
            Assert.NotNull(pid);



            await cluster1.Shutdown(true);
            await cluster2.Shutdown(true);
        }

        [Fact]
        public async Task ClusterShouldSendAndReceiveMessage()
        {
            var agent = new InMemAgent();

            int msgCount = 0;

            var prop = Props.FromFunc(context =>
                {
                    switch (context.Message)
                    {
                        case Ping ping:
                            msgCount++;
                            _logger.LogDebug("Received {0}", ping);
                            context.Respond(new Pong { Message = "Pong" });
                            break;
                    }

                    return Actor.Done;
                }
            );

            List<Cluster> clusterMembers = new List<Cluster>();

            var memberCount = 5;

            for (int i = 0; i < memberCount; i++)
            {
                clusterMembers.Add(await NewCluster(agent, 8085 + i, ("echo", prop)));
            }
            foreach (var member in clusterMembers)
            {

                var (pid, response) = await member.GetAsync("myactor", "echo");

                _logger.LogDebug("Response = {0}", response);
                _logger.LogDebug("PID = {0}", pid);


                Assert.Equal(ResponseStatusCode.OK, response);
                Assert.NotNull(pid);

                var pong = await member.System.Root.RequestAsync<Pong>(pid, new Ping { Message = "Ping" });
                Assert.NotNull(pong);
            }

            Assert.Equal(memberCount, msgCount);

            foreach (var member in clusterMembers)
            {
                await member.Shutdown(true);
            }

        }

        private static async Task<Cluster> NewCluster(InMemAgent agent, int port,
            params (string kind, Props prop)[] kinds)
        {
            var provider = new TestProvider(new TestProviderOptions(), agent);
            var system = new ActorSystem();
            var remote = system.AddRemote("localhost", port, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(Proto.Remote.Tests.Messages.ProtosReflection.Descriptor);
                foreach (var (kind, prop) in kinds)
                {
                    remote.RemoteKindRegistry.RegisterKnownKind(kind, prop);
                }
            });

            var clusterConfig = new ClusterConfig("cluster1", provider)
               .WithPidCache(false);

            var cluster = system.AddClustering(clusterConfig);

            await system.StartCluster();
            return cluster;
        }
    }
}