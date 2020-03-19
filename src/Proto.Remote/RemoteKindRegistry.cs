// -----------------------------------------------------------------------
//   <copyright file="Activator.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Proto.Remote
{
    public class RemoteKindRegistry
    {
        private readonly Dictionary<string, Props> Kinds = new Dictionary<string, Props>();
        public string[] GetKnownKinds() => Kinds.Keys.ToArray();

        public void RegisterKnownKind(string kind, Props props) => Kinds.Add(kind, props);

        public void UnregisterKnownKind(string kind) => Kinds.Remove(kind);

        public Props GetKnownKind(string kind)
        {
            if (Kinds.TryGetValue(kind, out var props))
            {
                return props;
            }

            throw new ArgumentException($"No Props found for kind '{kind}'");
        }
    }
}