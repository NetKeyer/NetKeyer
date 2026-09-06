using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NetKeyer.Helpers;

public static class RendezvousJwtTokenFactory
{
    public static string CreateHs256Token(string secret, IDictionary<string, object> claims, string keyId = "")
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException("JWT secret is required.", nameof(secret));
        }

        if (claims == null)
        {
            throw new ArgumentNullException(nameof(claims));
        }

        var header = new Dictionary<string, object>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };

        string normalizedKeyId = (keyId ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(normalizedKeyId))
        {
            header["kid"] = normalizedKeyId;
        }

        string headerJson = JsonSerializer.Serialize(header);
        string claimsJson = JsonSerializer.Serialize(claims);

        string encodedHeader = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        string encodedClaims = Base64UrlEncode(Encoding.UTF8.GetBytes(claimsJson));
        string unsignedToken = $"{encodedHeader}.{encodedClaims}";

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] signatureBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(unsignedToken));
        string encodedSignature = Base64UrlEncode(signatureBytes);

        return $"{unsignedToken}.{encodedSignature}";
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
