// -----------------------------------------------------------------------
//   <copyright file="EndpointSupervisorStrategy.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public class EndpointSupervisorStrategy : ISupervisorStrategy
    {
        private readonly ILogger _logger;

        private readonly int _maxNrOfRetries;
        private readonly Random _random = new Random();
        private readonly Remote _remote;
        private readonly TimeSpan? _withinTimeSpan;
        private readonly CancellationTokenSource _cancelFutureRetries;

        private int _backoff;
        private readonly string _address;

        public EndpointSupervisorStrategy(string address, Remote remote)
        {
            _logger = Log.CreateLogger<EndpointSupervisorStrategy>();
            _address = address;
            _remote = remote;
            _cancelFutureRetries = new CancellationTokenSource();
            _maxNrOfRetries = remote.RemoteConfig.EndpointWriterOptions.MaxRetries;
            _withinTimeSpan = remote.RemoteConfig.EndpointWriterOptions.RetryTimeSpan;
            _backoff = remote.RemoteConfig.EndpointWriterOptions.RetryBackOffms;
        }

        public void HandleFailure(
            ISupervisor supervisor, PID child, RestartStatistics rs, Exception reason,
            object message
        )
        {
            if (ShouldStop(rs))
            {
                _logger.LogWarning(
                    "Stopping connection to address {Address} after retries expired because of {Reason}",
                    _address, reason
                );

                _cancelFutureRetries.Cancel();
                supervisor.StopChildren(child);
                _remote.System.ProcessRegistry
                    .Remove(child); //TODO: work out why this hangs around in the process registry

                var terminated = new EndpointTerminatedEvent {Address = _address};
                _remote.System.EventStream.Publish(terminated);
            }
            else
            {
                _backoff *= 2;
                var noise = _random.Next(_backoff);
                var duration = TimeSpan.FromMilliseconds(_backoff + noise);

                Task.Delay(duration)
                    .ContinueWith(
                        t =>
                        {
                            _logger.LogWarning(
                                "Restarting {Actor} after {Duration} because of {Reason}",
                                child.ToShortString(), duration, reason
                            );
                            supervisor.RestartChildren(reason, child);
                        }, _cancelFutureRetries.Token
                    );
            }
        }

        private bool ShouldStop(RestartStatistics rs)
        {
            if (_maxNrOfRetries == 0)
            {
                return true;
            }

            rs.Fail();

            if (rs.NumberOfFailures(_withinTimeSpan) > _maxNrOfRetries)
            {
                rs.Reset();
                return true;
            }

            return false;
        }
    }
}