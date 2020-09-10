﻿// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron AB">
//      Copyright (C) 2015-2020 Asynkron AB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Messages;
using Microsoft.Extensions.Logging;
using Proto;
using Proto.Remote;
using ProtosReflection = Messages.ProtosReflection;

class Program
{
    static async Task Main(string[] args)
    {
        Log.SetLoggerFactory(LoggerFactory.Create(c => c
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Proto.EventStream", LogLevel.None)
            .AddConsole()
        ));
        var system = new ActorSystem();
        var context = new RootContext(system);
        var Remote = system.AddRemote("127.0.0.1", 8081, remoteConfiguration =>
        {
            remoteConfiguration.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            remoteConfiguration.RemoteConfig.EndpointWriterOptions.MaxRetries = 1;
        });
        Remote.Start();
        var messageCount = 1000000;
        var cancellationTokenSource = new CancellationTokenSource();
        var semaphore = new SemaphoreSlim(0);
        var props = Props.FromProducer(() => new LocalActor(0, messageCount, semaphore));

        var pid = context.Spawn(props);
        var remote = new PID{ Address = "127.0.0.1:8080", Id = "remote"};
        await context.RequestAsync<Start>(remote, new StartRemote { Sender = pid });
        var start = DateTime.Now;
        Console.WriteLine("Starting to send");
        var msg = new Ping();
        for (var i = 0; i < messageCount; i++)
        {
            context.Send(remote, msg);
        }
        await semaphore.WaitAsync(cancellationTokenSource.Token);
        var elapsed = DateTime.Now - start;
        Console.WriteLine("Elapsed {0}", elapsed);

        var t = messageCount * 2.0 / elapsed.TotalMilliseconds * 1000;
        Console.WriteLine("Throughput {0} msg / sec", t);

        Console.ReadLine();
        await Remote.ShutdownAsync();
    }

    public class LocalActor : IActor
    {
        private int _count;
        private readonly int _messageCount;
        private readonly SemaphoreSlim _wg;

        public LocalActor(int count, int messageCount, SemaphoreSlim wg)
        {
            _count = count;
            _messageCount = messageCount;
            _wg = wg;
        }


        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Pong _:
                    _count++;
                    if (_count % 50000 == 0)
                    {
                        Console.WriteLine(_count);
                    }
                    if (_count == _messageCount)
                    {
                        _wg.Release();
                    }
                    break;
            }
            return Actor.Done;
        }
    }
}