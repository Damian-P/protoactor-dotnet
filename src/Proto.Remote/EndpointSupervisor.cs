// -----------------------------------------------------------------------
//   <copyright file="EndpointSupervisor.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Proto.Remote
{
    public abstract class EndpointSupervisor : IActor, ISupervisorStrategy
    {
        private static readonly ILogger Logger = Log.CreateLogger<EndpointSupervisor>();

        private readonly int _maxNrOfRetries;
        private readonly Random _random = new Random();
        private readonly RemoteActorSystemBase _remote;
        private readonly TimeSpan? _withinTimeSpan;
        private CancellationTokenSource _cancelFutureRetries;

        private int _backoff;
        protected string _address;

        public EndpointSupervisor(RemoteActorSystemBase remote)
        {
            _remote = remote;
            _maxNrOfRetries = remote.RemoteConfig.EndpointWriterOptions.MaxRetries;
            _withinTimeSpan = remote.RemoteConfig.EndpointWriterOptions.RetryTimeSpan;
            _backoff = remote.RemoteConfig.EndpointWriterOptions.RetryBackOffms;
        }

        public Task ReceiveAsync(IContext context)
        {
            if (context.Message is string address)
            {
                _address = address;
                var watcher = SpawnWatcher(address, context);
                var writer = SpawnWriter(address, context);
                _cancelFutureRetries = new CancellationTokenSource();
                context.Respond(new Endpoint(writer, watcher));
            }

            return Actor.Done;
        }

        public void HandleFailure(
            ISupervisor supervisor, PID child, RestartStatistics rs, Exception reason,
            object message
        )
        {
            if (ShouldStop(rs))
            {
                Logger.LogWarning(
                    "Stopping connection to address {Address} after retries expired because of {Reason}",
                    _address, reason
                );

                _cancelFutureRetries.Cancel();
                supervisor.StopChildren(child);
                _remote.ProcessRegistry.Remove(child); //TODO: work out why this hangs around in the process registry

                var terminated = new EndpointTerminatedEvent {Address = _address};
                _remote.EventStream.Publish(terminated);
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
                            Logger.LogWarning(
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

        private PID SpawnWatcher(string address, ISpawnerContext context)
        {
            var watcherProps = Props.FromProducer(() => new EndpointWatcher(_remote, address));
            var watcher = context.Spawn(watcherProps);
            return watcher;
        }

        public abstract PID SpawnWriter(string address, ISpawnerContext context);
    }
}