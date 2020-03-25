// -----------------------------------------------------------------------
//   <copyright file="HostedRemoteService.cs" company="Asynkron HB">
//       Copyright (C) 2015-2020 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    internal class HostedRemoteService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly IRemote _remoting;
        private readonly EndpointManager _endpointManager;

        public HostedRemoteService(
            ILogger<HostedRemoteService> logger,
            IHostApplicationLifetime appLifetime,
            IRemote remoting,
            EndpointManager endpointManager)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _remoting = remoting;
            _endpointManager = endpointManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("StartAsync has been called.");
            _appLifetime.ApplicationStopping.Register(OnStopping);
            _appLifetime.ApplicationStarted.Register(OnStarted);
            return Task.CompletedTask;
        }

        private void OnStarted()
        {
            _logger.LogInformation("OnStarted has been called.");
            _remoting.Start().GetAwaiter().GetResult();
            _logger.LogInformation("OnStarted exits");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStopping()
        {
            _endpointManager.StopAsync().GetAwaiter().GetResult();
        }
    }
}