using Bsol.Observability.Configuration;
using Bsol.Observability.Utils;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Bsol.Observability.Core
{
    public static class TracingConfiguration
    {
        public static TracerProviderBuilder ConfigureStandardTracing(
            this TracerProviderBuilder builder,
            ObservabilityOptions options)
        {

            ObservabilityLogging.Debug("🔍 [TRACING CONFIG] ConfigureStandardTracing method STARTED!");
            ObservabilityLogging.Debug($"🔍 [TRACING CONFIG] Service: {options.ServiceName}");
            ObservabilityLogging.Debug($"🔍 [TRACING CONFIG] Endpoint: {options.TempoUrl}");

            var resourceBuilder = ResourceBuilder.CreateDefault()
                .AddService(options.ServiceName, options.ServiceVersion)
                .AddAttributes(new[]
                {
                    new KeyValuePair<string, object>("environment", options.Environment),
                    new KeyValuePair<string, object>("service.instance.id", Environment.MachineName)
                });

            if (options.ResourceAttributes.Any())
            {
                resourceBuilder.AddAttributes(
                    options.ResourceAttributes.Select(kv =>
                        new KeyValuePair<string, object>(kv.Key, kv.Value)));
            }

            builder.SetResourceBuilder(resourceBuilder);
            builder.SetSampler(new TraceIdRatioBasedSampler(options.SamplingRatio));
            builder.AddSource(options.ServiceName);
            builder.AddSource(ActivitySourceProvider.DefaultActivitySource.Name);
            builder.AddProcessor(new ServiceNameEnricherProcessor(options.ServiceName));

            if (options.EnableHttpClientInstrumentation)
            {
                builder.AddHttpClientInstrumentation(http =>
                {
                    http.FilterHttpWebRequest = httpWebRequest => TracingFilters.FilterHttpWebRequest(httpWebRequest, options);
                    http.FilterHttpRequestMessage = httpRequestMessage => TracingFilters.FilterHttpRequestMessage(httpRequestMessage, options);
                    http.EnrichWithHttpRequestMessage = (activity, request) =>
                    {
                        var correlationId = Activity.Current?.GetTagItem("correlation.id")?.ToString();
                        if (!string.IsNullOrEmpty(correlationId) && !request.Headers.Contains("X-Correlation-ID"))
                        {
                            request.Headers.Add("X-Correlation-ID", correlationId);
                        }

                        activity.SetTag("http.request.size", request.Content?.Headers?.ContentLength);
                    };

                    http.EnrichWithHttpResponseMessage = (activity, response) =>
                    {
                        activity.SetTag("http.response.size", response.Content?.Headers?.ContentLength);
                        activity.SetTag("http.response.content_type", response.Content?.Headers?.ContentType?.ToString());
                    };
                });
            }

            if (options.EnableSqlInstrumentation)
            {
                builder.AddSqlClientInstrumentation(sql =>
                {
                    sql.SetDbStatementForText = true;
                    sql.SetDbStatementForStoredProcedure = true;
                    sql.RecordException = true;
                    sql.EnableConnectionLevelAttributes = true;
                });
            }

            try
            {
                var endpoint = new Uri(options.TempoUrl);
                ObservabilityLogging.Debug($"🔍[TRACING CONFIG] Configuring OTLP Exporter:");
                ObservabilityLogging.Debug($"[TRACING CONFIG]   Endpoint: {endpoint}");
                ObservabilityLogging.Debug($"[TRACING CONFIG]   Scheme: {endpoint.Scheme}");
                ObservabilityLogging.Debug($"[TRACING CONFIG]   Host: {endpoint.Host}");
                ObservabilityLogging.Debug($"[TRACING CONFIG]   Port: {endpoint.Port}");

                builder.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri($"{options.TempoUrl}/v1/traces");
                    otlp.TimeoutMilliseconds = options.TimeoutMilliseconds;
                    switch (options.Protocol)
                    {
                        case OtlpProtocol.Grpc:
                            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                            ObservabilityLogging.Debug("[TRACING CONFIG]   Using gRPC protocol");
                            break;

                        case OtlpProtocol.HttpProtobuf:
                            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                            ObservabilityLogging.Debug("[TRACING CONFIG]   Using HTTP/Protobuf protocol");
                            break;

                        default:
                            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                            ObservabilityLogging.Debug("[TRACING CONFIG]   Using default HTTP/Protobuf protocol");
                            break;
                    }

                    if (!string.IsNullOrEmpty(options.Headers))
                    {
                        otlp.Headers = options.Headers;
                    }

                    otlp.BatchExportProcessorOptions = new()
                    {
                        MaxQueueSize = 2048,
                        ScheduledDelayMilliseconds = options.BatchDelayMilliseconds,
                        ExporterTimeoutMilliseconds = options.TimeoutMilliseconds,
                        MaxExportBatchSize = 10
                    };

                    ObservabilityLogging.Debug($"🔍[TRACING CONFIG] OTLP Batch Options:");
                    ObservabilityLogging.Debug($"[TRACING CONFIG]   Max Queue Size: {otlp.BatchExportProcessorOptions.MaxQueueSize}");
                    ObservabilityLogging.Debug($"[TRACING CONFIG]   Scheduled Delay: {otlp.BatchExportProcessorOptions.ScheduledDelayMilliseconds}ms");
                    ObservabilityLogging.Debug($"[TRACING CONFIG]   Export Timeout: {otlp.BatchExportProcessorOptions.ExporterTimeoutMilliseconds}ms");
                    ObservabilityLogging.Debug($"[TRACING CONFIG]   Max Batch Size: {otlp.BatchExportProcessorOptions.MaxExportBatchSize}");
                });

                ObservabilityLogging.Debug("[TRACING CONFIG] ✅ OTLP Exporter configured successfully");
            }
            catch (Exception ex)
            {
                ObservabilityLogging.Debug($"🔴[TRACING CONFIG]  OTLP Exporter configuration failed: {ex.Message}");
                ObservabilityLogging.Debug($"🔴[TRACING CONFIG]  Exception details: {ex}");
            }

            var excludePatterns = TracingFilters.GetAllExcludePatterns(options);

            if (options.EnableConsoleExporter)
            {
                builder.AddProcessor(new DetailedDebuggingActivityProcessor(excludePatterns));
                ObservabilityLogging.Debug("[TRACING CONFIG] ✅ Console Exporter enabled for comparison");
            }

            builder.AddProcessor(new ExportInterceptorProcessor(excludePatterns));

            return builder;
        }
    }

    internal class ServiceNameEnricherProcessor : BaseProcessor<Activity>
    {
        private readonly string _serviceName;

        public ServiceNameEnricherProcessor(string serviceName)
        {
            _serviceName = serviceName;
        }

        public override void OnStart(Activity activity)
        {
            activity.SetTag("service.name", _serviceName);
            base.OnStart(activity);
        }
    }

    internal class CallbackActivityProcessor : BaseProcessor<Activity>
    {
        private readonly Action<Activity> _callback;

        public CallbackActivityProcessor(Action<Activity> callback)
        {
            _callback = callback;
        }

        public override void OnEnd(Activity activity)
        {
            _callback(activity);
        }
    }


    internal class ExportInterceptorProcessor : BaseProcessor<Activity>
    {
        private static int _processedCount = 0;
        private readonly List<string> _excludePatterns;

        public ExportInterceptorProcessor(List<string> excludePatterns)
        {
            _excludePatterns = excludePatterns;
        }

        public override void OnEnd(Activity activity)
        {
            Interlocked.Increment(ref _processedCount);

            var activityUrl = activity.GetTagItem("url.full")?.ToString() ?? "N/A";
            if (_excludePatterns.Any(pattern => activityUrl.Contains(pattern)))
            {
                ObservabilityLogging.Debug($"🚫 [DETAILED DEBUG] Excluding activity: {activityUrl}");
                return;
            }

            ObservabilityLogging.Debug($"🔍 [PROCESSOR] Activity ending (#{_processedCount}):");
            ObservabilityLogging.Debug($"   TraceId: {activity.TraceId}");
            ObservabilityLogging.Debug($"   SpanId: {activity.SpanId}");
            ObservabilityLogging.Debug($"   Name: {activity.DisplayName}");
            ObservabilityLogging.Debug($"   Duration: {activity.Duration.TotalMilliseconds}ms");
            ObservabilityLogging.Debug($"   Status: {activity.Status}");
            ObservabilityLogging.Debug($"   Tags: {activity.Tags.Count()}");

            foreach (var tag in activity.Tags.Take(3)) // Limita para no saturar
            {
                ObservabilityLogging.Debug($"     {tag.Key}: {tag.Value}");
            }

            ObservabilityLogging.Debug($"   ✅ Activity will be sent to exporters");
        }
    }

    internal class DetailedDebuggingActivityProcessor : BaseProcessor<Activity>
    {
        private readonly List<string> _excludePatterns;

        public DetailedDebuggingActivityProcessor(List<string> excludePatterns)
        {
            _excludePatterns = excludePatterns;
        }

        public override void OnEnd(Activity activity)
        {
            var activityUrl = activity.GetTagItem("url.full")?.ToString() ?? "N/A";
            if (_excludePatterns.Any(pattern => activityUrl.Contains(pattern)))
            {
                ObservabilityLogging.Debug($"🚫 [DETAILED DEBUG] Excluding activity: {activityUrl}");
                return;
            }

            var correlationId = activity.GetTagItem("correlation.id")?.ToString() ?? "N/A";
            var serviceName = activity.GetTagItem("service.name")?.ToString() ?? "Unknown";

            ObservabilityLogging.Debug($"🔍 [DETAILED DEBUG] Activity Details:");
            ObservabilityLogging.Debug($"   Service: {serviceName}");
            ObservabilityLogging.Debug($"   TraceId: {activity.TraceId}");
            ObservabilityLogging.Debug($"   SpanId: {activity.SpanId}");
            ObservabilityLogging.Debug($"   ParentSpanId: {activity.ParentSpanId}");
            ObservabilityLogging.Debug($"   CorrelationId: {correlationId}");
            ObservabilityLogging.Debug($"   Name: {activity.DisplayName}");
            ObservabilityLogging.Debug($"   Kind: {activity.Kind}");
            ObservabilityLogging.Debug($"   Status: {activity.Status}");
            ObservabilityLogging.Debug($"   Start: {activity.StartTimeUtc:yyyy-MM-dd HH:mm:ss.fff}");
            ObservabilityLogging.Debug($"   Duration: {activity.Duration.TotalMilliseconds}ms");
            ObservabilityLogging.Debug($"   Source: {activity.Source.Name} v{activity.Source.Version}");

            if (activity.Tags.Any())
            {
                ObservabilityLogging.Debug($"   Tags ({activity.Tags.Count()}):");
                foreach (var tag in activity.Tags.Take(5)) // Limita para no saturar
                {
                    ObservabilityLogging.Debug($"     {tag.Key}: {tag.Value}");
                }
            }

            if (activity.Events.Any())
            {
                ObservabilityLogging.Debug($"   Events ({activity.Events.Count()}):");
                foreach (var evt in activity.Events.Take(3))
                {
                    ObservabilityLogging.Debug($"     {evt.Name} at {evt.Timestamp}");
                }
            }
        }
    }

}
