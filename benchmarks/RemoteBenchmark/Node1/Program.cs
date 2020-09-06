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
        Log.SetLoggerFactory(LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information)));
        var system = new ActorSystem();
        var remote = system.AddRemote("127.0.0.1", 12001, remote =>{
            remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
        });
        remote.Start();

        var messageCount = 1_000_000;
        var wg = new AutoResetEvent(false);
        var props = Props.FromProducer(() => new LocalActor(0, messageCount, wg));

        var pid = system.Root.Spawn(props);
        var remoteActor = new PID("127.0.0.1:12000", "remote");
        await system.Root.RequestAsync<Start>(remoteActor, new StartRemote { Sender = pid });
        
        var start = DateTime.Now;
        Console.WriteLine("Starting to send");
        var msg = new Ping();
        for (var i = 0; i < messageCount; i++)
        {
            system.Root.Send(remoteActor, msg);
        }
        wg.WaitOne(10_000);
        var elapsed = DateTime.Now - start;
        Console.WriteLine("Elapsed {0}", elapsed);

        var t = messageCount * 2.0 / elapsed.TotalMilliseconds * 1000;
        Console.WriteLine("Throughput {0} msg / sec", t);

        Console.ReadLine();
        await remote.ShutdownAsync();
    }

    public class LocalActor : IActor
    {
        private int _count;
        private readonly int _messageCount;
        private readonly AutoResetEvent _wg;

        public LocalActor(int count, int messageCount, AutoResetEvent wg)
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
                        _wg.Set();
                    }
                    break;
            }
            return Actor.Done;
        }
    }
}