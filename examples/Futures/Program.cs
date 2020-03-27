// -----------------------------------------------------------------------
//  <copyright file="Program.cs" company="Asynkron HB">
//      Copyright (C) 2015-2018 Asynkron HB All rights reserved
//  </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Proto;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var context = new RootContext(new ActorSystem());

        var props = Props.FromFunc(async ctx =>
        {
            Console.WriteLine(ctx.Message);
            switch (ctx.Message)
            {
                case string message:
                    await Task.Delay(100);
                    ctx.Send(ctx.Self, 1);
                    ctx.Respond("Hey !");
                    break;
                case int i when i > 10:
                    break;
                case int i:
                    await Task.Delay(100);
                    ctx.Send(ctx.Self, i + 1);
                    break;
            };
        });

        var pid = context.Spawn(props);
        Console.WriteLine("Hit enter");
        Console.ReadLine();

        var reply = await context.RequestAsync<object>(pid, "hello");
        await context.StopAsync(pid);
        Console.WriteLine(reply);
        Console.ReadLine();
    }
}