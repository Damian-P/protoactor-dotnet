// -----------------------------------------------------------------------
//   <copyright file="ChannelProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Grpc.Core;
using Grpc.Net.Client;

namespace Proto.Remote.AspNetCore
{
    internal class ChannelProvider : IChannelProvider
    {

        public ChannelBase GetChannel(string address, ChannelCredentials channelCredentials, IEnumerable<ChannelOption> channelOptions)
        {
            var addressWithProtocol =
                $"{(channelCredentials == ChannelCredentials.Insecure ? "http://" : "https://")}{address}";

            var options = new GrpcChannelOptions
            {
                Credentials = channelCredentials
            };
            var channel = GrpcChannel.ForAddress(addressWithProtocol, options);

            return channel;
        }
    }
}