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
using Proto.Remote.Grpc;
using Microsoft.Extensions.Logging;
using ProtosReflection = Messages.ProtosReflection;
using Proto.Remote;

class Program
{
    static async Task Main(string[] args)
    {
        var system = new ActorSystem();
        var context = new RootContext(system);

        var remote = new SelfHostedRemoteServerOverGrpc(system, "127.0.0.1", 0, remote =>
        {
            remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
        });
        remote.Start();

        var messageCount = 1_000_000;

        var remoteActor = new PID("127.0.0.1:12000", "ponger");

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

                    await context.RequestAsync<Start>(remoteActor, new StartRemote { Sender = pid }).ConfigureAwait(false);

                    var start = DateTime.Now;
                    Console.WriteLine("Starting to send");
                    var msg = new Ping();
                    for (var i = 0; i < messageCount; i++)
                    {
                        context.Send(remoteActor, msg);
                    }
                    wg.WaitOne();
                    var elapsed = DateTime.Now - start;
                    Console.Clear();
                    Console.WriteLine("Elapsed {0}", elapsed);

                    var t = messageCount * 2.0 / elapsed.TotalMilliseconds * 1000;
                    Console.WriteLine("Throughput {0} msg / sec", t);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
                finally
                {
                    await Task.Delay(2000);
                    if (pid != null)
                        context.Stop(pid);
                }
            }
        });
        Console.ReadLine();
        await remote.Stop();
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