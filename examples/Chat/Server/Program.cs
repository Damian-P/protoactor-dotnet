using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using chat.messages;
using Jaeger;
using Jaeger.Samplers;
using OpenTracing.Util;
using Proto;
using Proto.OpenTracing;
using Proto.Remote;

class Program
{
    static async Task Main(string[] args)
    {
        var tracer = new Tracer.Builder("Proto.Chat.Server")
            .WithSampler(new ConstSampler(true))
            .Build();
        GlobalTracer.Register(tracer);

        SpanSetup spanSetup = (span, message) => span.Log(message?.ToString());

        var system = new ActorSystem();
        var context = new RootContext(system);

        var remote = new SelfHostedRemoteServerOverGrpc(system, "127.0.0.1", 8000,
            remote => { remote.Serialization.RegisterFileDescriptor(ChatReflection.Descriptor); }
        );
        remote.Start();

        var clients = new HashSet<PID>();
        var props = Props.FromFunc(ctx =>
                {
                    switch (ctx.Message)
                    {
                        case Connect connect:
                            Console.WriteLine($"Client {connect.Sender} connected");
                            clients.Add(connect.Sender);
                            ctx.Send(connect.Sender, new Connected {Message = "Welcome!"});
                            break;
                        case SayRequest sayRequest:
                            foreach (var client in clients)
                            {
                                ctx.Send(client, new SayResponse
                                    {
                                        UserName = sayRequest.UserName,
                                        Message = sayRequest.Message
                                    }
                                );
                            }

                            break;
                        case NickRequest nickRequest:
                            foreach (var client in clients)
                            {
                                ctx.Send(client, new NickResponse
                                    {
                                        OldUserName = nickRequest.OldUserName,
                                        NewUserName = nickRequest.NewUserName
                                    }
                                );
                            }

                            break;
                    }

                    return Actor.Done;
                }
            )
            .WithOpenTracing(spanSetup, spanSetup);

        context.SpawnNamed(props, "chatserver");
        Console.ReadLine();
        await remote.Stop();
    }
}