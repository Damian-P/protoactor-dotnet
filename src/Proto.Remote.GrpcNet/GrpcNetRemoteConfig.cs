// -----------------------------------------------------------------------
//   <copyright file="AspRemoteConfig.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Proto.Remote
{
    public class GrpcNetRemoteConfig
    {
        public bool UseHttps { get; set; }
        public GrpcChannelOptions ChannelOptions { get;  set; } = new GrpcChannelOptions();
        public Action<ListenOptions>? ConfigureKestrel { get; set; }
    }
}