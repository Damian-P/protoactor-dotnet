﻿namespace Proto.Cluster.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Partition;
    using Remote;
    using Testing;
    using Xunit;
    using ProtosReflection = Remote.Tests.Messages.ProtosReflection;

    public abstract class InMemDefaultClusterTests: IAsyncLifetime
    {
        private readonly List<Cluster> _clusters = new List<Cluster>();
        
        protected async Task<IList<Cluster>> SpawnClusterNodes(int count, Action<ClusterConfig> configure = null)
        {
            var agent = new InMemAgent();
            var clusterTasks = Enumerable.Range(0, count).Select(_ => SpawnCluster(agent, configure))
                .ToList();
            await Task.WhenAll(clusterTasks);
            return clusterTasks.Select(task => task.Result).ToList();
        }

        private async Task<Cluster> SpawnCluster(InMemAgent agent, Action<ClusterConfig> configure)
        {
            var remoteConfig = GrpcRemoteConfig.BindToLocalhost()
                                .WithProtoMessages(ProtosReflection.Descriptor)
                                .WithProtoMessages(ClusterTest.Messages.MessagesReflection.Descriptor);
            var clusterConfig = ClusterConfig.Setup(
                "testCluster",
                new TestProvider(new TestProviderOptions(), agent),
                new PartitionIdentityLookup(),
                remoteConfig
            ).WithClusterKind(EchoActor.Kind, EchoActor.Props);


            configure?.Invoke(clusterConfig);
            var remote = new SelfHostedRemote(new ActorSystem(), remoteConfig);
            var cluster = new Cluster(remote, clusterConfig);

            await cluster.StartMemberAsync();
            _clusters.Add(cluster);
            return cluster;
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.WhenAll(_clusters.Select(cluster => cluster.ShutdownAsync()));
        }
    }
}