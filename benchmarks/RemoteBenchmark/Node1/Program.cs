﻿// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron HB">
//      Copyright (C) 2015-2018 Asynkron HB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Messages;
using Proto;
using Proto.Remote;
using Microsoft.Extensions.Logging;
using ProtosReflection = Messages.ProtosReflection;

class Program
{
    static async Task Main(string[] args)
    {
        // Log.SetLoggerFactory(LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Information)));
        var system = new ActorSystem();
        var context = new RootContext(system);
        var serialization = new Serialization();
        serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
        var Remote = new Remote(system, serialization);
        Remote.Start("127.0.0.1", 12001);

        var messageCount = 1000000;
        
        _ = Task.Run(async () =>
        {
            
            
            while (true)
            {
                PID pid = null;
                try
                {
                    var wg = new AutoResetEvent(false);
                    var props = Props.FromProducer(() => new LocalActor(0, messageCount, wg));
                    pid = context.Spawn(props);
                    var remote = new PID("127.0.0.1:12000", "ponger");
                    await context.RequestAsync<Start>(remote, new StartRemote { Sender = pid }).ConfigureAwait(false);

                    var start = DateTime.Now;
                    Console.WriteLine("Starting to send");
                    var msg = new Ping();
                    for (var i = 0; i < messageCount; i++)
                    {
                        context.Send(remote, msg);
                    }
                    wg.WaitOne();
                    var elapsed = DateTime.Now - start;
                    Console.Clear();
                    Console.WriteLine("Elapsed {0}", elapsed);

                    var t = messageCount * 2.0 / elapsed.TotalMilliseconds * 1000;
                    Console.WriteLine("Throughput {0} msg / sec", t);
                }
                finally
                {
                    await Task.Delay(5000);
                    if(pid!= null)
                    context.Stop(pid);
                }
            }
        });
        Console.ReadLine();
        await Remote.Shutdown();

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
                        // Console.WriteLine(_count);
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