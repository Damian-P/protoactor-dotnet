using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Proto.DependencyInjection
{
    public class DependencyResolver : IDependencyResolver
    {
        private readonly IServiceProvider _services;
        private readonly ActorSystem _actorSystem;
        private readonly ActorPropsRegistry _actorPropsRegistry;

        public DependencyResolver(IServiceProvider services, ActorSystem actorSystem, ActorPropsRegistry actorPropsRegistry)
        {
            _services = services;
            _actorSystem = actorSystem;
            _actorPropsRegistry = actorPropsRegistry;
        }

        public Props PropsFor<TActor>() where TActor : IActor
        {
            if (!_actorPropsRegistry.RegisteredProps.TryGetValue(typeof(TActor), out var props))
            {
                props = x => x;
            }
            return props(Props.FromProducer(() => ActivatorUtilities.GetServiceOrCreateInstance<TActor>(_services)));
        }

        public Props PropsFor(Type actorType)
        {
            if (!typeof(IActor).GetTypeInfo().IsAssignableFrom(actorType))
            {
                throw new InvalidOperationException($"Type {actorType.FullName} must implement {typeof(IActor).FullName}");
            }
            if (!_actorPropsRegistry.RegisteredProps.TryGetValue(actorType, out var props))
            {
                props = x => x;
            }
            return props(Props.FromProducer(() => (IActor)ActivatorUtilities.GetServiceOrCreateInstance(_services, actorType)));
        }

        public PID RegisterActor<T>(T actor, string? id = null, string? address = null, IContext? parent = null)
            where T : IActor
        {
            id ??= typeof(T).FullName;
            return GetActor(id, address, parent, () => CreateActor<T>(id, parent, () => new Props().WithProducer(() => actor)));
        }

        public PID GetActor(string id, string? address = null, IContext? parent = null)
        {
            return GetActor(id, address, parent, () => throw new InvalidOperationException($"Actor not created {id}"));
        }

        public PID GetActor<T>(string? id = null, string? address = null, IContext? parent = null)
            where T : IActor
        {
            id ??= typeof(T).FullName;
            return GetActor(id, address, parent, () => CreateActor<T>(id, parent, () => new Props().WithProducer(() => ActivatorUtilities.GetServiceOrCreateInstance<T>(_services))));
        }

        private PID GetActor(string id, string? address, IContext? parent, Func<PID> create)
        {
            address ??= "nonhost";

            var pidId = id;
            if (parent != null)
            {
                pidId = $"{parent.Self!.Id}/{id}";
            }

            var pid = PID.FromAddress(address, pidId);
            var reff = _actorSystem.ProcessRegistry.Get(pid);
            if (reff is DeadLetterProcess)
            {
                pid = create();
            }
            return pid;
        }

        private PID CreateActor<T>(string id, IContext? parent, Func<Props> producer)
            where T : IActor
        {
            if (!_actorPropsRegistry.RegisteredProps.TryGetValue(typeof(T), out var props))
                props = x => x;

            var props2 = props(producer());
            if (parent == null)
            {
                return _actorSystem.Root.SpawnNamed(props2, id);
            }
            return parent.SpawnNamed(props2, id);
        }
    }
}