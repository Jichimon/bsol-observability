using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bsol.Observability.Core
{

    public interface ITraceContextAccessor
    {
        TraceContext? GetCurrentTraceContext();
    }

    public class TraceContext
    {
        public string TraceId { get; set; } = string.Empty;
        public string SpanId { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public string ServiceName { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
    }
    internal class TraceContextAccessor : ITraceContextAccessor
    {
        public TraceContext? GetCurrentTraceContext()
        {
            var activity = Activity.Current;
            if (activity == null) return null;

            return new TraceContext
            {
                TraceId = activity.TraceId.ToString(),
                SpanId = activity.SpanId.ToString(),
                CorrelationId = activity.GetTagItem("correlation.id")?.ToString() ?? "",
                ServiceName = activity.Source.Name,
                OperationName = activity.DisplayName
            };
        }
    }
}
