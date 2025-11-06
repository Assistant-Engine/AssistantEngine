using AssistantEngine.UI.Services.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Extensions
{
    public static class McpConnectorConfigExtensions
    {
        public static McpConnectorConfig Clone(this McpConnectorConfig c)
        {
            if (c is null) return new McpConnectorConfig();

            return new McpConnectorConfig
            {
                Id = c.Id,
                ServerUrl = c.ServerUrl,

                // Provider & auth
                ProviderKey = c.ProviderKey,
                ProviderLogo = c.ProviderLogo,
                Auth = c.Auth,
                AuthToken = c.AuthToken,
                ApiKeyHeaderName = c.ApiKeyHeaderName,
                SupportsApiKey = c.SupportsApiKey,
                ClientMode = c.ClientMode,

                // OAuth / OIDC
                OAuthClientId = c.OAuthClientId,
                OAuthClientSecret = c.OAuthClientSecret,
                OAuthAuthUrl = c.OAuthAuthUrl,
                OAuthTokenUrl = c.OAuthTokenUrl,
                OAuthRedirectUrl = c.OAuthRedirectUrl,
                OAuthScopes = c.OAuthScopes,
                OAuthAccessToken = c.OAuthAccessToken,
                OAuthRefreshToken = c.OAuthRefreshToken,
                OAuthExpiryUtc = c.OAuthExpiryUtc,

                // Discovery / DCR
                AuthorizationServerIssuer = c.AuthorizationServerIssuer,
                ResourceMetadataUrl = c.ResourceMetadataUrl,
                LastChallengedScopes = c.LastChallengedScopes,
                RegistrationEndpoint = c.RegistrationEndpoint,
                RegisteredClientId = c.RegisteredClientId,
                RegistrationAccessToken = c.RegistrationAccessToken,
                RegistrationClientUri = c.RegistrationClientUri,
                ClientIdIssuedAt = c.ClientIdIssuedAt,
                ClientSecretExpiresAt = c.ClientSecretExpiresAt,

                // Tools
                EnabledTools = c.EnabledTools != null ? new List<string>(c.EnabledTools) : new List<string>()
            };
        }
    }

}
