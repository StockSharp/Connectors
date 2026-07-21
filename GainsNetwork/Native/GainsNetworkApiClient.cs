namespace StockSharp.GainsNetwork.Native;

sealed class GainsNetworkApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private readonly Uri _backendEndpoint;
	private readonly Uri _globalEndpoint;
	private readonly Uri _pricingEndpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;

	public GainsNetworkApiClient(string backendEndpoint, string globalEndpoint,
		string pricingEndpoint)
	{
		_backendEndpoint = CreateEndpoint(backendEndpoint,
			nameof(backendEndpoint));
		_globalEndpoint = CreateEndpoint(globalEndpoint, nameof(globalEndpoint));
		_pricingEndpoint = CreateEndpoint(pricingEndpoint,
			nameof(pricingEndpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-GainsNetwork-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "GainsNetwork_HTTP";

	public ValueTask<GainsTradingVariables> GetTradingVariablesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<GainsTradingVariables>(CreateUri(_backendEndpoint,
			"trading-variables"), cancellationToken);

	public ValueTask<GainsCharts> GetChartsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<GainsCharts>(CreateUri(_pricingEndpoint, "charts"),
			cancellationToken);

	public ValueTask<GainsTradeContainer[]> GetOpenTradesAsync(string address,
		CancellationToken cancellationToken)
		=> SendAsync<GainsTradeContainer[]>(CreateUri(_backendEndpoint,
			"open-trades/" + Uri.EscapeDataString(address.NormalizeAddress())),
			cancellationToken);

	public ValueTask<GainsUserTradingVariables> GetUserVariablesAsync(
		string address, CancellationToken cancellationToken)
		=> SendAsync<GainsUserTradingVariables>(CreateUri(_backendEndpoint,
			"user-trading-variables/" +
			Uri.EscapeDataString(address.NormalizeAddress())), cancellationToken);

	public async ValueTask<GainsHistoryItem[]> GetHistoryAsync(string address,
		long chainId, int limit, DateTime? from, DateTime? to, string pair,
		CancellationToken cancellationToken)
	{
		if (chainId <= 0)
			throw new ArgumentOutOfRangeException(nameof(chainId));
		if (limit <= 0)
			return [];
		address = address.NormalizeAddress();
		from = from?.EnsureUtc();
		to = to?.EnsureUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));

		var result = new List<GainsHistoryItem>(limit.Min(1000));
		long? cursor = null;
		for (var page = 0; page < 100 && result.Count < limit; page++)
		{
			var count = (limit - result.Count).Min(1000);
			var resource = new StringBuilder("api/personal-trading-history/")
				.Append(Uri.EscapeDataString(address))
				.Append("?chainId=")
				.Append(chainId.ToString(CultureInfo.InvariantCulture))
				.Append("&limit=")
				.Append(count.ToString(CultureInfo.InvariantCulture));
			if (cursor is long cursorValue)
				resource.Append("&cursor=").Append(cursorValue.ToString(
					CultureInfo.InvariantCulture));
			if (from is DateTime fromValue)
				resource.Append("&startDate=").Append(Uri.EscapeDataString(
					fromValue.ToString("O", CultureInfo.InvariantCulture)));
			if (to is DateTime toValue)
				resource.Append("&endDate=").Append(Uri.EscapeDataString(
					toValue.ToString("O", CultureInfo.InvariantCulture)));
			if (!pair.IsEmpty())
				resource.Append("&pair=").Append(Uri.EscapeDataString(pair));

			var response = await SendAsync<GainsHistoryResponse>(CreateUri(
				_globalEndpoint, resource.ToString()), cancellationToken);
			var items = (response.Items ?? [])
				.Where(static item => item is not null)
				.ToArray();
			result.AddRange(items);
			if (response.Pagination?.IsMoreAvailable != true ||
				response.Pagination.NextCursor is not long nextCursor ||
				nextCursor == cursor || items.Length == 0)
				break;
			cursor = nextCursor;
		}
		return [.. result.Take(limit)];
	}

	private async ValueTask<T> SendAsync<T>(Uri endpoint,
		CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
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
			{
				var error = TryDeserialize<GainsErrorResponse>(body);
				var message = error?.Message.IsEmpty() == false
					? error.Message
					: error?.Error.IsEmpty() == false ? error.Error : Truncate(body);
				throw new InvalidOperationException("Gains HTTP " +
					(int)response.StatusCode + ": " + message);
			}
			return Deserialize<T>(body);
		}
	}

	private async ValueTask WaitForRequestAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private T Deserialize<T>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(body, _settings) ??
				throw new InvalidDataException(
					"Gains returned an empty JSON response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Gains returned an unexpected response shape.", error);
		}
	}

	private T TryDeserialize<T>(string body)
		where T : class
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(body, _settings);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Gains endpoint must use HTTP or HTTPS.", parameterName);
		return uri;
	}

	private static Uri CreateUri(Uri endpoint, string resource)
		=> new(endpoint, resource.TrimStart('/'));

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
				"Gains response exceeds the 32 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var block = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(block, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Gains response exceeds the 32 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}
}
