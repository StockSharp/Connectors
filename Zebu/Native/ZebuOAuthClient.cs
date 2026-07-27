namespace StockSharp.Zebu.Native;

sealed class ZebuOAuthToken
{
    public string AccessToken { get; init; }
    public string RefreshToken { get; init; }
    public string UserId { get; init; }
    public string AccountId { get; init; }
    public int ExpiresIn { get; init; }
}

sealed class ZebuOAuthClient : BaseLogReceiver
{
    private readonly HttpClient _httpClient;

    public ZebuOAuthClient(
        Uri restAddress,
        HttpMessageHandler handler = null)
    {
        _httpClient = handler == null ? new() : new(handler);
        _httpClient.BaseAddress =
            restAddress ?? throw new ArgumentNullException(nameof(restAddress));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Zebu/1.0");
    }

    public override string Name => nameof(Zebu) + "_" +
        nameof(ZebuOAuthClient);

    protected override void DisposeManaged()
    {
        _httpClient.Dispose();
        base.DisposeManaged();
    }

    public Task<ZebuOAuthToken> ExchangeCode(
        SecureString clientId,
        SecureString clientSecret,
        SecureString authorizationCode,
        CancellationToken cancellationToken)
    {
        var id = clientId.ThrowIfEmpty(nameof(clientId)).UnSecure();
        var secret = clientSecret.ThrowIfEmpty(nameof(clientSecret)).UnSecure();
        var code = authorizationCode
            .ThrowIfEmpty(nameof(authorizationCode))
            .UnSecure();
        return Send(
            "GenAcsTok",
            new
            {
                code,
                checksum = ComputeChecksum(id, secret, code),
            },
            cancellationToken);
    }

    public Task<ZebuOAuthToken> Refresh(
        SecureString refreshToken,
        CancellationToken cancellationToken)
        => Send(
            "RefreshToken",
            new
            {
                refresh_token = refreshToken
                    .ThrowIfEmpty(nameof(refreshToken))
                    .UnSecure(),
            },
            cancellationToken);

    internal static string ComputeChecksum(
        string clientId,
        string clientSecret,
        string authorizationCode)
    {
        clientId.ThrowIfEmpty(nameof(clientId));
        clientSecret.ThrowIfEmpty(nameof(clientSecret));
        authorizationCode.ThrowIfEmpty(nameof(authorizationCode));
        return Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        clientId + clientSecret + authorizationCode)))
            .ToLowerInvariant();
    }

    internal static ZebuOAuthToken ParseToken(
        string operation,
        string json)
    {
        if (json.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Zebu OAuth returned an empty response for {operation}.");
        }

        JObject response;
        try
        {
            response = JObject.Parse(json);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException(
                $"Zebu OAuth returned invalid JSON for {operation}.",
                error);
        }

        var status = response.Value<string>("stat");
        if (!status.EqualsIgnoreCase("Ok"))
        {
            throw new InvalidOperationException(
                $"Zebu OAuth {operation} failed: " +
                response.Value<string>("emsg").IsEmpty(status));
        }

        var accessToken =
            response.Value<string>("access_token")
                .IsEmpty(response.Value<string>("susertoken"));
        if (accessToken.IsEmpty())
        {
            throw new InvalidOperationException(
                $"Zebu OAuth {operation} returned no access token.");
        }
        var expiresIn = int.TryParse(
            response.Value<string>("expires_in"),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var seconds)
                ? seconds
                : 0;
        var userId = response.Value<string>("uid")
            .IsEmpty(response.Value<string>("user_id"));
        return new()
        {
            AccessToken = accessToken,
            RefreshToken = response.Value<string>("refresh_token"),
            UserId = userId,
            AccountId = response.Value<string>("actid").IsEmpty(userId),
            ExpiresIn = Math.Max(0, expiresIn),
        };
    }

    private async Task<ZebuOAuthToken> Send(
        string path,
        object body,
        CancellationToken cancellationToken)
    {
        var json = JsonConvert.SerializeObject(body, Formatting.None);
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                $"jData={json}",
                Encoding.UTF8,
                "text/plain"),
        };
        this.AddVerboseLog("Zebu OAuth POST {0}.", path);
        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseContentRead,
            cancellationToken);
        var content = await response.Content.ReadAsStringAsync(
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Zebu OAuth {path} returned HTTP " +
                $"{(int)response.StatusCode}: {response.ReasonPhrase}.");
        }
        return ParseToken(path, content);
    }
}
