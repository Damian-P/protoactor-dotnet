using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Proto.Mailbox
{
    public class PrioritizedUnboundedMailboxQueue : IMailboxQueue
    {
        private readonly ConcurrentQueue<object>[] _messages;
        private readonly int _maxPriority;
        private readonly Func<object, int> _priority;
        public PrioritizedUnboundedMailboxQueue(int maxPriority, Func<object, int> priority)
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
        public static IMailbox Create(int maxPriority, Func<object, int> priorityDecider, params IMailboxStatistics[] stats) =>
            new DefaultMailbox(new UnboundedMailboxQueue(), new PrioritizedUnboundedMailboxQueue(maxPriority, priorityDecider), stats);
    }
}