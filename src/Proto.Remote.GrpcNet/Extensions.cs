// -----------------------------------------------------------------------
//   <copyright file="Extensions.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Net;
using Grpc.HealthCheck;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Proto.Remote
{
    public static class Extensions
    {
        public static IRemote AddRemote(this ActorSystem actorSystem, string hostname, int port,
            Action<RemoteConfiguration> configure)
        {
            var remote = new SelfHostedRemote(actorSystem, hostname, port, configure);
            return remote;
        }
        public static IServiceCollection AddRemote(this IServiceCollection services,
            string hostname, int port, Action<RemoteConfiguration, IServiceProvider> configure)
        {
            var remoteConfig = new AspRemoteConfig();
            var serialization = new Serialization();
            var remoteKindRegistry = new RemoteKindRegistry();
            services.AddSingleton(sp =>
            {
                var remoteConfiguration = new RemoteConfiguration(serialization, remoteKindRegistry, remoteConfig);
                configure.Invoke(remoteConfiguration, sp);
                if (!remoteConfiguration.RemoteConfig.UseHttps)
                    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                return remoteConfiguration;
            });
            services.AddSingleton(serialization);
            services.AddSingleton(remoteKindRegistry);
            services.AddSingleton(remoteConfig);
            AddAllServices(services, hostname, port);
            return services;
        }

        public static IServiceCollection AddRemote(this IServiceCollection services,
            string hostname, int port, Action<RemoteConfiguration> configure)
        {
            var remoteConfig = new AspRemoteConfig();
            var serialization = new Serialization();
            var remoteKindRegistry = new RemoteKindRegistry();
            services.AddSingleton(sp =>
            {
                var remoteConfiguration = new RemoteConfiguration(serialization, remoteKindRegistry, remoteConfig);
                configure.Invoke(remoteConfiguration);
                if (!remoteConfiguration.RemoteConfig.UseHttps)
                    AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
                return remoteConfiguration;
            });
            services.AddSingleton(serialization);
            services.AddSingleton(remoteKindRegistry);
            services.AddSingleton(remoteConfig);
            AddAllServices(services, hostname, port);
            return services;
        }

        private static void AddAllServices(IServiceCollection services, string hostname, int port)
        {
            services.TryAddSingleton<ActorSystem>();
            services.AddHostedService<RemoteHostedService>();
            services.AddSingleton<Remote, Remote>();
            services.AddSingleton<HostedRemote, HostedRemote>(sp =>
            {
                sp.GetRequiredService<RemoteConfiguration>();
                var remote = sp.GetRequiredService<Remote>();
                var serialization = sp.GetRequiredService<Serialization>();
                var remoteKindRegistry = sp.GetRequiredService<RemoteKindRegistry>();
                var system = sp.GetRequiredService<ActorSystem>();
                var remoteConfig = sp.GetRequiredService<RemoteConfig>();
                return new HostedRemote(hostname, port, remote,
                    serialization, remoteKindRegistry, system, remoteConfig);
            });
            services.AddSingleton<IRemote, HostedRemote>(sp => sp.GetRequiredService<HostedRemote>());
            services.AddSingleton<EndpointManager>();
            services.AddSingleton<RemoteConfig, AspRemoteConfig>(sp => sp.GetRequiredService<AspRemoteConfig>());
            services.AddSingleton<EndpointReader, EndpointReader>();
            services.AddSingleton<Remoting.RemotingBase, EndpointReader>(sp => sp.GetRequiredService<EndpointReader>());
            services.AddSingleton<IChannelProvider, ChannelProvider>();
        }

        private static GrpcServiceEndpointConventionBuilder AddProtoRemoteEndpoint(IEndpointRouteBuilder endpoints)
        {
            endpoints.MapGrpcService<HealthServiceImpl>();
            return endpoints.MapGrpcService<Remoting.RemotingBase>();
        }

        public static void UseProtoRemote(this IApplicationBuilder applicationBuilder)
        {
            var hostedRemote = applicationBuilder.ApplicationServices.GetRequiredService<HostedRemote>();
            hostedRemote.ServerAddressesFeature = applicationBuilder.ServerFeatures.Get<IServerAddressesFeature>();
            applicationBuilder.UseEndpoints(c => AddProtoRemoteEndpoint(c));
        }

        public static void UseProtoRemote(this IApplicationBuilder applicationBuilder, Action<GrpcServiceEndpointConventionBuilder> configure)
        {
            var hostedRemote = applicationBuilder.ApplicationServices.GetRequiredService<HostedRemote>();
            hostedRemote.ServerAddressesFeature = applicationBuilder.ServerFeatures.Get<IServerAddressesFeature>();
            applicationBuilder.UseEndpoints(c => configure(AddProtoRemoteEndpoint(c)));
        }
    }
}