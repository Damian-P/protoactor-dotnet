using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Proto;
using Proto.Cluster;
using Messages;
using Microsoft.Extensions.Logging;
using System.Threading;
using Polly;
using System.Collections.Generic;

namespace Client
{
    public class ClientService : IHostedService
    {
        private readonly ActorSystem _actorSystem;
        private readonly Grains _grains;
        private readonly ILogger<ClientService> _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        public ClientService(
            ActorSystem actorSystem,
            Grains grains,
            ILogger<ClientService> logger,
            IHostApplicationLifetime appLifetime)
        {
            _actorSystem = actorSystem;
            _grains = grains;
            _logger = logger;
            _appLifetime = appLifetime;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStarted.Register(OnStarted);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _ = Task.Run(async () =>
                {
                    _actorSystem.EventStream.Subscribe<ClusterTopologyEvent>(e =>
                        _logger.LogInformation("Topology changed {@Event}", e)
                    );
                    _actorSystem.EventStream.Subscribe<MemberStatusEvent>(e =>
                        _logger.LogInformation("Member status {@Event}", e)
                    );

                    var options = new GrainCallOptions
                    {
                        RetryCount = 10,
                        RetryAction = i =>
                        {
                            _logger.LogCritical("!");
                            return Task.Delay(50);
                        }
                    };

                    await Task.Delay(2000);
                    _logger.LogCritical("Starting to send !");

                    var policy = Policy.Handle<Exception>().RetryForeverAsync();
                    var n = 1_000_000;
                    var tasks = new List<Task>();
                    for (var i = 0; i < n; i++)
                    {
                        var client = _grains.HelloGrain("name" + i % 200);
                        tasks.Add(policy.ExecuteAsync(() =>
                                client.SayHello(new HelloRequest(), new CancellationTokenSource(2000).Token, options)
                            )
                        );
                        if (tasks.Count % 1000 == 0)
                        {
                            Task.WaitAll(tasks.ToArray());
                            tasks.Clear();
                        }
                    }

                    Task.WaitAll(tasks.ToArray());
                    _logger.LogCritical("Done!");
                    _appLifetime.StopApplication();
                }, _appLifetime.ApplicationStopping
            );
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}