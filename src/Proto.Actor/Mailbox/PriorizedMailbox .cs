using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Proto.Mailbox
{
    public interface IPriorityMessage
    {
        ushort Priority { get; }
    }
    public class PrioritizedUnboundedMailboxQueue : IMailboxQueue
    {
        private readonly ConcurrentQueue<object>[] _messages;
        private readonly ushort _maxPriority;
        public PrioritizedUnboundedMailboxQueue(ushort maxPriority)
        {
            _maxPriority = maxPriority;
            _messages = new ConcurrentQueue<object>[maxPriority + 1];
            for (int i = 0; i <= maxPriority; i++)
            {
                _messages[i] = new ConcurrentQueue<object>();
            }
        }
        public void Push(object message)
        {
            ushort priority = 0;
            switch (message)
            {
                case IPriorityMessage p when p.Priority > _maxPriority:
                    priority = _maxPriority;
                    break;
                case IPriorityMessage p when p.Priority > 0:
                    priority = p.Priority;
                    break;
                default:
                    priority = 0;
                    break;
            }
            _messages[priority].Enqueue(message);
        }

        public object? Pop()
        {
            for (int i = _messages.Length - 1; i >= 0; i--)
            {
                if (_messages[i].TryDequeue(out var message))
                    return message;
            }
            return null;
        }

        public bool HasMessages => _messages.Any(x => !x.IsEmpty);
    }
    public class PrioritizedByFuncUnboundedMailboxQueue : IMailboxQueue
    {
        private readonly ConcurrentQueue<object>[] _messages;
        private readonly ushort _maxPriority;
        private readonly Func<object, ushort> _priority;
        public PrioritizedByFuncUnboundedMailboxQueue(ushort maxPriority, Func<object, ushort> priority)
        {
            _maxPriority = maxPriority;
            _priority = priority;
            _messages = new ConcurrentQueue<object>[maxPriority + 1];
            for (int i = 0; i <= maxPriority; i++)
            {
                _messages[i] = new ConcurrentQueue<object>();
            }
        }
        public void Push(object message)
        {
            var priority = _priority(message);
            if (priority > _maxPriority)
                priority = _maxPriority;
            else if (priority < 0)
                priority = 0;
            _messages[priority].Enqueue(message);
        }

        public object? Pop()
        {
            for (int i = _messages.Length - 1; i >= 0; i--)
            {
                if (_messages[i].TryDequeue(out var message))
                    return message;
            }
            return null;
        }

        public bool HasMessages => _messages.Any(x => !x.IsEmpty);
    }
    public static class PrioritizedUnboundedMailbox
    {
        public static IMailbox Create(ushort maxPriority, Func<object, ushort> priorityDecider, params IMailboxStatistics[] stats) =>
            new DefaultMailbox(new UnboundedMailboxQueue(), new PrioritizedByFuncUnboundedMailboxQueue(maxPriority, priorityDecider), stats);

        public static IMailbox Create(ushort maxPriority, params IMailboxStatistics[] stats) =>
            new DefaultMailbox(new UnboundedMailboxQueue(), new PrioritizedUnboundedMailboxQueue(maxPriority), stats);
    }
}