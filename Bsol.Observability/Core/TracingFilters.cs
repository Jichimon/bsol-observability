using Bsol.Observability.Configuration;
using Bsol.Observability.Utils;
using System.Collections.Generic;
using System.Linq;

namespace Bsol.Observability.Core
{
    public static class TracingFilters
    {
        public static bool FilterHttpWebRequest(System.Net.HttpWebRequest request, ObservabilityOptions? options = null)
        {
            var uri = request.RequestUri?.ToString()?.ToLower() ?? "";

            var excludePatterns = GetCombinedHttpClientExcludePatterns(options);

            if (excludePatterns.Any(pattern => uri.Contains(pattern.ToLower())))
            {
                ObservabilityLogging.Debug($"🚫 [HTTP FILTER] Excluding: {uri}");
                return false;
            }

            ObservabilityLogging.Debug($"✅ [HTTP FILTER] Including: {uri}");
            return true;
        }

        public static bool FilterHttpRequestMessage(System.Net.Http.HttpRequestMessage httpRequestMessage, ObservabilityOptions? options = null)
        {
            var uri = httpRequestMessage.RequestUri?.ToString()?.ToLower() ?? "";

            var excludePatterns = GetCombinedHttpClientExcludePatterns(options);

            if (excludePatterns.Any(pattern => uri.Contains(pattern.ToLower())))
            {
                ObservabilityLogging.Debug($"🚫 [HTTPCLIENT FILTER] Excluding: {uri}");
                return false;
            }

            ObservabilityLogging.Debug($"✅ [HTTPCLIENT FILTER] Including: {uri}");
            return true;
        }

#if NET8_0
        public static bool FilterAspNetCoreHttpCalls(Microsoft.AspNetCore.Http.HttpContext httpContext, ObservabilityOptions? options = null)
        {
            var path = httpContext.Request.Path.Value?.ToLower() ?? "";
            var method = httpContext.Request.Method;

            var excludedPaths = GetCombinedAspNetCoreExcludePatterns(options);

            if (string.IsNullOrEmpty(path) || path == "/")
            {
                ObservabilityLogging.Debug($"🚫 [ASPNET FILTER] Excluding root: {method} {path}");
                return false;
            }

            if (excludedPaths.Any(excluded => path.Contains(excluded.ToLower())))
            {
                ObservabilityLogging.Debug($"🚫 [ASPNET FILTER] Excluding: {method} {path}");
                return false;
            }

            ObservabilityLogging.Debug($"✅ [ASPNET FILTER] Including API: {method} {path}");
            return true;
        }
#endif

        public static List<string> GetAllExcludePatterns(ObservabilityOptions? options)
        {
            var patterns = new List<string>();

            patterns.AddRange(GetDefaultHttpClientExcludePatterns());
            patterns.AddRange(GetDefaultAwsExcludePatterns());
            patterns.AddRange(GetDefaultAspNetCoreExcludePatterns());

            if (options != null)
            {
                patterns.AddRange(options.HttpClientExcludePatterns);
                patterns.AddRange(options.ApiCallsExcludePatterns);
            }

            return patterns;
        }

        /// <summary>
        /// Combina todos los patrones de exclusión para HttpClient basado en la configuración
        /// </summary>
        private static List<string> GetCombinedHttpClientExcludePatterns(ObservabilityOptions? options)
        {
            var patterns = new List<string>();

            patterns.AddRange(GetDefaultHttpClientExcludePatterns());
            patterns.AddRange(GetDefaultAwsExcludePatterns());

            if (options != null)
            {
                patterns.AddRange(options.HttpClientExcludePatterns);
            }

            return patterns;
        }

        private static List<string> GetCombinedAspNetCoreExcludePatterns(ObservabilityOptions? options)
        {
            var patterns = new List<string>();

            patterns.AddRange(GetDefaultAspNetCoreExcludePatterns());

            if (options != null)
            {
                patterns.AddRange(options.ApiCallsExcludePatterns);
            }

            return patterns;
        }

        /// <summary>
        /// Patrones por defecto para HttpClient si no se especifica configuración
        /// </summary>
        private static List<string> GetDefaultHttpClientExcludePatterns()
        {
            return new List<string>
            {
                // Telemetría y observabilidad
                "applicationinsights",
                "livediagnostics.monitor.azure.com",
                "monitor.azure.com",
                "4318", "4317", // Tempo/OTLP ports
                
                // Cloud metadata services
                "169.254.169.254", // AWS metadata
                "metadata.google.internal", // GCP metadata
                
                // Health checks comunes
                "/health", "/metrics", "/ready", "/ping", "/status"
            };
        }

        /// <summary>
        /// Patrones por defecto para AWS si no se especifica configuración
        /// </summary>
        private static List<string> GetDefaultAwsExcludePatterns()
        {
            return new List<string>
            {
                "sqs.us-east-1.amazonaws.com",
                "s3.amazonaws.com",
                "dynamodb.us-east-1.amazonaws.com",
                "rds.amazonaws.com",
                "lambda.us-east-1.amazonaws.com"
            };
        }

        /// <summary>
        /// Patrones por defecto para AspNetCore si no se especifica configuración
        /// </summary>
        private static List<string> GetDefaultAspNetCoreExcludePatterns()
        {
            return new List<string>
            {
                // Health y sistema
                "/health", "/metrics", "/ready", "/live", "/ping",
                "/swagger", "/favicon", "/robots.txt",
                
                // Assets estáticos  
                "/_framework", "/css", "/js", "/lib", "/images", "/fonts",
                ".css", ".js", ".ico", ".png", ".jpg", ".svg", ".woff",
                
                // APIs internas
                "/internal/", "/system/", "/admin/telemetry"
            };
        }
    }
}
