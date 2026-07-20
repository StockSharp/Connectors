namespace StockSharp.Avantis.Native;

sealed class AvantisApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _marketDataEndpoint;
	private readonly Uri _coreApiEndpoint;
	private readonly Uri _feedEndpoint;
	private readonly Uri _lazerEndpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};

	public AvantisApiClient(string marketDataEndpoint, string coreApiEndpoint,
		string feedEndpoint, string lazerEndpoint)
	{
		_marketDataEndpoint = CreateUri(marketDataEndpoint,
			nameof(marketDataEndpoint));
		_coreApiEndpoint = CreateUri(coreApiEndpoint, nameof(coreApiEndpoint));
		_feedEndpoint = CreateUri(feedEndpoint, nameof(feedEndpoint));
		_lazerEndpoint = CreateUri(lazerEndpoint, nameof(lazerEndpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Avantis-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Avantis_API";

	public string LazerEndpoint => _lazerEndpoint.AbsoluteUri;

	public ValueTask<AvantisMarketsData> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> GetMarketsCoreAsync(cancellationToken);

	private async ValueTask<AvantisMarketsData> GetMarketsCoreAsync(
		CancellationToken cancellationToken)
	{
		var response = await GetAsync<AvantisMarketsResponse>(
			_marketDataEndpoint, cancellationToken);
		return response?.Data ?? throw new InvalidDataException(
			"Avantis returned no market metadata.");
	}

	public ValueTask<AvantisFeedPriceResponse> GetPriceAsync(int pairIndex,
		CancellationToken cancellationToken)
	{
		if (pairIndex < 0)
			throw new ArgumentOutOfRangeException(nameof(pairIndex));
		return GetAsync<AvantisFeedPriceResponse>(Combine(_feedEndpoint,
			"v2/pairs/" + pairIndex.ToString(CultureInfo.InvariantCulture) +
			"/price-update-data"), cancellationToken);
	}

	public ValueTask<AvantisLazerPriceResponse> GetLazerPricesAsync(
		int[] feedIds, CancellationToken cancellationToken)
	{
		if (feedIds is null || feedIds.Length == 0 ||
			feedIds.Any(static id => id < 0))
			throw new ArgumentOutOfRangeException(nameof(feedIds));
		var endpoint = _lazerEndpoint.AbsoluteUri.Replace("/stream",
			"/latest_price", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
		var query = string.Join("&", feedIds.Distinct().Select(static id =>
			"price_feed_ids=" + id.ToString(CultureInfo.InvariantCulture)));
		return GetAsync<AvantisLazerPriceResponse>(new Uri(endpoint + "?" +
			query, UriKind.Absolute), cancellationToken);
	}

	public ValueTask<AvantisUserData> GetUserDataAsync(string walletAddress,
		CancellationToken cancellationToken)
	{
		walletAddress = walletAddress.NormalizeAddress();
		var endpoint = Combine(_coreApiEndpoint, "user-data?trader=" +
			Uri.EscapeDataString(walletAddress));
		return GetAsync<AvantisUserData>(endpoint, cancellationToken);
	}

	private async ValueTask<T> GetAsync<T>(Uri endpoint,
		CancellationToken cancellationToken)
		where T : class
	{
		for (var attempt = 0; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 3 && (response.StatusCode == (HttpStatusCode)429 ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					"Avantis API HTTP " + (int)response.StatusCode + ": " +
					Truncate(body));
			try
			{
				return JsonConvert.DeserializeObject<T>(body, _jsonSettings) ??
					throw new InvalidDataException(
						"Avantis API returned an empty response.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Avantis API returned an unexpected response shape.", error);
			}
		}
	}

	private static Uri CreateUri(string endpoint, string name)
	{
		endpoint = endpoint.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Avantis API endpoint must use HTTP or HTTPS.", name);
		return uri;
	}

	private static Uri Combine(Uri endpoint, string relative)
		=> new(endpoint.AbsoluteUri.TrimEnd('/') + "/" +
			relative.TrimStart('/'), UriKind.Absolute);

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
				"Avantis API response exceeds the 16 MiB safety limit.");
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
					"Avantis API response exceeds the 16 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
