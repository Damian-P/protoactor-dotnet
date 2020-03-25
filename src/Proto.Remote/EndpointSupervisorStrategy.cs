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
        private readonly ActorSystem _actorSystem;
        private readonly TimeSpan? _withinTimeSpan;
        private readonly CancellationTokenSource _cancelFutureRetries;

        private int _backoff;
        private readonly string _address;

        public EndpointSupervisorStrategy(string address, ActorSystem actorSystem, EndpointWriterOptions endpointWriterOptions)
        {
            _logger = Log.CreateLogger<EndpointSupervisorStrategy>();
            _address = address;
            _actorSystem = actorSystem;
            _cancelFutureRetries = new CancellationTokenSource();
            _maxNrOfRetries = endpointWriterOptions.MaxRetries;
            _withinTimeSpan = endpointWriterOptions.RetryTimeSpan;
            _backoff = endpointWriterOptions.RetryBackOffms;
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
                var terminated = new EndpointTerminatedEvent { Address = _address };
                _actorSystem.EventStream.Publish(terminated);
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
                            _logger.LogInformation(
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