// -----------------------------------------------------------------------
//   <copyright file="ChannelProvider.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Grpc.Core;
using Grpc.Net.Client;

namespace Proto.Remote
{
    public class ChannelProvider : IChannelProvider
    {
        private readonly Action<GrpcChannelOptions>? configureChannelOptions;

        public ChannelProvider(Action<GrpcChannelOptions>? configure = null)
        {
            this.configureChannelOptions = configure;
        }
        public ChannelBase GetChannel(string address)
        {
            var addressWithProtocol =
                $"{(configureChannelOptions == null ? "http://" : "https://")}{address}";
            var grpcChannelOptions = new GrpcChannelOptions();
            configureChannelOptions?.Invoke(grpcChannelOptions);
            var channel = GrpcChannel.ForAddress(addressWithProtocol, grpcChannelOptions);
            return channel;
        }
    }
}