namespace StockSharp.Pendle.Native;

sealed class PendleHttpClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly PendleChains _chain;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;

	public PendleHttpClient(string endpoint, PendleChains chain)
	{
		if (!System.Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported Pendle chain.");
		_chain = chain;
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
			"/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Pendle API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Pendle-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Pendle_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask VerifyAsync(CancellationToken cancellationToken)
	{
		var response = await SendAsync<PendleChainsResponse>(HttpMethod.Get,
			"v1/chains", null, cancellationToken);
		if (response.ChainIds?.Contains((int)_chain) != true)
			throw new InvalidOperationException(
				$"Pendle API does not list chain {(int)_chain} as supported.");
	}

	public async ValueTask<PendleApiMarket[]> GetMarketsAsync(
		IReadOnlyCollection<string> addresses, int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum <= 0)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var ids = addresses is { Count: > 0 }
			? string.Join(',', addresses.Select(address =>
				$"{(int)_chain}-{address.NormalizeAddress()}"))
			: null;
		var result = new List<PendleApiMarket>();
		for (var skip = 0; result.Count < maximum;)
		{
			var limit = Math.Min(100, maximum - result.Count);
			var path = "v2/markets/all?chainId=" +
				((int)_chain).ToString(CultureInfo.InvariantCulture) +
				"&isActive=true&order_by=liquidity:-1&limit=" +
				limit.ToString(CultureInfo.InvariantCulture) + "&skip=" +
				skip.ToString(CultureInfo.InvariantCulture);
			if (!ids.IsEmpty())
				path += "&ids=" + Escape(ids);
			var page = await SendAsync<PendleMarketsResponse>(HttpMethod.Get,
				path, null, cancellationToken);
			var markets = page.Results ?? [];
			foreach (var market in markets)
			{
				if (market is null || market.ChainId != (int)_chain)
					continue;
				result.Add(market);
				if (result.Count >= maximum)
					break;
			}
			skip += markets.Length;
			if (markets.Length == 0 || skip >= page.Total ||
				!ids.IsEmpty())
				break;
		}
		return [.. result];
	}

	public async ValueTask<PendleApiAsset[]> GetAssetsAsync(
		IEnumerable<string> addresses, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(addresses);
		var normalized = addresses.Select(static address =>
				address.NormalizeAddress())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		var result = new List<PendleApiAsset>();
		foreach (var chunk in normalized.Chunk(30))
		{
			var ids = string.Join(',', chunk.Select(address =>
				$"{(int)_chain}-{address}"));
			var response = await SendAsync<PendleAssetsResponse>(
				HttpMethod.Get,
				"v1/assets/all?chainId=" +
					((int)_chain).ToString(CultureInfo.InvariantCulture) +
					"&limit=30&ids=" + Escape(ids),
				null, cancellationToken);
			result.AddRange((response.Assets ?? []).Where(
				asset => asset?.ChainId == (int)_chain));
		}
		return [.. result];
	}

	public ValueTask<PendlePricesResponse> GetPricesAsync(string market,
		CancellationToken cancellationToken)
		=> SendAsync<PendlePricesResponse>(HttpMethod.Get,
			$"v1/sdk/{(int)_chain}/markets/" +
				$"{Escape(market.NormalizeAddress())}/swapping-prices",
			null, cancellationToken);

	public async ValueTask<PendleHistoricalPoint[]> GetHistoryAsync(
		string market, TimeSpan timeFrame, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));
		var frame = timeFrame == TimeSpan.FromHours(1)
			? "hour"
			: timeFrame == TimeSpan.FromDays(1)
				? "day"
				: timeFrame == TimeSpan.FromDays(7)
					? "week"
					: throw new ArgumentOutOfRangeException(nameof(timeFrame),
						timeFrame, "Unsupported Pendle time frame.");
		var path = $"v3/{(int)_chain}/markets/" +
			$"{Escape(market.NormalizeAddress())}/historical-data" +
			"?time_frame=" + frame +
			"&timestamp_start=" + Escape(from.ToUniversalTime().ToString(
				"O", CultureInfo.InvariantCulture)) +
			"&timestamp_end=" + Escape(to.ToUniversalTime().ToString(
				"O", CultureInfo.InvariantCulture)) +
			"&fields=timestamp,ptPrice,ytPrice,syPrice,impliedApy," +
				"tradingVolume";
		var response = await SendAsync<PendleHistoricalResponse>(
			HttpMethod.Get, path, null, cancellationToken);
		return response.Results ?? [];
	}

	public ValueTask<PendleConvertResponse> BuildConvertAsync(string input,
		string output, BigInteger amount, string receiver, decimal slippage,
		CancellationToken cancellationToken)
	{
		input = input.NormalizeAddress();
		output = output.NormalizeAddress();
		receiver = receiver.NormalizeAddress();
		if (input.EqualsIgnoreCase(output))
			throw new ArgumentException(
				"Pendle conversion tokens must be different.",
				nameof(output));
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (slippage is <= 0 or > 1)
			throw new ArgumentOutOfRangeException(nameof(slippage));
		var request = new PendleConvertRequest
		{
			Receiver = receiver,
			Slippage = slippage,
			EnableAggregator = false,
			Inputs =
			[
				new()
				{
					Token = input,
					Amount = amount.ToString(CultureInfo.InvariantCulture),
				},
			],
			Outputs = [output],
		};
		return SendAsync<PendleConvertResponse>(HttpMethod.Post,
			$"v3/sdk/{(int)_chain}/convert",
			JsonConvert.SerializeObject(request, _jsonSettings),
			cancellationToken);
	}

	private async ValueTask<TResult> SendAsync<TResult>(HttpMethod method,
		string path, string body, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(method);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, path));
			if (!body.IsEmpty())
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var responseBody = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (attempt < 3 && (response.StatusCode == (HttpStatusCode)429 ||
				(int)response.StatusCode >= 500))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromSeconds(1 << attempt);
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(8)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateApiException(response.StatusCode, responseBody);
			try
			{
				var result = JsonConvert.DeserializeObject<TResult>(
					responseBody, _jsonSettings);
				return result is null
					? throw new InvalidDataException(
						"Pendle API returned an empty JSON value.")
					: result;
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Pendle API returned an unexpected response shape.",
					error);
			}
		}
	}

	private PendleApiException CreateApiException(HttpStatusCode statusCode,
		string body)
	{
		PendleApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<PendleApiError>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message?.Type == JTokenType.Array
			? string.Join("; ", error.Message.Values<string>())
			: error?.Message?.Value<string>();
		if (detail.IsEmpty())
			detail = error?.Error;
		if (detail.IsEmpty())
			detail = body?.Trim().Truncate(512, string.Empty);
		if (detail.IsEmpty())
			detail = "request rejected";
		return new(statusCode,
			$"Pendle HTTP {(int)statusCode}: {detail}");
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(150);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Pendle response exceeds the 16 MiB safety limit.");
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
					"Pendle response exceeds the 16 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}
