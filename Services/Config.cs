using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Provides access to application configuration values.
    /// Values are read from environment variables or an optional
    /// <c>config.json</c> file located next to the executable.
    /// </summary>
    public static class Config
    {
        private static readonly Dictionary<string, string>? _fileSettings;

        public static readonly string TenantId;
        public static readonly string ClientId;
        public static readonly string ClientSecret;

        static Config()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    _fileSettings = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
                catch
                {
                    // ignore malformed config file
                }
            }

            TenantId = GetSetting("TENANT_ID");
            ClientId = GetSetting("CLIENT_ID");
            ClientSecret = GetSetting("CLIENT_SECRET");
        }

        private static string GetSetting(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            if (_fileSettings != null && _fileSettings.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile))
                return fromFile;

            throw new InvalidOperationException($"Configuration value '{key}' not found.");
        }
    }
}
