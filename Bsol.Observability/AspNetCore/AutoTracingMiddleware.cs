#if NET8_0
using Bsol.Observability.Core;
using Bsol.Observability.Utils;
using Microsoft.AspNetCore.Http;
using OpenTelemetry.Trace;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Bsol.Observability.AspNetCore
{
    public class AutoTracingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ActivitySource _activitySource;

        public AutoTracingMiddleware(RequestDelegate next)
        {
            _next = next;
            _activitySource = ActivitySourceProvider.DefaultActivitySource;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var incomingTraceId = context.Request.Headers["X-Trace-Id"].FirstOrDefault();
            var incomingSpanId = context.Request.Headers["X-Span-Id"].FirstOrDefault();
            var incomingCorrelationId = context.Request.Headers["X-Correlation-ID"].FirstOrDefault();

            ObservabilityLogging.Debug($"🔍 AutoTracingMiddleware: Processing {context.Request.Method} {context.Request.Path}");
            ObservabilityLogging.Debug($"🔍 ActivitySource: {_activitySource.Name} v{_activitySource.Version}");

            using var activity = _activitySource.StartActivity($"{context.Request.Method} {context.Request.Path}");

            if (activity != null)
            {
                ObservabilityLogging.Debug($"🔍 Activity created: {activity.TraceId} - {activity.SpanId}");
            }
            else
            {
                ObservabilityLogging.Debug($"🔴 Activity NOT created - possible ActivitySource issue");
            }

            if (!string.IsNullOrEmpty(incomingTraceId) && activity != null)
            {
                activity.SetTag("parent.trace_id", incomingTraceId);
                activity.SetTag("parent.span_id", incomingSpanId);
            }

            activity?.SetTag("http.method", context.Request.Method);
            activity?.SetTag("http.url", context.Request.Path.Value);
            activity?.SetTag("http.user_agent", context.Request.Headers["User-Agent"].FirstOrDefault());
            activity?.SetTag("http.request_id", context.TraceIdentifier);

            var correlationId = CorrelationIdGenerator.GetOrGenerate(context);
            context.Response.Headers.Append("X-Correlation-ID", correlationId);
            activity?.SetTag("correlation.id", correlationId);

            var stopwatch = Stopwatch.StartNew();
            Exception? caughtException = null;

            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                caughtException = ex;

                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.RecordException(ex);
                activity?.SetTag("error", true);
                activity?.SetTag("error.type", ex.GetType().Name);

                throw;
            }
            finally
            {
                stopwatch.Stop();

                activity?.SetTag("http.response.status_code", context.Response.StatusCode);
                activity?.SetTag("http.request.duration_ms", stopwatch.ElapsedMilliseconds);

                var isError = context.Response.StatusCode >= 400 || caughtException != null;
                if (isError && caughtException == null)
                {
                    activity?.SetTag("error", true);
                    activity?.SetTag("error.type", "http_error");
                }

                var tags = new TagList
                {
                    { "method", context.Request.Method },
                    { "status_code", context.Response.StatusCode.ToString() },
                    { "endpoint", context.Request.Path.Value ?? "" },
                    { "error", isError.ToString().ToLower() }
                };

                Metrics.HttpRequestsTotal.Add(1, tags);
                Metrics.HttpRequestDuration.Record(stopwatch.Elapsed.TotalSeconds, tags);
            }
        }
    }
}
#endif