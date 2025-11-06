using AssistantEngine.UI.Services.Implementation.MCP;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AssistantEngine.UI.Services.Models
{
    public enum AuthType { None, BearerOrApiKey, OAuth2 }

    // NEW: bring ClientMode onto the connector so UI can branch cleanly
    public class McpConnectorConfig
    {
        public string Id { get; set; } = "New Connector";
        public string ServerUrl { get; set; } = "http://localhost:4001";

        public AuthType Auth { get; set; } = AuthType.None;

        // API key/bearer
        public string? AuthToken { get; set; }
        public string ApiKeyHeaderName { get; set; } = "Authorization";

        // OAuth (expanded)
        public ClientMode ClientMode { get; set; } = ClientMode.PublicPkce; // NEW
        public bool SupportsApiKey { get; set; } = false;                   // NEW (hint from provider)

        public string? OAuthClientId { get; set; }
        public string? OAuthClientSecret { get; set; } // NEW: for UserSuppliedConfidential only

        public string? OAuthAuthUrl { get; set; }
        public string? OAuthTokenUrl { get; set; }
        public string? OAuthRedirectUrl { get; set; }
        public string? OAuthScopes { get; set; }
        public string? OAuthAccessToken { get; set; }
        public string? OAuthRefreshToken { get; set; }
        public DateTimeOffset? OAuthExpiryUtc { get; set; }

        public List<string> EnabledTools { get; set; } = new();
        public string? ProviderKey { get; set; }
        public string? ProviderLogo { get; set; }

        public string? ResourceMetadataUrl { get; set; }           // RFC9728 discovered URL
        public string? AuthorizationServerIssuer { get; set; }     // chosen AS issuer
        public string? RegistrationEndpoint { get; set; }          // from AS metadata

        // DCR artifacts (RFC7591)
        public string? RegisteredClientId { get; set; }            // if obtained via DCR
        public string? RegistrationAccessToken { get; set; }       // to update/delete registration
        public string? RegistrationClientUri { get; set; }         // management URL from AS
        public long? ClientIdIssuedAt { get; set; }
        public long? ClientSecretExpiresAt { get; set; }

        // Last scope challenge (for step-up)
        public string? LastChallengedScopes { get; set; }
    }

}



