namespace StockSharp.ChainlinkDataStreams.Native;

readonly record struct ChainlinkAuthHeaders(string ApiKey, string Timestamp,
    string Signature);

static class ChainlinkAuthenticator
{
    public static ChainlinkAuthHeaders Create(HttpMethod method, Uri requestUri,
        ReadOnlySpan<byte> body, string apiKey, string apiSecret, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(requestUri);
        if (apiKey.IsEmpty() || apiSecret.IsEmpty())
            throw new ArgumentException("Chainlink API credentials are empty.");

        now = now.EnsureUtc();
        var timestamp = checked((long)now.ToUnix(false)).ToString(
            CultureInfo.InvariantCulture);
        var bodyHash = Convert.ToHexString(SHA256.HashData(body))
            .ToLowerInvariant();
        var canonical = string.Join(" ", method.Method,
            requestUri.PathAndQuery, bodyHash, apiKey, timestamp);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
        var signature = Convert.ToHexString(hmac.ComputeHash(
            Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return new(apiKey, timestamp, signature);
    }

    public static void Apply(HttpRequestHeaders headers,
        ChainlinkAuthHeaders authentication)
    {
        ArgumentNullException.ThrowIfNull(headers);
        headers.TryAddWithoutValidation("Authorization", authentication.ApiKey);
        headers.TryAddWithoutValidation("X-Authorization-Timestamp",
            authentication.Timestamp);
        headers.TryAddWithoutValidation("X-Authorization-Signature-SHA256",
            authentication.Signature);
    }

    public static void Apply(ClientWebSocketOptions options,
        ChainlinkAuthHeaders authentication)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.SetRequestHeader("Authorization", authentication.ApiKey);
        options.SetRequestHeader("X-Authorization-Timestamp",
            authentication.Timestamp);
        options.SetRequestHeader("X-Authorization-Signature-SHA256",
            authentication.Signature);
    }
}
