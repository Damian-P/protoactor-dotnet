// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.HealthCheck;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class ChannelProvider : IChannelProvider
    {
        public ChannelBase GetChannel(ChannelCredentials channelCredentials, string address, IEnumerable<ChannelOption> channelOptions)
        {
            var addressWithProtocol =
                $"{(channelCredentials == ChannelCredentials.Insecure ? "http://" : "https://")}{address}";
            if (channelOptions != null)
                foreach (var channelOption in channelOptions)
                {
                    switch (channelOption.Name)
                    {
                        case ChannelOptions.Census:
                            break;
                        default:
                            break;
                    }
                }
            var channel = GrpcChannel.ForAddress(addressWithProtocol);

            return channel;
        }
    }
    public static class Extensions
    {
        public static IRemote AddRemote(this ActorSystem actorSystem, string hostname, int port,
            Action<IRemoteConfiguration>? configure = null)
        {
            var remote = new SelfHostedRemote(actorSystem, hostname, port, configure);
            return remote;
        }
        public static void StartRemote(this ActorSystem actorSystem)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            remote.Start();
        }

        public static Task ShutdownRemoteAsync(this ActorSystem actorSystem, bool graceful = true)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.ShutdownAsync(graceful);
        }

        public static Task<ActorPidResponse> SpawnAsync(this ActorSystem actorSystem, string address, string kind,
            TimeSpan timeout)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.SpawnAsync(address, kind, timeout);
        }

        public static Task<ActorPidResponse> SpawnNamedAsync(this ActorSystem actorSystem, string address, string name,
            string kind, TimeSpan timeout)
        {
            var remote = actorSystem.Plugins.GetPlugin<IRemote>();
            return remote.SpawnNamedAsync(address, name, kind, timeout);
        }
        public static IServiceCollection AddRemote(this IServiceCollection services,
            Action<IRemoteConfiguration, IServiceProvider> configure)
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
            services.AddSingleton<IChannelProvider, ChannelProvider>();
            return services;
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
                (sp.GetRequiredService<IRemote>() as HostedRemote)!.EndpointManager
            );
            services.AddSingleton<Serialization>(sp => sp.GetRequiredService<IRemote>().Serialization);
            services.AddSingleton<RemoteKindRegistry>(sp => sp.GetRequiredService<IRemote>().RemoteKindRegistry);
            services.AddSingleton<RemoteConfig>(sp => sp.GetRequiredService<IRemote>().RemoteConfig);
            services.AddSingleton<Remoting.RemotingBase, EndpointReader>();
            services.AddSingleton<IChannelProvider, ChannelProvider>();
            return services;
        }

        public static GrpcServiceEndpointConventionBuilder MapProtoRemoteService(this IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGrpcService<HealthServiceImpl>();
            return endpoints.MapGrpcService<Remoting.RemotingBase>();
        }
    }
}