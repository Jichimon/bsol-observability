using System;
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
            Console.WriteLine("🔍 [DEBUG] AddBsolObservability method starting...");

            var options = configuration.GetObservabilityOptions();
            Console.WriteLine($"🔍 [DEBUG] Options loaded: ServiceName='{options.ServiceName}', Endpoint='{options.TempoEndpoint}'");

            configureOptions?.Invoke(options);

            services.Configure<ObservabilityOptions>(opt =>
            {
                opt.ServiceName = options.ServiceName;
                opt.ServiceVersion = options.ServiceVersion;
                opt.Environment = options.Environment;
                opt.TempoEndpoint = options.TempoEndpoint;
                opt.SamplingRatio = options.SamplingRatio;
                opt.EnableSqlInstrumentation = options.EnableSqlInstrumentation;
                opt.EnableHttpClientInstrumentation = options.EnableHttpClientInstrumentation;
                opt.EnableConsoleExporter = options.EnableConsoleExporter;
                opt.EnableAutoLogging = options.EnableAutoLogging;
                opt.CustomTags = options.CustomTags;
                opt.ResourceAttributes = options.ResourceAttributes;
            });

            Console.WriteLine("🔍 [DEBUG] About to configure OpenTelemetry...");

            services.AddOpenTelemetry()
                .WithTracing(tracing => tracing
                    .ConfigureStandardTracing(options)
                    .AddAspNetCoreInstrumentation(aspnet =>
                    {
                        aspnet.Filter = context => FilterHttpCalls(context, options);
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
                        otlp.Endpoint = new Uri(options.TempoEndpoint);
                        otlp.TimeoutMilliseconds = options.TimeoutMilliseconds;
                        switch (options.Protocol)
                        {
                            case OtlpProtocol.Grpc:
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                                Console.WriteLine("[METRICS CONFIG]   Using gRPC protocol");
                                break;

                            case OtlpProtocol.HttpProtobuf:
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                                Console.WriteLine("[METRICS CONFIG]   Using HTTP/Protobuf protocol");
                                break;

                            default:
                                otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
                                Console.WriteLine("[METRICS CONFIG]   Using default HTTP/Protobuf protocol");
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

            Console.WriteLine("🔍 [DEBUG] OpenTelemetry configuration completed");
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

        private static bool FilterHttpCalls(Microsoft.AspNetCore.Http.HttpContext httpContext, ObservabilityOptions options)
        {
            var path = httpContext.Request.Path.Value?.ToLower() ?? "";
            var method = httpContext.Request.Method;

            var excludedPaths = new[]
            {
                "/health", "/metrics", "/swagger", "/favicon",
                "/_framework", "/css", "/js", "/lib"
            };

            if (path == "/" || excludedPaths.Any(excluded => path.Contains(excluded)))
            {
                Console.WriteLine($"🚫 [FILTER] Excluding: {method} {path}");
                return false;
            }

            Console.WriteLine($"🚫 [FILTER] Including API: {method} {path}");
            return true;
        }
    }
}
#endif