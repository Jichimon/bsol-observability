using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Bsol.Observability.Core
{
    internal class TracingLoggerDecorator<T> : ILogger<T>
    {
        private readonly ILogger<T> _innerLogger;
        private readonly ITraceContextAccessor _traceContextAccessor;

        public TracingLoggerDecorator(ILogger<T> innerLogger, ITraceContextAccessor traceContextAccessor)
        {
            _innerLogger = innerLogger;
            _traceContextAccessor = traceContextAccessor;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var traceContext = _traceContextAccessor.GetCurrentTraceContext();

            if (traceContext != null)
            {
                using var scope = _innerLogger.BeginScope(new Dictionary<string, object>
                {
                    ["TraceId"] = traceContext.TraceId,
                    ["SpanId"] = traceContext.SpanId,
                    ["CorrelationId"] = traceContext.CorrelationId,
                    ["ServiceName"] = traceContext.ServiceName,
                    ["OperationName"] = traceContext.OperationName
                });

                var activity = Activity.Current;
                if (activity != null)
                {
                    var logMessage = formatter(state, exception);
                    activity.AddEvent(new ActivityEvent($"Log.{logLevel}", DateTimeOffset.UtcNow, new ActivityTagsCollection
                    {
                        ["log.level"] = logLevel.ToString(),
                        ["log.message"] = logMessage,
                        ["log.exception"] = exception?.ToString()
                    }));

                    if (logLevel >= LogLevel.Error && exception != null)
                    {
                        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
                        activity.RecordException(exception);
                    }
                }

                _innerLogger.Log(logLevel, eventId, state, exception, formatter);
            }
            else
            {
                _innerLogger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        public bool IsEnabled(LogLevel logLevel) => _innerLogger.IsEnabled(logLevel);

        public IDisposable BeginScope<TState>(TState state) => _innerLogger.BeginScope(state);
    }
}
