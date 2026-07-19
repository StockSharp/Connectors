namespace StockSharp.Uniswap.Native;

sealed class UniswapGraphClient : BaseLogReceiver
{
    private const string _poolsQuery = """
        query TopPools($first: Int!) {
          pools(first: $first, orderBy: totalValueLockedUSD,
            orderDirection: desc, where: { liquidity_gt: "0" }) {
            id feeTier liquidity totalValueLockedUSD volumeUSD
            token0 { id symbol name decimals }
            token1 { id symbol name decimals }
          }
        }
        """;
    private const string _swapsQuery = """
        query PoolSwaps($pool: String!, $first: Int!, $timestamp: BigInt!) {
          swaps(first: $first, orderBy: timestamp, orderDirection: desc,
            where: { pool: $pool, timestamp_gte: $timestamp }) {
            id timestamp amount0 amount1 amountUSD sqrtPriceX96 tick
            transaction { id }
          }
        }
        """;
    private const int _maximumResponseBytes = 32 * 1024 * 1024;
    private readonly Uri _endpoint;
    private readonly HttpClient _http = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.GZip |
            DecompressionMethods.Deflate,
    });
    private readonly SemaphoreSlim _sendSync = new(1, 1);
    private readonly JsonSerializerSettings _jsonSettings = new()
    {
        DateParseHandling = DateParseHandling.None,
        FloatParseHandling = FloatParseHandling.Decimal,
        NullValueHandling = NullValueHandling.Ignore,
        Formatting = Formatting.None,
        Culture = CultureInfo.InvariantCulture,
    };
    private DateTime _nextSend;

    public UniswapGraphClient(SecureString apiKey, string subgraphId)
    {
        var key = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
        subgraphId = subgraphId?.Trim();
        if (key.IsEmpty() || subgraphId.IsEmpty())
            throw new ArgumentException(
                "The Graph API key and subgraph ID are required together.");
        if (subgraphId.Any(static ch =>
            !(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
            throw new ArgumentException("Invalid subgraph identifier.",
                nameof(subgraphId));
        _endpoint = new Uri(
            $"https://gateway.thegraph.com/api/{Uri.EscapeDataString(key)}" +
            $"/subgraphs/id/{Uri.EscapeDataString(subgraphId)}");
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StockSharp-Uniswap-Connector/1.0");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
        _http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
    }

    public override string Name => "Uniswap_Subgraph";

    protected override void DisposeManaged()
    {
        _http.Dispose();
        _sendSync.Dispose();
        base.DisposeManaged();
    }

    public async ValueTask<UniswapPool[]> GetPoolsAsync(int maximum,
        CancellationToken cancellationToken)
    {
        var response = await SendAsync<UniswapPoolVariables,
            UniswapPoolData>(new()
            {
                Query = _poolsQuery,
                Variables = new() { First = maximum.Min(1000).Max(1) },
            }, cancellationToken);
        return response.Pools ?? [];
    }

    public async ValueTask<UniswapSwap[]> GetSwapsAsync(string poolId,
        DateTime from, int maximum, CancellationToken cancellationToken)
    {
        var response = await SendAsync<UniswapSwapVariables,
            UniswapSwapData>(new()
            {
                Query = _swapsQuery,
                Variables = new()
                {
                    Pool = poolId.NormalizeAddress(),
                    First = maximum.Min(1000).Max(1),
                    Timestamp = from.ToUnixSeconds().Max(0)
                    .ToString(CultureInfo.InvariantCulture),
                },
            }, cancellationToken);
        return response.Swaps ?? [];
    }

    private async ValueTask<TData> SendAsync<TVariables, TData>(
        UniswapGraphRequest<TVariables> payload,
        CancellationToken cancellationToken)
        where TVariables : class
        where TData : class
    {
        var body = JsonConvert.SerializeObject(payload, _jsonSettings);
        for (var attempt = 0; ; attempt++)
        {
            await WaitForSendAsync(cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post,
                _endpoint)
            {
                Content = new StringContent(body, Encoding.UTF8,
                    "application/json"),
            };
            using var response = await _http.SendAsync(request,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var responseBody = await ReadBodyAsync(response.Content,
                cancellationToken);
            if (attempt < 3 && (response.StatusCode ==
                    (HttpStatusCode)429 || (int)response.StatusCode >= 500))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(
                    500 * (1 << attempt)), cancellationToken);
                continue;
            }
            if (!response.IsSuccessStatusCode)
                throw new UniswapApiException(response.StatusCode,
                    $"The Graph HTTP {(int)response.StatusCode}: " +
                    Truncate(responseBody));
            UniswapGraphResponse<TData> graph;
            try
            {
                graph = JsonConvert.DeserializeObject<
                    UniswapGraphResponse<TData>>(responseBody,
                    _jsonSettings);
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    "The Graph returned an unexpected response shape.",
                    error);
            }
            if (graph?.Errors is { Length: > 0 })
                throw new InvalidOperationException(
                    "The Graph rejected the query: " + string.Join("; ",
                        graph.Errors.Select(static error => error.Message)));
            return graph?.Data ?? throw new InvalidDataException(
                "The Graph returned no data object.");
        }
    }

    private async ValueTask WaitForSendAsync(
        CancellationToken cancellationToken)
    {
        await _sendSync.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextSend - DateTime.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);
            _nextSend = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
        }
        finally
        {
            _sendSync.Release();
        }
    }

    private static string Truncate(string value)
    {
        value = value?.Trim();
        return value.IsEmpty() ? "request rejected"
            : value.Length <= 512 ? value : value[..512];
    }

    private static async ValueTask<string> ReadBodyAsync(HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > _maximumResponseBytes)
            throw new InvalidDataException(
                "The Graph response exceeds the 32 MiB safety limit.");
        await using var source = await content.ReadAsStreamAsync(
            cancellationToken);
        using var target = new MemoryStream();
        var block = new byte[81920];
        while (true)
        {
            var read = await source.ReadAsync(block, cancellationToken);
            if (read == 0)
                break;
            if (target.Length + read > _maximumResponseBytes)
                throw new InvalidDataException(
                    "The Graph response exceeds the 32 MiB safety limit.");
            target.Write(block, 0, read);
        }
        return Encoding.UTF8.GetString(target.ToArray());
    }
}
