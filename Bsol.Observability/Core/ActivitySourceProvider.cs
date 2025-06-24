using System.Diagnostics;
using System.Reflection;

namespace Bsol.Observability.Core
{
    public static class ActivitySourceProvider
    {
        private static readonly string AssemblyName = Assembly.GetExecutingAssembly().GetName().Name!;
        private static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

        public static readonly ActivitySource DefaultActivitySource = new(AssemblyName, Version);

        public static ActivitySource CreateActivitySource(string name, string? version = null)
        {
            return new ActivitySource(name, version ?? Version);
        }
    }
}
