﻿using System.Collections.Generic;

namespace Bsol.Observability.Configuration
{
    public class ObservabilityOptions
    {
        public const string SectionName = "Observability";

        public string ServiceName { get; set; } = string.Empty;
        public string ServiceVersion { get; set; } = "1.0.0";
        public string Environment { get; set; } = "development";
        public string TempoUrl { get; set; } = "http://localhost:4318";
        public double SamplingRatio { get; set; } = 1.0;
        public bool EnableSqlInstrumentation { get; set; } = true;
        public bool EnableHttpClientInstrumentation { get; set; } = true;
        public bool EnableConsoleExporter { get; set; } = false;
        public bool EnableAutoLogging { get; set; } = true;
        public Dictionary<string, string> CustomTags { get; set; } = new();
        public Dictionary<string, string> ResourceAttributes { get; set; } = new();
        public OtlpProtocol Protocol { get; set; } = OtlpProtocol.HttpProtobuf;
        public string? Headers { get; set; }
        public int TimeoutMilliseconds { get; set; } = 10000;
        public int BatchDelayMilliseconds { get; set; } = 500;

        /// <summary>
        /// Patrones de URLs/hosts para excluir del tracing de HttpClient requests salientes.
        /// Se evalúa usando Contains(), así que pueden ser dominios parciales, puertos, etc.
        /// </summary>
        public List<string> HttpClientExcludePatterns { get; set; } = new();


        /// <summary>
        /// Patrones de URLs/hosts para excluir del tracing de requests entrantes.
        /// Se evalúa usando Contains(), así que pueden ser dominios parciales, puertos, etc.
        /// </summary>
        public List<string> ApiCallsExcludePatterns { get; set; } = new();

        public bool EnableDebugLogging { get; set; } = false;

    }

    public enum OtlpProtocol
    {
        Grpc = 0,
        HttpProtobuf = 1
    }
}
