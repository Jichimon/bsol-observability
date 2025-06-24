using Microsoft.Extensions.Configuration;
using System;

namespace Bsol.Observability.Configuration
{
    public static class ConfigurationExtensions
    {
        public static ObservabilityOptions GetObservabilityOptions(this IConfiguration configuration)
        {
            var options = new ObservabilityOptions();
            configuration.GetSection(ObservabilityOptions.SectionName).Bind(options);

            if (string.IsNullOrEmpty(options.ServiceName))
            {
                throw new InvalidOperationException("ServiceName es requerido en la configuración del paquete Observability");
            }

            if (!Uri.TryCreate(options.TempoEndpoint, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException("TempoEndpoint debe ser una URL válida");
            }

            return options;
        }
    }
}
