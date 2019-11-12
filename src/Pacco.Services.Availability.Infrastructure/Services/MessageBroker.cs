using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Convey.CQRS.Events;
using Convey.MessageBrokers;
using Convey.MessageBrokers.Outbox;
using Convey.MessageBrokers.RabbitMQ;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenTracing;
using Pacco.Services.Availability.Application.Services;

namespace Pacco.Services.Availability.Infrastructure.Services
{
    internal sealed class MessageBroker : IMessageBroker
    {
        private readonly IMessageOutbox _outbox;
        private readonly ICorrelationContextAccessor _contextAccessor;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IMessagePropertiesAccessor _messagePropertiesAccessor;
        private readonly ITracer _tracer;
        private readonly ILogger<IMessageBroker> _logger;
        private readonly string _spanContextHeader;

        public MessageBroker(IMessageOutbox outbox, ICorrelationContextAccessor contextAccessor,
            IHttpContextAccessor httpContextAccessor, IMessagePropertiesAccessor messagePropertiesAccessor,
            RabbitMqOptions options, ITracer tracer, ILogger<IMessageBroker> logger)
        {
            _outbox = outbox;
            _contextAccessor = contextAccessor;
            _httpContextAccessor = httpContextAccessor;
            _messagePropertiesAccessor = messagePropertiesAccessor;
            _tracer = tracer;
            _logger = logger;
            _spanContextHeader = string.IsNullOrWhiteSpace(options.SpanContextHeader)
                ? "span_context"
                : options.SpanContextHeader;
        }

        public Task PublishAsync(params IEvent[] events)
            => PublishAsync(events.AsEnumerable());

        public async Task PublishAsync(IEnumerable<IEvent> events)
        {
            if (events is null)
            {
                return;
            }

            var correlationContext = _contextAccessor.CorrelationContext ??
                                     _httpContextAccessor.GetCorrelationContext();

            var messageProperties = _messagePropertiesAccessor.MessageProperties;
            var correlationId = _messagePropertiesAccessor.MessageProperties?.CorrelationId;
            var spanContext = string.Empty;

            if (!(messageProperties is null) && messageProperties.Headers.TryGetValue(_spanContextHeader, out var span)
                                             && span is byte[] spanBytes)
            {
                spanContext = Encoding.UTF8.GetString(spanBytes);
            }

            if (string.IsNullOrWhiteSpace(spanContext))
            {
                spanContext = _tracer.ActiveSpan is null ? string.Empty : _tracer.ActiveSpan.Context.ToString();
            }

            foreach (var @event in events)
            {
                if (@event is null)
                {
                    continue;
                }

                _logger.LogInformation($"Handling integration event: {@event.GetType().Name}");
                await _outbox.SendAsync(@event, correlationId: correlationId, spanContext: spanContext,
                    messageContext: correlationContext);
            }
        }
    }
}