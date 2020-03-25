// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Proto.Remote.AspNetCore;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static ActorSystem AddRemotingOverAspNet(this ActorSystem actorSystem, string hostanme, int port, Action<RemotingConfiguration> configure = null)
        {
            var remote = new SelfHostedRemoteServerOverAspNet(actorSystem, hostanme, port, configure);
            return actorSystem;
        }

        public static IServiceCollection AddRemote(this IServiceCollection services, Action<RemotingConfiguration> configure)
        {
            services.AddHostedService<HostedRemoteService>();
            services.AddSingleton<RemotingConfiguration>(sp =>
            {
                var remotingConfiguration = new RemotingConfiguration();
                configure?.Invoke(remotingConfiguration);
                if (remotingConfiguration.RemoteConfig.ServerCredentials == ServerCredentials.Insecure)
                    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                return remotingConfiguration;
            });
            services.AddSingleton<IRemote, HostedRemote>();
            services.AddSingleton<EndpointManager>();
            services.AddSingleton<IChannelProvider, ChannelProvider>();
            services.AddSingleton<Serialization>(sp => sp.GetRequiredService<RemotingConfiguration>().Serialization);
            services.AddSingleton<RemoteKindRegistry>(sp => sp.GetRequiredService<RemotingConfiguration>().RemoteKindRegistry);
            services.AddSingleton<RemoteConfig>(sp => sp.GetRequiredService<RemotingConfiguration>().RemoteConfig);
            services.AddSingleton<Remoting.RemotingBase, EndpointReader>();
            return services;
        }

        public static GrpcServiceEndpointConventionBuilder MapProtoRemoteService(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapGrpcService<Remoting.RemotingBase>();
        }
    }
}