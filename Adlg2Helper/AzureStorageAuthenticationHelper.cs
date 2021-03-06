﻿using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Newtonsoft.Json.Linq;

namespace Adlg2Helper
{
    internal static class AzureStorageAuthenticationHelper
    {
        private static Guid _state = Guid.NewGuid();
        internal static AuthenticationHeaderValue BuildSignedAuthorizationHeader(
            string storageAccountName,
            string storageAccountKey,
            DateTime now,
            HttpRequestMessage httpRequestMessage,
            string contentEncoding = "",
            string contentLanguage = "",
            long? contentLength = null,
            string contentMd5 = "",
            string contentType = "",
            DateTime? date = null,
            DateTime? ifModifiedSince = null,
            string ifMatch = "",
            string ifNoneMatch = "",
            DateTime? ifUnmodifiedSince = null,
            string range = null
        )
        {
            var messageSignature =
                $"{httpRequestMessage.Method}\n" + //Verb
                $"{contentEncoding}\n" + //Content-Encoding
                $"{contentLanguage}\n" + //Content-Language
                $"{contentLength}\n" + //Content-Length
                $"{contentMd5}\n" + //Content-MD5
                $"{contentType}\n" + //Content-Type
                $"{date:R}\n" + //Date
                $"{ifModifiedSince:R}\n" + //If-Modified-Since
                $"{ifMatch}\n" + // If-Match
                $"{ifNoneMatch}\n" + //If-None-Match
                $"{ifUnmodifiedSince:R}\n" + //If-Unmodified-Since
                $"{range}\n" + //Range
                $"{string.Join("\n",httpRequestMessage.Headers.Where(h => h.Key.StartsWith("x-ms-")).OrderBy(h => h.Key).Select(h =>$"{h.Key}:{h.Value.Single()}"))}\n" + //x-ms headers
                $"{GetCanonicalizedResource(httpRequestMessage.RequestUri, storageAccountName)}";
            var signatureBytes = Encoding.UTF8.GetBytes(messageSignature.ToCharArray());
            var sha256 = new HMACSHA256 { Key = Convert.FromBase64String(storageAccountKey) };
            var signature = Convert.ToBase64String(sha256.ComputeHash(signatureBytes));
            return new AuthenticationHeaderValue("SharedKey", storageAccountName + ":" + signature);
        }

        private static string GetCanonicalizedResource(Uri address, string storageAccountName)
        {
            var sb = new StringBuilder("/").Append(storageAccountName).Append(address.AbsolutePath);
            var values = HttpUtility.ParseQueryString(address.Query);
            foreach (var item in values.AllKeys.OrderBy(k => k))
            {
                sb.Append("\n").Append(item).Append(':').Append(values[item]);
            }
            return sb.ToString();
        }

        private static string _token;
        private static DateTime _expires;
        internal static AuthenticationHeaderValue BuildBearerTokenHeader(HttpClient httpClient, string tenantId, string clientId, string clientSecret)
        {
            if (!string.IsNullOrEmpty(_token) && _expires > DateTime.UtcNow) return new AuthenticationHeaderValue("Bearer",_token);
            var response = JObject.Parse(
                httpClient.PostAsync($"https://login.microsoftonline.com/{tenantId}/oauth2/token",
                        new FormUrlEncodedContent(new KeyValuePair<string, string>[]
                        {
                            new KeyValuePair<string, string>("grant_type", "client_credentials"),
                            new KeyValuePair<string, string>("client_id", clientId),
                            new KeyValuePair<string, string>("client_secret", clientSecret),
                            new KeyValuePair<string, string>("resource", $"https://storage.azure.com"),
                        })).GetAwaiter().GetResult()
                    .Content.ReadAsStringAsync().GetAwaiter().GetResult()
            );
            _token = response.GetValue("access_token", StringComparison.InvariantCultureIgnoreCase).Value<string>();
            _expires = new DateTime(response.GetValue("expires_on").Value<long>());
            return new AuthenticationHeaderValue("Bearer", _token);
        }

        internal static void ClearToken()
        {
            _token = null;
        }
    }
}