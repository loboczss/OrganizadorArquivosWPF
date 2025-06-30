using System;
using System.IO;
using Newtonsoft.Json;


namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
/// Provides access to application configuration values.
/// Values are read from a JSON file stored under the user's
/// LocalApplicationData folder.
    /// </summary>
    public static class Config
    {

        public static readonly string TenantId;
        public static readonly string ClientId;
        public static readonly string ClientSecret;

        static Config()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OneEngRenamer");
            var file = Path.Combine(dir, "config.json");

            if (!File.Exists(file))
                throw new InvalidOperationException($"Configuration file '{file}' not found.");

            var json = File.ReadAllText(file);
            var cfg = JsonConvert.DeserializeObject<ConfigFile>(json)
                ?? throw new InvalidOperationException("Invalid configuration file.");

            TenantId = cfg.TenantId ?? throw new InvalidOperationException("TenantId missing.");
            ClientId = cfg.ClientId ?? throw new InvalidOperationException("ClientId missing.");
            ClientSecret = cfg.ClientSecret ?? throw new InvalidOperationException("ClientSecret missing.");
        }

        private class ConfigFile
        {
            public string? TenantId { get; set; }
            public string? ClientId { get; set; }
            public string? ClientSecret { get; set; }
        }
    }
}
