﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace ProgrammaticExportSampleAppISV.Helpers
{
    public class TokenGenerator
    {
        private readonly ILogger<TokenGenerator> _logger;
        private readonly IConfiguration _configuration;
        public const int TokenGenerateRetryCount = 3;
        public string AccessToken { get; set; }
        public long ExpiryTime = 0;

        public TokenGenerator(ILogger<TokenGenerator> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        private async Task<HttpResponseMessage> PostFormUrlEncoded(string url, Dictionary<string, string> postData)
        {
            var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(postData) };
            return await client.SendAsync(req);
        }

        public async Task<string> GenerateADTokenWithRetries()
        {
            string accessToken = string.Empty;
            for (int i = 0; i < TokenGenerateRetryCount; i++)
            {
                try
                {
                    accessToken = await GenerateAdToken();
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError($"Token generation attempt {i} failed: {e.Message}");
                    continue;
                }
            }

            return accessToken;
        }

        public async Task<string> GenerateAdToken()
        {
            long currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Dictionary<string, string> postData = new Dictionary<string, string>();
            var finalTokenUrl = String.Format(
                _configuration["AdTokenConfig:TokenGenerateUrl"],
                _configuration["AdTokenConfig:TenantId"]);

            // All the IDs and secret should be fetched from KeyVault. It is in the config file
            // Only for demo purposes.
            postData.Add("resource", _configuration["AdTokenConfig:ResourceId"]);
            postData.Add("client_id", _configuration["AdTokenConfig:WebAppClientId"]);
            postData.Add("client_secret", _configuration["AdTokenConfig:WebAppClientSecret"]);

            // On first run or when Access token is null or expired, generate from scratch
            if (ExpiryTime == 0 || string.IsNullOrEmpty(AccessToken))
            {
                postData.Add("grant_type", "client_credentials");
            }
            else
            {
                return AccessToken;
            }

            TokenGeneratorResponseObject tokenGeneratorResponseObject;
            try
            {
                HttpResponseMessage response = await PostFormUrlEncoded(finalTokenUrl, postData);
                if ((int)response.StatusCode != 200)
                {
                    _logger.LogInformation("Token generation failed");
                    throw new Exception("Token generation failed");
                }
                var content = await response.Content.ReadAsStringAsync();
                tokenGeneratorResponseObject = JsonConvert.DeserializeObject<TokenGeneratorResponseObject>(content);
                if (string.IsNullOrEmpty(tokenGeneratorResponseObject.access_token))
                {
                    _logger.LogInformation("Token generation returned no access token");
                    throw new Exception("Token generation returned no acces token");
                }
            }
            catch (Exception e)
            {
                _logger.LogInformation($"Exception in token generation: {e.Message}");
                // Set expiry time to 0 on errors, so that on the next retry we generate token from scratch
                ExpiryTime = 0;
                throw new Exception($"Exception in token generation: {e.Message}");
            }
            AccessToken = tokenGeneratorResponseObject.access_token;
            ExpiryTime = long.Parse(tokenGeneratorResponseObject.expires_on);

            return AccessToken;
        }
    }
}
