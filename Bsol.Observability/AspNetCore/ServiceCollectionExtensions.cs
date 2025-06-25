using System;
using Bsol.Observability.Utils;

#if NET8_0
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Bsol.Observability.Configuration;
using Bsol.Observability.Core;
using Microsoft.AspNetCore.Builder;
using System.Diagnostics;
using System.Linq;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Bsol.Observability.AspNetCore
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddBsolObservability(
            this IServiceCollection services,
            IConfiguration configuration,
            Action<ObservabilityOptions>? configureOptions = null)
        {
            var options = configuration.GetObservabilityOptions();

            ObservabilityLogging.EnableDebugLogging = options.EnableDebugLogging;

            ObservabilityLogging.Debug("🔍 [TRACING CONFIG] ConfigureStandardTracing method STARTED!");
            ObservabilityLogging.Debug($"🔍 [TRACING CONFIG] Service: {options.ServiceName}");
            ObservabilityLogging.Debug($"🔍 [TRACING CONFIG] Endpoint: {options.TempoUrl}");

            configureOptions?.Invoke(options);

            services.Configure<ObservabilityOptions>(opt =>
            {
                opt.ServiceName = options.ServiceName;
                opt.ServiceVersion = options.ServiceVersion;
                opt.Environment = options.Environment;
                opt.TempoUrl = options.TempoUrl;
                opt.SamplingRatio = options.SamplingRatio;
                opt.EnableSqlInstrumentation = options.EnableSqlInstrumentation;
                opt.EnableHttpClientInstrumentation = options.EnableHttpClientInstrumentation;
                opt.EnableConsoleExporter = options.EnableConsoleExporter;
                opt.EnableAutoLogging = options.EnableAutoLogging;
                opt.CustomTags = options.CustomTags;
                opt.ResourceAttributes = options.ResourceAttributes;
            });

            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    .ConfigureStandardTracing(options)
                    .AddAspNetCoreInstrumentation(aspnet =>
                    {
                        aspnet.Filter = context => TracingFilters.FilterAspNetCoreHttpCalls(context, options);
                        aspnet.RecordException = true;
                        aspnet.EnrichWithHttpRequest = EnrichWithHttpRequest;
                        aspnet.EnrichWithHttpResponse = EnrichWithHttpResponse;
                    }))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddMeter(Metrics.Meter.Name)
                    .AddOtlpExporter(otlp =>
                    {
                        var baseUri = new Uri(options.TempoUrl).GetLeftPart(UriPartial.Authority);
                        otlp.Endpoint = new Uri($"{baseUri}/v1/metrics");
                        otlp.TimeoutMilliseconds = options.TimeoutMilliseconds;
                        switch (options.Protocol)
                        {
                            case OtlpProtocol.Grpc:
                                var uri = new Uri(baseUri);
                                var builder = new UriBuilder() { Port = 4317 };
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                                otlp.Endpoint = new Uri($"{uri}/v1/metrics");
                                ObservabilityLogging.Debug("[METRICS CONFIG]   Using gRPC protocol");
                                break;

                            case OtlpProtocol.HttpProtobuf:
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                                ObservabilityLogging.Debug("[METRICS CONFIG]   Using HTTP/Protobuf protocol");
                                break;

                            default:
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                                ObservabilityLogging.Debug("[METRICS CONFIG]   Using default HTTP/Protobuf protocol");
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
                    }));


            if (options.EnableAutoLogging)
            {
                services.TryAddSingleton<ITraceContextAccessor, TraceContextAccessor>();
                services.TryDecorate(typeof(ILogger<>), typeof(TracingLoggerDecorator<>));
            }

            ObservabilityLogging.Debug("🔍 [DEBUG] OpenTelemetry configuration completed");
            return services;
        }

        public static IApplicationBuilder UseBsolObservability(this IApplicationBuilder app)
        {
            return app.UseMiddleware<AutoTracingMiddleware>();
        }

        private static void EnrichWithHttpRequest(Activity activity, Microsoft.AspNetCore.Http.HttpRequest httpRequest)
        {
            activity.SetTag("http.request.size", httpRequest.ContentLength);
            activity.SetTag("http.request.protocol", httpRequest.Protocol);
            activity.SetTag("http.scheme", httpRequest.Scheme);
            activity.SetTag("http.host", httpRequest.Host.Value);

            if (httpRequest.Headers.TryGetValue("X-Correlation-ID", out Microsoft.Extensions.Primitives.StringValues value))
            {
                var correlationId = value.FirstOrDefault();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    activity.SetTag("correlation.id", correlationId);
                }
            }
        }

        private static void EnrichWithHttpResponse(Activity activity, Microsoft.AspNetCore.Http.HttpResponse httpResponse)
        {
            activity.SetTag("http.response.size", httpResponse.ContentLength);
            activity.SetTag("http.response.content_type", httpResponse.ContentType);
        }
    }
}
#endif