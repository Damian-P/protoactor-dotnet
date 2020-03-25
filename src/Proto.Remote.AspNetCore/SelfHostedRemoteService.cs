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
        private readonly IRemote _remote;

        public SelfHostedRemoteService(
            ILogger<SelfHostedRemoteService> logger,
            IHostApplicationLifetime appLifetime,
            IRemote remote)
        {
            _logger = logger;
            _appLifetime = appLifetime;
            _remote = remote;
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
            _remote.Stop(false).GetAwaiter().GetResult();
        }
    }
}