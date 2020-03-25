// -----------------------------------------------------------------------
//   <copyright file="ClusterHostedService.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Cluster
{
    public class ClusterHostedService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly Cluster _cluster;

        public ClusterHostedService(
            ILogger<ClusterHostedService> logger,
            IHostApplicationLifetime appLifetime,
            Cluster cluster)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _cluster = cluster;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping);
            _appLifetime.ApplicationStarted.Register(OnStarted);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _cluster.Start().GetAwaiter().GetResult();
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStopping()
        {
            _cluster.Shutdown().GetAwaiter().GetResult();
        }
    }
}