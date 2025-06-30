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
            TenantId = GetSetting("3b08e64e-b3be-402b-bb26-1fa4f91cf61f");
            ClientId = GetSetting("3cffac6a-f9d9-42d1-9065-4054fcd40163");
            ClientSecret = GetSetting("JFd8Q~hHgTYYo0P0EjAM8mpe3xm3.5vTfCHRFc.T");
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
