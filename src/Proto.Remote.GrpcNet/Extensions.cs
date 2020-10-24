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
        public static IRemote AddRemote(this ActorSystem actorSystem,
            GrpcNetRemoteConfig config)
        {
            var remote = new SelfHostedRemote(actorSystem, config);
            return remote;
        }
        public static IServiceCollection AddRemote(this IServiceCollection services, Func<IServiceProvider, GrpcNetRemoteConfig> configure)
        {
            services.AddSingleton(sp=> configure(sp));
            AddAllServices(services);
            return services;
        }

        public static IServiceCollection AddRemote(this IServiceCollection services,
            GrpcNetRemoteConfig config)
        {
            services.AddSingleton(config);
            AddAllServices(services);
            return services;
        }

        private static void AddAllServices(IServiceCollection services)
        {
            services.TryAddSingleton<ActorSystem>();
            services.AddHostedService<RemoteHostedService>();
            services.AddSingleton<HostedRemote>();
            services.AddSingleton<IRemote, HostedRemote>(sp => sp.GetRequiredService<HostedRemote>());
            services.AddSingleton<EndpointManager>();
            services.AddSingleton<RemoteConfig, GrpcNetRemoteConfig>(sp => sp.GetRequiredService<GrpcNetRemoteConfig>());
            services.AddSingleton<EndpointReader, EndpointReader>();
            services.AddSingleton<Serialization>(sp=> sp.GetRequiredService<RemoteConfig>().Serialization);
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