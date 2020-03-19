// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron HB">
//      Copyright (C) 2015-2018 Asynkron HB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Messages;
using Proto;
using Proto.Remote.AspNetCore;
using ProtosReflection = Messages.ProtosReflection;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Starting App");
        var system = new SelfHostedRemoteActorSystem("http://localhost", 12001);
        system.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
        await system.StartAsync(default);
        var messageCount = 5000000;
        var wg = new AutoResetEvent(false);
        var props = Props.FromProducer(() => new LocalActor(0, messageCount, wg));

        var pid = system.Root.Spawn(props);
        var remote = new PID("http://localhost:12000", "remote");
        await system.Root.RequestAsync<Start>(remote, new StartRemote { Sender = pid });

        var start = DateTime.Now;
        Console.WriteLine("Starting to send");
        var msg = new Ping();
        for (var i = 0; i < messageCount; i++)
        {
            system.Root.Send(remote, msg);
        }
        wg.WaitOne();
        var elapsed = DateTime.Now - start;
        Console.WriteLine("Elapsed {0}", elapsed);

        var t = messageCount * 2.0 / elapsed.TotalMilliseconds * 1000;
        Console.WriteLine("Throughput {0} msg / sec", t);

        Console.ReadLine();
        await system.StopAsync(new CancellationTokenSource(5000).Token);
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