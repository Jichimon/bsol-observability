#if NET8_0
using Microsoft.AspNetCore.Http;
using System.Linq;
using System;
#endif

namespace Bsol.Observability.Utils
{
    public static class CorrelationIdGenerator
    {
#if NET8_0
        public static string GetOrGenerate(HttpContext context)
        {
            var headerNames = new[] { "X-Correlation-ID", "X-Request-ID", "X-Trace-ID", "Request-ID" };

            foreach (var headerName in headerNames)
            {
                if (context.Request.Headers.TryGetValue(headerName, out var correlationId)
                    && !string.IsNullOrEmpty(correlationId.FirstOrDefault()))
                {
                    return correlationId.FirstOrDefault()!;
                }
            }

            // Intentar obtener de query parameters
            if (context.Request.Query.TryGetValue("correlationId", out var queryCorrelationId)
                && !string.IsNullOrEmpty(queryCorrelationId.FirstOrDefault()))
            {
                return queryCorrelationId.FirstOrDefault()!;
            }

            // Generar nuevo
            return Generate();
        }
#endif

        public static string Generate()
        {
            return System.Guid.NewGuid().ToString("N").Substring(0, 16);
        }
    }
}