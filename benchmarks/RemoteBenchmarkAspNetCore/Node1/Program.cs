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
using Microsoft.Extensions.Logging;
using ProtosReflection = Messages.ProtosReflection;
using Proto.Remote;
using System.Collections.Concurrent;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using System.Collections.Generic;


public class MyJsonSerializer : Proto.Remote.ISerializer
{
    public MyJsonSerializer()
    {
        RegisterFileDescriptor(Proto.ProtosReflection.Descriptor);
        RegisterFileDescriptor(Proto.Remote.ProtosReflection.Descriptor);
    }
    private readonly Dictionary<string, MessageParser> TypeLookup = new Dictionary<string, MessageParser>();
    private readonly Dictionary<string, Func<ByteString, object>> types = new Dictionary<string, Func<ByteString, object>>();
    public ByteString Serialize(object obj)
    {
        return obj switch
        {
            IMessage message when TypeLookup.ContainsKey(message.Descriptor.FullName) => message.ToByteString(),
            object o => ByteString.CopyFromUtf8(System.Text.Json.JsonSerializer.Serialize(obj)),
            _ => throw new Exception("Type unknown")
        };
    }

    public object Deserialize(ByteString bytes, string typeName)
    {
        if (TypeLookup.TryGetValue(typeName, out var parser))
        {
            return parser.ParseFrom(bytes);
        }
        if (types.TryGetValue(typeName, out var deserialize))
        {
            return deserialize(bytes);
        }
        Console.WriteLine($"Json not registered: {typeName}");
        return System.Text.Json.JsonSerializer.Deserialize(bytes.ToStringUtf8(), Type.GetType(typeName));
    }

    public void RegisterFileDescriptor(FileDescriptor fd)
    {
        foreach (var msg in fd.MessageTypes)
        {
            TypeLookup.Add(msg.FullName, msg.Parser);
        }
    }

    public void RegisterTypeDeserializer<T>()
    {
        types.TryAdd(typeof(T).FullName, bytes => System.Text.Json.JsonSerializer.Deserialize<T>(bytes.ToStringUtf8()));
    }

    public string GetTypeName(object obj)
    {
        switch (obj)
        {
            case IMessage message when TypeLookup.ContainsKey(message.Descriptor.FullName):
                return message.Descriptor.FullName;
            default:
                return obj.GetType().FullName;
        }
    }
}

class Program
{
    static PID remoteActor = new PID("127.0.0.1:12000", "ponger");
    static async Task Main(string[] args)
    {
        Log.SetLoggerFactory(LoggerFactory.Create(b => b.AddConsole()
                                                            .AddFilter("Proto.EventStream", LogLevel.Critical)
                                                            .AddFilter("Microsoft", LogLevel.Critical)
                                                            .AddFilter("Grpc.AspNetCore", LogLevel.Critical)
                                                            .SetMinimumLevel(LogLevel.Information)));
        var system = new ActorSystem();
        var context = new RootContext(system);

        var remote = new SelfHostedRemoteServerOverAspNet(system, "127.0.0.1", 12001, remote =>
        {
            remote.Serialization.RegisterFileDescriptor(ProtosReflection.Descriptor);
            remote.RemoteConfig.EndpointWriterOptions.EndpointWriterBatchSize = 10000;
            var s = new MyJsonSerializer();
            s.RegisterTypeDeserializer<Messages.Ping>();
            s.RegisterTypeDeserializer<Messages.Pong>();
            s.RegisterTypeDeserializer<Messages.Start>();
            s.RegisterTypeDeserializer<Messages.StartRemote>();
            s.RegisterFileDescriptor(ProtosReflection.Descriptor);
            remote.Serialization.RegisterSerializer(s, true);
        });
        remote.Start();

        var messageCount = 1_000_000;

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