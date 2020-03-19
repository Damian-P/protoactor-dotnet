﻿// -----------------------------------------------------------------------
//   <copyright file="Futures.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Proto
{
    internal class FutureProcess<T> : Process
    {
        private readonly CancellationTokenSource _cts;
        private readonly TaskCompletionSource<T> _tcs;

        internal FutureProcess(IActorSystem system, TimeSpan timeout) : this(system, new CancellationTokenSource(timeout))
        {

        }

        internal FutureProcess(IActorSystem system, CancellationToken cancellationToken) : this(system,
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
        )
        {
        }

        internal FutureProcess(IActorSystem system) : this(system, null)
        {
        }

        private FutureProcess(IActorSystem system, CancellationTokenSource? cts) : base(system)
        {
            _tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cts = cts;

            var name = System.ProcessRegistry.NextId();
            var (pid, absent) = System.ProcessRegistry.TryAdd(name, this);

            if (!absent)
            {
                throw new ProcessNameExistException(name, pid);
            }

            Pid = pid;

            if (_cts != null)
            {
                _cts.Token.Register(
                    () =>
                    {
                        if (_tcs.Task.IsCompleted)
                        {
                            return;
                        }

                        _tcs.TrySetException(
                            new TimeoutException("Request didn't receive any Response within the expected time.")
                        );

                        Stop(pid);
                    }
                );
                Task = _tcs.Task;
            }
            else
            {
                Task = _tcs.Task;
            }
        }

        public PID Pid { get; }
        public Task<T> Task { get; }

        protected internal override void SendUserMessage(PID pid, object message)
        {
            var msg = MessageEnvelope.UnwrapMessage(message);

            try
            {
                if (msg is T || msg == null)
                {
                    _tcs.TrySetResult((T)msg);
                }
                else
                {
                    _tcs.TrySetException(
                        new InvalidOperationException(
                            $"Unexpected message. Was type {msg.GetType()} but expected {typeof(T)}"
                        )
                    );
                }
            }
            finally
            {
                Stop(Pid);
            }
        }

        protected internal override void SendSystemMessage(PID pid, object message)
        {
            if (message is Stop)
            {
                System.ProcessRegistry.Remove(Pid);
                _cts?.Dispose();
                return;
            }

            if (_cts == null || !_cts.IsCancellationRequested)
            {
                _tcs.TrySetResult(default);
            }

            Stop(pid);
        }
    }
}