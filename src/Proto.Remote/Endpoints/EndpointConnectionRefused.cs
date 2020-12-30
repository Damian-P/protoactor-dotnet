// -----------------------------------------------------------------------
//   <copyright file="EndpointActor.cs" company="Asynkron AB">
//       Copyright (C) 2015-2020 Asynkron AB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Proto.Remote
{
    [Serializable]
    internal class EndpointConnectionRefused : Exception
    {
        public EndpointConnectionRefused()
        {
        }

        public EndpointConnectionRefused(string message) : base(message)
        {
        }

        public EndpointConnectionRefused(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected EndpointConnectionRefused(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}