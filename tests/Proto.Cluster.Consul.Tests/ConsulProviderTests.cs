﻿using System.Threading.Tasks;
using Proto.Cluster.Tests;
using Xunit;
using Xunit.Abstractions;

namespace Proto.Cluster.Consul.Tests
{
    public class ConsulProviderTests: ClusterTestTemplate
    {
        private const string SkipReason = "Consul needs to run locally";
        
        public ConsulProviderTests(ITestOutputHelper testOutputHelper) : base(testOutputHelper) { }

        [Theory]
        [InlineData(1, 100, 1000)]
        public override Task OrderedDeliveryFromActors(int clusterNodes, int sendingActors, int messagesSentPerCall)
        {
            return base.OrderedDeliveryFromActors(clusterNodes, sendingActors, messagesSentPerCall);
        }

        protected override IClusterProvider GetClusterProvider()
        {
            return new ConsulProvider(new ConsulProviderConfig());
        }
    }
}