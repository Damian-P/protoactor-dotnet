using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote.Tests.Node
{
    public class ProtoService : IHostedService
    {
        private readonly ILogger<ProtoService> _logger;
        private readonly int _port;
        private readonly string _host;
        private IRemote _remote;

        public ProtoService(IConfiguration configuration, ILogger<ProtoService> logger, ILoggerFactory loggerFactory)
        {
            Log.SetLoggerFactory(loggerFactory);
            _logger = logger;
            _host = configuration["host"];
            _port = configuration.GetValue<int>("port");
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ProtoService starting on {Host}:{Port}...", _host, _port);

            var actorSystem = new ActorSystem();

            var props = Props.FromProducer(() => new EchoActor(_host, _port));

            _remote = new SelfHostedRemoteServerOverGrpc(actorSystem, _host, _port, remote =>
            {
                remote.Serialization.RegisterFileDescriptor(Messages.ProtosReflection.Descriptor);
                remote.RemoteKindRegistry.RegisterKnownKind("EchoActor", props);
            });
            _remote.Start();

            actorSystem.Root.SpawnNamed(props, "EchoActorInstance");

            _logger.LogInformation("ProtoService started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ProtoService stopping...");
            _remote.Stop();
            return Task.CompletedTask;
        }
    }
}