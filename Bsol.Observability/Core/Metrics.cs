using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bsol.Observability.Core
{
    public static class Metrics
    {
        public static readonly Meter Meter = new("Bsol.Metrics", "1.0.0");

        public static readonly Counter<int> HttpRequestsTotal =
            Meter.CreateCounter<int>("http_requests_total", "count", "Total number of HTTP requests");

        public static readonly Histogram<double> HttpRequestDuration =
            Meter.CreateHistogram<double>("http_request_duration_seconds", "seconds", "HTTP request duration");

        public static readonly Counter<int> BusinessOperationsTotal =
            Meter.CreateCounter<int>("business_operations_total", "count", "Total business operations");


        public static Counter<T> CreateCounter<T>(string name, string description) where T : struct
        {
            return Meter.CreateCounter<T>(name, description: description);
        }

        public static Histogram<T> CreateHistogram<T>(string name, string description) where T : struct
        {
            return Meter.CreateHistogram<T>(name, description: description);
        }
    }
}
