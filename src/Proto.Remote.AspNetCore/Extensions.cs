// -----------------------------------------------------------------------
//   <copyright file="EndpointWriter.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Proto.Remote.AspNetCore
{
    internal static class FakeApp
    {
        internal static void Main(string[] args) { }
    }

    public static class Extensions
    {
        public static IServiceCollection AddRemoteActorSystem(this IServiceCollection services,
            string hostname,
            int port,
            RemoteConfig remoteConfig = null,
            Action<ActorPropsRegistry> configureActorPropsRegistry = null,
            Action<RemoteKindRegistry> configureRemoteKindRegistry = null,
            Action<Serialization> configureSerialization = null)
        {
            services.AddSingleton<IRemoteActorSystem>(sp =>
            {
                var remoteActorSystem = new RemoteActorSystem(hostname, port, remoteConfig);
                var remoteKindRegistry = new RemoteKindRegistry();
                configureRemoteKindRegistry?.Invoke(remoteActorSystem.RemoteKindRegistry);
                configureSerialization?.Invoke(remoteActorSystem.Serialization);
                return remoteActorSystem;
            });
            services.AddSingleton<EndpointReader>();
            return services;
        }
    }
}
