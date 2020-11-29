using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Proto.Extensions;

namespace Proto.DependencyInjection
{
    [PublicAPI]
    public class DIExtension : IActorSystemExtension<DIExtension>
    {
        public DIExtension(ActorSystem system, IDependencyResolver resolver)
        {
            Resolver = resolver;
            system.Extensions.Register(this);
        }
        public IDependencyResolver Resolver { get; }
    }

    public static class Extensions
    {
        public static ActorSystem WithServiceProvider(this ActorSystem actorSystem, IServiceProvider serviceProvider, Action<ActorPropsRegistry>? configureProps)
        {
            var actorPropsRegistry = new ActorPropsRegistry();
            configureProps?.Invoke(actorPropsRegistry);
            var dependencyResolver = new DependencyResolver(serviceProvider, actorSystem, actorPropsRegistry);
            _ = new DIExtension(actorSystem, dependencyResolver);
            return actorSystem;
        }

        /// <summary>
        /// Register ProtoActor in the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="registerAction">Actor factory props registration</param>
        public static IServiceCollection AddProtoActor(this IServiceCollection services,
            Action<ActorPropsRegistry>? configureProps = null)
        {
            var registry = new ActorPropsRegistry();
            configureProps?.Invoke(registry);
            services.AddSingleton(registry);
            services.AddSingleton<IDependencyResolver, DependencyResolver>();
            services.AddSingleton<DIExtension>();
            services.AddSingleton(new ActorSystem());
            return services;
        }

        /// <summary>
        /// Register ProtoActor in the service collection
        /// </summary>
        /// <param name="services">The service collection</param>
        /// <param name="registerAction">Actor factory props registration</param>
        public static IServiceCollection AddProtoActor(this IServiceCollection services,
            ActorSystemConfig config,
            Action<ActorPropsRegistry>? configureProps = null)
        {
            var registry = new ActorPropsRegistry();
            configureProps?.Invoke(registry);
            services.AddSingleton(registry);
            services.AddSingleton<IDependencyResolver, DependencyResolver>();
            services.AddSingleton<DIExtension>();
            services.AddSingleton(new ActorSystem(config));
            return services;
        }

        public static IDependencyResolver DI(this ActorSystem system) => system.Extensions.Get<DIExtension>().Resolver;
    }
}