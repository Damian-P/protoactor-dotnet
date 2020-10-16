using System;
using System.Threading.Tasks;
using Xunit;

namespace Proto.Remote.Tests
{
    public class RemoteKindsRegistrationTests
    {
        [Fact]
        public void CanRegisterKind()
        {
            var props = new Props();
            var kind = Guid.NewGuid().ToString();
            var remoteConfig = GrpcRemoteConfig.BindToLocalhost()
                .WithRemoteKinds((kind, props));

            Assert.Equal(props, remoteConfig.GetRemoteKind(kind));
        }

        [Fact]
        public void CanRegisterMultipleKinds()
        {
            var props = new Props();
            var kind1 = Guid.NewGuid().ToString();
            var kind2 = Guid.NewGuid().ToString();
            var remoteConfig = GrpcRemoteConfig.BindToLocalhost()
                .WithRemoteKinds(
                    (kind1, props),
                    (kind2, props));

            var kinds = remoteConfig.GetRemoteKinds();
            Assert.Contains(kind1, kinds);
            Assert.Contains(kind2, kinds);
        }

        [Fact]
        public void UnknownKindThrowsException()
        {
            var remoteConfig = GrpcRemoteConfig.BindToLocalhost();
            Assert.Throws<ArgumentException>(() => { remoteConfig.GetRemoteKind("not registered"); });
        }
    }
}