// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.HealthCheck;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static IRemote AddRemote(this ActorSystem actorSystem, string hostname, int port,
            Action<IRemoteConfiguration>? configure = null, Action<GrpcChannelOptions>? configureChannelOptions = null, Action<ListenOptions>? configureKestrel = null)
        {
            var remote = new SelfHostedRemote(actorSystem, hostname, port, configure, configureChannelOptions, configureKestrel);
            return remote;
        }
        public static IServiceCollection AddRemote(this IServiceCollection services,
            Action<IRemoteConfiguration, IServiceProvider> configure, Action<GrpcChannelOptions>? configureChannelOptions = null)
        {
            services.AddHostedService<RemoteHostedService>();
            services.AddSingleton<IRemote, HostedRemote>(sp =>
                {
                    var actorSystem = sp.GetRequiredService<ActorSystem>();
                    var logger = sp.GetRequiredService<ILogger<HostedRemote>>();
                    var channelProvider = sp.GetRequiredService<IChannelProvider>();
                    var remote = new HostedRemote(actorSystem, logger, channelProvider);
                    configure.Invoke(remote, sp);
                    return remote;
                }
            );
            services.AddSingleton<EndpointManager>(sp =>
                (sp.GetRequiredService<IRemote>() as HostedRemote)!.EndpointManager
            );
            services.AddSingleton<Serialization>(sp => sp.GetRequiredService<IRemote>().Serialization);
            services.AddSingleton<RemoteKindRegistry>(sp => sp.GetRequiredService<IRemote>().RemoteKindRegistry);
            services.AddSingleton<RemoteConfig>(sp => sp.GetRequiredService<IRemote>().RemoteConfig);
            services.AddSingleton<Remoting.RemotingBase, EndpointReader>();
            services.AddSingleton<IChannelProvider>(sp => new ChannelProvider(configureChannelOptions));
            return services;
        }

        public static IServiceCollection AddRemote(this IServiceCollection services,
            Action<IRemoteConfiguration> configure, Action<GrpcChannelOptions>? configureChannelOptions = null)
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
                (sp.GetRequiredService<IRemote>() as HostedRemote)!.EndpointManager
            );
            services.AddSingleton<Serialization>(sp => sp.GetRequiredService<IRemote>().Serialization);
            services.AddSingleton<RemoteKindRegistry>(sp => sp.GetRequiredService<IRemote>().RemoteKindRegistry);
            services.AddSingleton<RemoteConfig>(sp => sp.GetRequiredService<IRemote>().RemoteConfig);
            services.AddSingleton<Remoting.RemotingBase, EndpointReader>();
            services.AddSingleton<IChannelProvider>(sp => new ChannelProvider(configureChannelOptions));
            return services;
        }

        public static GrpcServiceEndpointConventionBuilder MapProtoRemoteService(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGrpcService<HealthServiceImpl>();
            return endpoints.MapGrpcService<Remoting.RemotingBase>();
        }
    }
}