// -----------------------------------------------------------------------
//   <copyright file="SelfHostedRemoteService.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Proto.Remote.AspNetCore
{
    internal class SelfHostedRemoteService : IHostedService
    {
        private readonly ILogger _logger;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly EndpointManager _endpointManager;

        public SelfHostedRemoteService(
            ILogger<SelfHostedRemoteService> logger,
            IHostApplicationLifetime appLifetime,
            EndpointManager endpointManager)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _endpointManager = endpointManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _appLifetime.ApplicationStopping.Register(OnStopping);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        private void OnStopping()
        {
            _endpointManager.StopAsync().GetAwaiter().GetResult();
            System.Diagnostics.Process.GetCurrentProcess().Kill();
        }
    }
}