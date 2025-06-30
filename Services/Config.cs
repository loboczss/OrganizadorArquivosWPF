using System;


namespace OrganizadorArquivosWPF.Services
{
    /// <summary>
    /// Provides access to application configuration values.
    /// Values are read from environment variables.
    /// </summary>
    public static class Config
    {

        public static readonly string TenantId;
        public static readonly string ClientId;
        public static readonly string ClientSecret;

        static Config()
        {
            TenantId = GetSetting("TENANT_ID");
            ClientId = GetSetting("CLIENT_ID");
            ClientSecret = GetSetting("CLIENT_SECRET");
        }

        private static string GetSetting(string key)
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (!string.IsNullOrWhiteSpace(value))
                return value;

            throw new InvalidOperationException($"Configuration value '{key}' not found.");
        }
    }
}
