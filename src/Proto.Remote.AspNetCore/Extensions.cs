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
using Microsoft.Extensions.Logging;
using Proto.Remote.AspNetCore;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static IRemote AddRemoteOverAspNet(this ActorSystem actorSystem, string hostname, int port,
            Action<IRemoteConfiguration> configure = null)
        {
            return new SelfHostedRemoteServerOverAspNet(actorSystem, hostname, port, configure);
            ;
        }

        public static IServiceCollection AddRemote(this IServiceCollection services,
            Action<IRemoteConfiguration> configure)
        {
            services.AddHostedService<RemoteHostedService>();
            services.AddSingleton<IRemote, HostedRemote>(sp =>
                {
                    var actorSystem = sp.GetRequiredService<ActorSystem>();
                    var logger = sp.GetRequiredService<ILogger<HostedRemote>>();
                    var channelProvider = sp.GetRequiredService<IChannelProvider>();

                    var remote = new HostedRemote(actorSystem, logger, channelProvider);
                    configure.Invoke(remote);
                    return remote;
                }
            );
            services.AddSingleton<EndpointManager>(sp =>
                (sp.GetRequiredService<IRemote>() as HostedRemote).EndpointManager
            );
            services.AddSingleton<IChannelProvider, ChannelProvider>();
            services.AddSingleton<Serialization>(sp => sp.GetRequiredService<IRemote>().Serialization);
            services.AddSingleton<RemoteKindRegistry>(sp => sp.GetRequiredService<IRemote>().RemoteKindRegistry);
            services.AddSingleton<RemoteConfig>(sp => sp.GetRequiredService<IRemote>().RemoteConfig);
            services.AddSingleton<Remoting.RemotingBase, EndpointReader>();
            return services;
        }

        public static GrpcServiceEndpointConventionBuilder MapProtoRemoteService(this IEndpointRouteBuilder endpoints)
        {
            return endpoints.MapGrpcService<Remoting.RemotingBase>();
        }
    }
}