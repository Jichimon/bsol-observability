using Bsol.Observability.Configuration;
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

            Console.WriteLine("🔍 [TRACING CONFIG] ConfigureStandardTracing method STARTED!");
            Console.WriteLine($"🔍 [TRACING CONFIG] Service: {options.ServiceName}");
            Console.WriteLine($"🔍 [TRACING CONFIG] Endpoint: {options.TempoEndpoint}");

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
                    http.FilterHttpWebRequest = FilterHttpWebRequest;
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
                var endpoint = new Uri(options.TempoEndpoint);
                Console.WriteLine($"🔍[TRACING CONFIG] Configuring OTLP Exporter:");
                Console.WriteLine($"[TRACING CONFIG]   Endpoint: {endpoint}");
                Console.WriteLine($"[TRACING CONFIG]   Scheme: {endpoint.Scheme}");
                Console.WriteLine($"[TRACING CONFIG]   Host: {endpoint.Host}");
                Console.WriteLine($"[TRACING CONFIG]   Port: {endpoint.Port}");

                builder.AddOtlpExporter(otlp =>
                {
                    otlp.Endpoint = new Uri(options.TempoEndpoint);
                    otlp.TimeoutMilliseconds = options.TimeoutMilliseconds;
                    switch (options.Protocol)
                    {
                        case OtlpProtocol.Grpc:
                            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                            Console.WriteLine("[TRACING CONFIG]   Using gRPC protocol");
                            break;

                        case OtlpProtocol.HttpProtobuf:
                            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                            Console.WriteLine("[TRACING CONFIG]   Using HTTP/Protobuf protocol");
                            break;

                        default:
                            otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                            Console.WriteLine("[TRACING CONFIG]   Using default HTTP/Protobuf protocol");
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

                    Console.WriteLine($"🔍[TRACING CONFIG] OTLP Batch Options:");
                    Console.WriteLine($"[TRACING CONFIG]   Max Queue Size: {otlp.BatchExportProcessorOptions.MaxQueueSize}");
                    Console.WriteLine($"[TRACING CONFIG]   Scheduled Delay: {otlp.BatchExportProcessorOptions.ScheduledDelayMilliseconds}ms");
                    Console.WriteLine($"[TRACING CONFIG]   Export Timeout: {otlp.BatchExportProcessorOptions.ExporterTimeoutMilliseconds}ms");
                    Console.WriteLine($"[TRACING CONFIG]   Max Batch Size: {otlp.BatchExportProcessorOptions.MaxExportBatchSize}");
                });

                Console.WriteLine("[TRACING CONFIG] ✅ OTLP Exporter configured successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔴[TRACING CONFIG]  OTLP Exporter configuration failed: {ex.Message}");
                Console.WriteLine($"🔴[TRACING CONFIG]  Exception details: {ex}");
            }

            if (options.EnableConsoleExporter)
            {
                builder.AddProcessor(new DetailedDebuggingActivityProcessor());
                Console.WriteLine("[TRACING CONFIG] ✅ Console Exporter enabled for comparison");
            }

            builder.AddProcessor(new ExportInterceptorProcessor());

            return builder;
        }

        private static bool FilterHttpWebRequest(System.Net.HttpWebRequest request)
        {
            var uri = request.RequestUri?.ToString() ?? "";

            // Excluir telemetría y monitoreo
            var excludedHosts = new[]
            {
                    "applicationinsights",
                    "4318", // Tempo
                    "4317", // Tempo gRPC
                    "metadata.google.internal", // GCP metadata
                    "169.254.169.254" // AWS metadata
                };

            if (excludedHosts.Any(host => uri.Contains(host)))
            {
                return false;
            }

            return true;
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

        public override void OnEnd(Activity activity)
        {
            Interlocked.Increment(ref _processedCount);

            Console.WriteLine($"🔍 [PROCESSOR] Activity ending (#{_processedCount}):");
            Console.WriteLine($"   TraceId: {activity.TraceId}");
            Console.WriteLine($"   SpanId: {activity.SpanId}");
            Console.WriteLine($"   Name: {activity.DisplayName}");
            Console.WriteLine($"   Duration: {activity.Duration.TotalMilliseconds}ms");
            Console.WriteLine($"   Status: {activity.Status}");
            Console.WriteLine($"   Tags: {activity.Tags.Count()}");

            foreach (var tag in activity.Tags.Take(3)) // Limita para no saturar
            {
                Console.WriteLine($"     {tag.Key}: {tag.Value}");
            }

            Console.WriteLine($"   ✅ Activity will be sent to exporters");
        }
    }

    internal class DetailedDebuggingActivityProcessor : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity activity)
        {
            var correlationId = activity.GetTagItem("correlation.id")?.ToString() ?? "N/A";
            var serviceName = activity.GetTagItem("service.name")?.ToString() ?? "Unknown";

            Console.WriteLine($"🔍 [DETAILED DEBUG] Activity Details:");
            Console.WriteLine($"   Service: {serviceName}");
            Console.WriteLine($"   TraceId: {activity.TraceId}");
            Console.WriteLine($"   SpanId: {activity.SpanId}");
            Console.WriteLine($"   ParentSpanId: {activity.ParentSpanId}");
            Console.WriteLine($"   CorrelationId: {correlationId}");
            Console.WriteLine($"   Name: {activity.DisplayName}");
            Console.WriteLine($"   Kind: {activity.Kind}");
            Console.WriteLine($"   Status: {activity.Status}");
            Console.WriteLine($"   Start: {activity.StartTimeUtc:yyyy-MM-dd HH:mm:ss.fff}");
            Console.WriteLine($"   Duration: {activity.Duration.TotalMilliseconds}ms");
            Console.WriteLine($"   Source: {activity.Source.Name} v{activity.Source.Version}");

            if (activity.Tags.Any())
            {
                Console.WriteLine($"   Tags ({activity.Tags.Count()}):");
                foreach (var tag in activity.Tags.Take(5)) // Limita para no saturar
                {
                    Console.WriteLine($"     {tag.Key}: {tag.Value}");
                }
            }

            if (activity.Events.Any())
            {
                Console.WriteLine($"   Events ({activity.Events.Count()}):");
                foreach (var evt in activity.Events.Take(3))
                {
                    Console.WriteLine($"     {evt.Name} at {evt.Timestamp}");
                }
            }
        }
    }

}
