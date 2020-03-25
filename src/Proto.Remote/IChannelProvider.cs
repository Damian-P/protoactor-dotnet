// -----------------------------------------------------------------------
//   <copyright file="IChannelProvider.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System.Collections.Generic;
using Grpc.Core;

namespace Proto.Remote
{
    public interface IChannelProvider
    {
        ChannelBase GetChannel(string address, ChannelCredentials channelCredentials, IEnumerable<ChannelOption> channelOptions);
    }
}