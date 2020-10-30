﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClusterTest.Messages;
using Divergic.Logging.Xunit;
using FluentAssertions;
using Proto.Cluster.IdentityLookup;
using Proto.Cluster.Partition;
using Proto.Cluster.Testing;
using Proto.Remote;
using Xunit;
using Xunit.Abstractions;

namespace Proto.Cluster.Tests
{
    [Collection("ClusterTests")]
    public abstract class ClusterTestTemplate: IAsyncLifetime
    {
        protected readonly ITestOutputHelper TestOutputHelper;
        private readonly Lazy<InMemAgent> _inMemAgent = new Lazy<InMemAgent>(() => new InMemAgent());
        private readonly List<Cluster> _clusters = new List<Cluster>();
        private InMemAgent InMemAgent => _inMemAgent.Value;

        protected ClusterTestTemplate(ITestOutputHelper testOutputHelper)
        {
            var factory = LogFactory.Create(testOutputHelper);
            TestOutputHelper = testOutputHelper;
            Log.SetLoggerFactory(factory);
        }

        [Theory]
        [InlineData(1, 100, 10)]
        [InlineData(3, 100, 10)]
        public virtual async Task OrderedDeliveryFromActors(int clusterNodes, int sendingActors, int messagesSentPerCall)
        {
            const string aggregatorId = "agg-1";
            var clusterMembers = await SpawnMembers(clusterNodes);

            await Task.Delay(3000);
            
            var maxWait = new CancellationTokenSource(5000).Token;
            
            var sendToRequest = new SendToRequest
            {
                Count = messagesSentPerCall,
                Id = aggregatorId
            };
            var sendRequestsSent = clusterMembers.SelectMany(
                cluster => Enumerable.Range(0, sendingActors)
                    .Select(id => cluster.RequestAsync<Ack>($"snd-{id}", "sender", sendToRequest, maxWait)))
                .ToList();
            
            await Task.WhenAll(sendRequestsSent);

            var result = await clusterMembers.First().RequestAsync<AggregatorResult>(aggregatorId, "aggregator",
                new AskAggregator(),
                new CancellationTokenSource(5000).Token
            );
            
            result.Should().NotBeNull("We expect a response from the aggregator actor");
            result.SequenceKeyCount.Should().Be(sendRequestsSent.Count, "We expect a unique id per send request");
            result.SenderKeyCount.Should().Be(sendingActors, "We expect a single instantiation per sender id");
            result.OutOfOrderCount.Should().Be(0, "Messages from one actor to another should be received in order");
            result.TotalMessages.Should().Be(sendRequestsSent.Count * messagesSentPerCall);
        }

        protected async Task<IList<Cluster>> SpawnMembers(int memberCount)
        {
            var clusterName = "test-cluster." + Guid.NewGuid().ToString("N");
            var clusterTasks = Enumerable.Range(0, memberCount).Select(_ => SpawnMember(clusterName))
                .ToList();
            await Task.WhenAll(clusterTasks);
            return clusterTasks.Select(task => task.Result).ToList();
        }

        private async Task<Cluster> SpawnMember(string clusterName)
        {
            var system = new ActorSystem();
            var identityLookup = GetIdentityLookup(clusterName);
            
            var senderProps = Props.FromProducer(() => new SenderActor(TestOutputHelper));
            var aggProps = Props.FromProducer(() => new VerifyOrderActor());

            var clusterProvider = GetClusterProvider();
            var (clusterConfig, remoteConfig) = GetClusterConfig(clusterProvider, clusterName, identityLookup);
            clusterConfig = clusterConfig.WithClusterKind("sender", senderProps)
                            .WithClusterKind("aggregator", aggProps);

            var remote = new SelfHostedRemote(system, remoteConfig);
            var cluster = new Cluster(remote, clusterConfig);

            await cluster.StartMemberAsync();
            _clusters.Add(cluster);
            return cluster;
        }

        protected virtual IClusterProvider GetClusterProvider() => new TestProvider(new TestProviderOptions(), InMemAgent);

        protected virtual IIdentityLookup GetIdentityLookup(string clusterName) => new PartitionIdentityLookup();


        protected virtual (ClusterConfig, GrpcRemoteConfig) GetClusterConfig(IClusterProvider clusterProvider, string clusterName,
            IIdentityLookup identityLookup)
        {
            var portStr = Environment.GetEnvironmentVariable("PROTOPORT") ?? "0";
            var port = int.Parse(portStr);
            var host = Environment.GetEnvironmentVariable("PROTOHOST") ?? "127.0.0.1";
            var remoteConfig = GrpcRemoteConfig.BindTo(host, port)
                .WithProtoMessages(MessagesReflection.Descriptor)
                .WithAdvertisedHost(Environment.GetEnvironmentVariable("PROTOHOSTPUBLIC") ?? host!);
            var clusterConfig = ClusterConfig.Setup(clusterName, clusterProvider, identityLookup, remoteConfig);
            return (clusterConfig, remoteConfig);
        }


        private class SenderActor : IActor
        {
            private Cluster _cluster;
            private readonly ITestOutputHelper _testOutputHelper;

            private string _instanceId;
            private int _seq;

            public SenderActor(ITestOutputHelper testOutputHelper)
            {
                _testOutputHelper = testOutputHelper;
            }

            public async Task ReceiveAsync(IContext context)
            {
                switch (context.Message)
                {
                    case ClusterInit init:
                        _instanceId = $"{init.Kind}:{init.Identity}.{Guid.NewGuid():N}";
                        _cluster = init.Cluster;
                        break;
                    case SendToRequest sendTo:

                        var key = Guid.NewGuid().ToString("N");
                        for (var i = 0; i < sendTo.Count; i++)
                        {
                            try
                            {
                                await _cluster.RequestAsync<Ack>(sendTo.Id, "aggregator", new SequentialIdRequest
                                {
                                    SequenceKey = key,
                                    SequenceId = _seq++,
                                    Sender = _instanceId
                                }, CancellationToken.None
                                );
                            }
                            catch (Exception e)
                            {
                                _testOutputHelper.WriteLine("Failed to send to aggregator: {0}", e);
                            }
                        }

                        context.Respond(new Ack());
                        break;
                }
            }
        }

        private class VerifyOrderActor : IActor
        {
            private int _outOfOrderErrors;
            private int _seqRequests;

            private readonly Dictionary<string, int> _lastReceivedSeq = new Dictionary<string, int>();
            private readonly HashSet<string> _senders = new HashSet<string>();

            public Task ReceiveAsync(IContext context)
            {
                switch (context.Message)
                {
                    case SequentialIdRequest request:
                        HandleOrderedRequest(request, context);
                        break;
                    case AskAggregator _:
                        context.Respond(new AggregatorResult
                        {
                            SequenceKeyCount = _lastReceivedSeq.Count,
                            TotalMessages = _seqRequests,
                            OutOfOrderCount = _outOfOrderErrors,
                            SenderKeyCount = _senders.Count
                        }
                        );
                        break;
                }

                return Task.CompletedTask;
            }

            private void HandleOrderedRequest(SequentialIdRequest request, IContext context)
            {
                _seqRequests++;
                _senders.Add(request.Sender);
                var outOfOrder = _lastReceivedSeq.TryGetValue(request.SequenceKey, out var last) &&
                                 last + 1 != request.SequenceId;
                _lastReceivedSeq[request.SequenceKey] = request.SequenceId;
                if (outOfOrder)
                {
                    _outOfOrderErrors++;
                }

                context.Respond(new Ack());
            }
        }

        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            return Task.WhenAll(_clusters.Select(c => c.ShutdownAsync()));
        }
    }
}