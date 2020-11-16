using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Proto.Remote.GrpcCore;
using Proto.Remote.GrpcNet;
using Xunit;

// ReSharper disable MethodHasAsyncOverload

namespace Proto.Remote.Tests
{
    public class HostedGrpcNetClientWithGrpcCoreServerTests : RemoteTests, IClassFixture<HostedGrpcNetClientWithGrpcCoreServerTests.HostedGrpcNetClientWithGrpcCoreServerFixture>
    {
        public class HostedGrpcNetClientWithGrpcCoreServerFixture : RemoteFixture
        {
            private readonly IHost _clientHost;
            public HostedGrpcNetClientWithGrpcCoreServerFixture()
            {
                var clientConfig = ConfigureClientRemoteConfig(GrpcNetRemoteConfig.BindToLocalhost(5000));
                (_clientHost, Remote) = GetHostedGrpcNetRemote(clientConfig);
                var serverConfig = ConfigureServerRemoteConfig(GrpcCoreRemoteConfig.BindToLocalhost(5001));
                ServerRemote = GetGrpcCoreRemote(serverConfig);
            }
            public override async Task DisposeAsync()
            {
                await _clientHost.StopAsync();
                _clientHost.Dispose();
                await ServerRemote.ShutdownAsync();
            }
        }
        public HostedGrpcNetClientWithGrpcCoreServerTests(HostedGrpcNetClientWithGrpcCoreServerFixture fixture) : base(fixture)
        {
        }
    }
}