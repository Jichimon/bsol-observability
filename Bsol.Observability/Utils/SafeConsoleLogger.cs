using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;

namespace Bsol.Observability.Utils
{
    internal static class SafeConsoleLogger
    {
        private static readonly bool _isConsoleAvailable = CheckConsoleAvailability();
        private static ILogger? _logger;

        public static void SetLogger(ILogger logger)
        {
            _logger = logger;
        }

        public static void WriteLine(string message)
        {
            try
            {
                if (_logger != null)
                {
                    _logger.LogDebug("[OBSERVABILITY] {Message}", message);
                    return;
                }

                Debug.WriteLine($"[OBSERVABILITY] {message}");

                if (_isConsoleAvailable)
                {
                    Console.WriteLine($"[OBSERVABILITY] {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OBSERVABILITY] Logging failed: {ex.Message}");
            }
        }

        public static void WriteLine(string format, params object[] args)
        {
            try
            {
                var message = string.Format(format, args);
                WriteLine(message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OBSERVABILITY] Logging format failed: {ex.Message}");
            }
        }

        private static bool CheckConsoleAvailability()
        {
            try
            {
                var test = Console.CursorLeft;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    internal static class ObservabilityLogging
    {
        public static bool EnableDebugLogging { get; set; } = false;

        public static void Debug(string message)
        {
            if (EnableDebugLogging)
            {
                SafeConsoleLogger.WriteLine(message);
            }
        }

        public static void Debug(string format, params object[] args)
        {
            if (EnableDebugLogging)
            {
                SafeConsoleLogger.WriteLine(format, args);
            }
        }
    }
}
