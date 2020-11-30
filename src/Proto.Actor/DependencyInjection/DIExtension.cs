using System;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
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

        public static IServiceCollection AddProtoActor(this IServiceCollection services,
            Action<ActorPropsRegistry>? configureProps = null)
        {
            var registry = new ActorPropsRegistry();
            configureProps?.Invoke(registry);
            services.AddSingleton(registry);
            services.AddSingleton(sp =>
            {
                var system = new ActorSystem();
                var dependencyResolver = new DependencyResolver(sp, system, registry);
                _ = new DIExtension(system, dependencyResolver);
                return system;
            });
            return services;
        }

        public static IServiceCollection AddProtoActor(this IServiceCollection services,
            ActorSystemConfig config,
            Action<ActorPropsRegistry>? configureProps = null)
        {
            var registry = new ActorPropsRegistry();
            configureProps?.Invoke(registry);
            services.AddSingleton(registry);
            services.AddSingleton(sp =>
            {
                var system = new ActorSystem(config);
                var dependencyResolver = new DependencyResolver(sp, system, registry);
                _ = new DIExtension(system, dependencyResolver);
                return system;
            });
            return services;
        }

        public static IDependencyResolver DI(this ActorSystem system) => system.Extensions.Get<DIExtension>().Resolver;
    }
}