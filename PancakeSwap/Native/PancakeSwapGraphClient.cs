namespace StockSharp.PancakeSwap.Native;

sealed class PancakeSwapGraphClient : BaseLogReceiver
{
	private const string _v3PoolsQuery = """
		query TopPools($first: Int!) {
		  pools(first: $first, orderBy: totalValueLockedUSD,
		    orderDirection: desc, where: { liquidity_gt: "0" }) {
		    id feeTier totalValueLockedUSD
		    token0 { id symbol name decimals }
		    token1 { id symbol name decimals }
		  }
		}
		""";
	private const string _v2PoolsQuery = """
		query TopPairs($first: Int!) {
		  pairs(first: $first, orderBy: reserveUSD,
		    orderDirection: desc, where: { reserveUSD_gt: "0" }) {
		    id reserveUSD
		    token0 { id symbol name decimals }
		    token1 { id symbol name decimals }
		  }
		}
		""";
	private const string _v3SwapsQuery = """
		query PoolSwaps($pool: String!, $first: Int!, $timestamp: BigInt!) {
		  swaps(first: $first, orderBy: timestamp, orderDirection: desc,
		    where: { pool: $pool, timestamp_gte: $timestamp }) {
		    id timestamp amount0 amount1 transaction { id }
		  }
		}
		""";
	private const string _v2SwapsQuery = """
		query PairSwaps($pool: String!, $first: Int!, $timestamp: BigInt!) {
		  swaps(first: $first, orderBy: timestamp, orderDirection: desc,
		    where: { pair: $pool, timestamp_gte: $timestamp }) {
		    id timestamp amount0In amount1In amount0Out amount1Out
		    transaction { id }
		  }
		}
		""";
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly PancakeSwapPoolVersions _poolVersion;
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

	public PancakeSwapGraphClient(SecureString apiKey, string source,
		PancakeSwapPoolVersions poolVersion)
	{
		source = source?.Trim();
		if (source.IsEmpty())
			throw new ArgumentException(
				"A PancakeSwap subgraph source is required.", nameof(source));
		if (Uri.TryCreate(source, UriKind.Absolute, out var endpoint))
		{
			if (!endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
				throw new ArgumentException(
					"Subgraph endpoint must use HTTPS.", nameof(source));
			_endpoint = endpoint;
		}
		else
		{
			if (source.Any(static ch =>
				!(char.IsLetterOrDigit(ch) || ch is '-' or '_')))
				throw new ArgumentException(
					"Invalid subgraph deployment identifier.",
					nameof(source));
			var key = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
			if (key.IsEmpty())
				throw new ArgumentException(
					"A The Graph API key is required for a deployment ID.",
					nameof(apiKey));
			_endpoint = new Uri(
				$"https://gateway.thegraph.com/api/" +
				$"{Uri.EscapeDataString(key)}/subgraphs/id/" +
				Uri.EscapeDataString(source));
		}
		_poolVersion = poolVersion;
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-PancakeSwap-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => $"PancakeSwap_{_poolVersion}_Subgraph";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_sendSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<PancakeSwapPool[]> GetPoolsAsync(int maximum,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync<PancakeSwapPoolVariables,
			PancakeSwapPoolData>(new()
		{
			Query = _poolVersion == PancakeSwapPoolVersions.V3
				? _v3PoolsQuery
				: _v2PoolsQuery,
			Variables = new() { First = maximum.Min(1000).Max(1) },
		}, cancellationToken);
		return _poolVersion == PancakeSwapPoolVersions.V3
			? response.Pools ?? []
			: response.Pairs ?? [];
	}

	public async ValueTask<PancakeSwapSwap[]> GetSwapsAsync(string poolId,
		DateTime from, int maximum, CancellationToken cancellationToken)
	{
		var response = await SendAsync<PancakeSwapSwapVariables,
			PancakeSwapSwapData>(new()
		{
			Query = _poolVersion == PancakeSwapPoolVersions.V3
				? _v3SwapsQuery
				: _v2SwapsQuery,
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
		PancakeSwapGraphRequest<TVariables> payload,
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
				throw new PancakeSwapApiException(response.StatusCode,
					$"The Graph HTTP {(int)response.StatusCode}: " +
					Truncate(responseBody));
			PancakeSwapGraphResponse<TData> graph;
			try
			{
				graph = JsonConvert.DeserializeObject<
					PancakeSwapGraphResponse<TData>>(responseBody,
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
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
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
