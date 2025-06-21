// GerarTokenService.cs — ONE Engenharia
// Serviço de geração de Access Token via Refresh Token no Dropbox
// Compatível com C# 7.3 e .NET Framework 4.x

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace OrganizadorArquivosWPF.Services
{
    public class GerarTokenService
    {
        private readonly string _appKey;
        private readonly string _appSecret;
        private readonly string _refreshToken;

        /// <summary>
        /// Construtor do serviço de token.
        /// </summary>
        /// <param name="appKey">App Key do Dropbox</param>
        /// <param name="appSecret">App Secret do Dropbox</param>
        /// <param name="refreshToken">Refresh Token do Dropbox</param>
        public GerarTokenService(string appKey, string appSecret, string refreshToken)
        {
            _appKey = appKey;
            _appSecret = appSecret;
            _refreshToken = refreshToken;
        }

        /// <summary>
        /// Gera um Access Token válido por 4 horas usando o Refresh Token.
        /// </summary>
        /// <returns>Access Token (string)</returns>
        public async Task<string> ObterAccessTokenAsync()
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var parametros = new Dictionary<string, string>
                    {
                        { "grant_type", "refresh_token" },
                        { "refresh_token", _refreshToken },
                        { "client_id", _appKey },
                        { "client_secret", _appSecret }
                    };

                    var conteudo = new FormUrlEncodedContent(parametros);

                    var resposta = await client.PostAsync("https://api.dropboxapi.com/oauth2/token", conteudo);

                    if (!resposta.IsSuccessStatusCode)
                    {
                        var erro = await resposta.Content.ReadAsStringAsync();
                        throw new Exception($"Erro ao gerar Access Token: {resposta.StatusCode} - {erro}");
                    }

                    var jsonResposta = await resposta.Content.ReadAsStringAsync();
                    var resultado = JsonConvert.DeserializeObject<DropboxTokenResponse>(jsonResposta);

                    if (resultado == null || string.IsNullOrEmpty(resultado.AccessToken))
                    {
                        throw new Exception("Resposta inválida do Dropbox. Access Token não encontrado.");
                    }

                    return resultado.AccessToken;
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Erro no GerarTokenService: " + ex.Message);
            }
        }

        /// <summary>
        /// Modelo da resposta JSON do Dropbox
        /// </summary>
        private class DropboxTokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonProperty("scope")]
            public string Scope { get; set; }
        }
    }
}
