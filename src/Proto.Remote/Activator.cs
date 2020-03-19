// -----------------------------------------------------------------------
//   <copyright file="Activator.cs" company="Asynkron HB">
//       Copyright (C) 2015-2018 Asynkron HB All rights reserved
//   </copyright>
// -----------------------------------------------------------------------

using System;
using System.Threading.Tasks;

namespace Proto.Remote
{
    public class Activator : IActor
    {
        private readonly IRemoteActorSystem _remoteActorSystem;

        public Activator(IRemoteActorSystem remoteActorSystem)
        {
            _remoteActorSystem = remoteActorSystem;
        }

        public Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case ActorPidRequest msg:
                    var props = _remoteActorSystem.RemoteKindRegistry.GetKnownKind(msg.Kind);
                    var name = msg.Name;
                    if (string.IsNullOrEmpty(name))
                    {
                        name = _remoteActorSystem.ProcessRegistry.NextId();
                    }

                    try
                    {
                        var pid = _remoteActorSystem.Root.SpawnNamed(props, name);
                        var response = new ActorPidResponse {Pid = pid};
                        context.Respond(response);
                    }
                    catch (ProcessNameExistException ex)
                    {
                        var response = new ActorPidResponse
                        {
                            Pid = ex.Pid,
                            StatusCode = (int) ResponseStatusCode.ProcessNameAlreadyExist
                        };
                        context.Respond(response);
                    }
                    catch (ActivatorException ex)
                    {
                        var response = new ActorPidResponse
                        {
                            StatusCode = ex.Code
                        };
                        context.Respond(response);

                        if (!ex.DoNotThrow)
                            throw;
                    }
                    catch
                    {
                        var response = new ActorPidResponse
                        {
                            StatusCode = (int) ResponseStatusCode.Error
                        };
                        context.Respond(response);

                        throw;
                    }

                    break;
            }

            return Actor.Done;
        }
    }

    public class ActivatorUnavailableException : ActivatorException
    {
        public ActivatorUnavailableException() : base((int) ResponseStatusCode.Unavailable, true)
        {
        }
    }

    public class ActivatorException : Exception
    {
        public int Code { get; }
        public bool DoNotThrow { get; }

        public ActivatorException(int code, bool doNotThrow = false)
        {
            Code = code;
            DoNotThrow = doNotThrow;
        }
    }
}