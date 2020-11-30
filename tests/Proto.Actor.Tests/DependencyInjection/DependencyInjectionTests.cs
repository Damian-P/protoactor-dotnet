using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Proto.DependencyInjection;
using Xunit;

namespace Proto.Tests.DependencyInjection
{

    public class DependencyInjectionTests
    {
        public interface ISampleActor : IActor
        {

        }
        public class SampleActor : ISampleActor
        {
            public static bool Created { get; private set; }
            public SampleActor() => Created = true;
            public Task ReceiveAsync(IContext context)=> Task.CompletedTask;
        }
        public class FooDep
        {

        }

        public class BarDep
        {

        }
        public class DiActor : IActor
        {
            public BarDep Bar { get; }
            public FooDep Foo { get; }

            public DiActor(FooDep foo, BarDep bar)
            {
                Foo = foo;
                Bar = bar;
            }

            public Task ReceiveAsync(IContext context)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public void CanResolveDependencies()
        {
            var s = new ServiceCollection();
            s.AddSingleton<FooDep>();
            s.AddSingleton<BarDep>();
            s.AddTransient<DiActor>();
            var provider = s.BuildServiceProvider();

            var system = new ActorSystem();
            var actorPropsRegistry = new ActorPropsRegistry();
            var resolver = new DependencyResolver(provider, system, actorPropsRegistry);
            var plugin = new DIExtension(system, resolver);

            var props = system.DI().PropsFor<DiActor>();
            var actor = (DiActor)props.Producer(system);

            Assert.NotNull(props);
            Assert.NotNull(actor);
            Assert.NotNull(actor.Bar);
            Assert.NotNull(actor.Foo);

        }

        [Fact]
        public async void SpawnActor()
        {
            var services = new ServiceCollection();
            services.AddProtoActor();

            var provider = services.BuildServiceProvider();
            var system = provider.GetRequiredService<ActorSystem>();

            var pid = system.DI().GetActor<SampleActor>();

            system.Root.Send(pid, "hello");

            await system.Root.StopAsync(pid);

            Assert.True(SampleActor.Created);
        }

        [Fact]
        public async void SpawnActorFromInterface()
        {
            var services = new ServiceCollection();
            services.AddProtoActor();
            services.AddTransient<ISampleActor, SampleActor>();

            var provider = services.BuildServiceProvider();
            var system = provider.GetRequiredService<ActorSystem>();

            var pid = system.DI().GetActor<ISampleActor>();

            system.Root.Send(pid, "hello");

            await system.Root.StopAsync(pid);

            Assert.True(SampleActor.Created);
        }

        [Fact]
        public async void Should_register_by_type()
        {
            var services = new ServiceCollection();
            var created = false;

            IActor Producer()
            {
                created = true;
                return new SampleActor();
            }

            services.AddProtoActor(register => register.RegisterProps(typeof(SampleActor), p => p.WithProducer(Producer)));

            var provider = services.BuildServiceProvider();
            var system = provider.GetRequiredService<ActorSystem>();

            var pid = system.DI().GetActor<SampleActor>();

            await system.Root.StopAsync(pid);

            Assert.True(created);
        }

        [Fact]
        public void Should_throw_if_not_actor_type()
        {
            var services = new ServiceCollection();
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                services.AddProtoActor(register => register.RegisterProps(GetType(), p => p));
            });

            Assert.Equal($"Type {GetType().FullName} must implement {typeof(IActor).FullName}", ex.Message);
        }
    }
}