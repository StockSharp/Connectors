namespace StockSharp.ZeroX.Native;

sealed class ZeroXHttpClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly ZeroXChains _chain;
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

	public ZeroXHttpClient(string endpoint, ZeroXChains chain,
		SecureString apiKey)
	{
		if (!System.Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported 0x chain.");
		_chain = chain;
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
			"/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"0x API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		var key = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
		if (key.IsEmpty())
			throw new ArgumentException(
				"A 0x API key is required.", nameof(apiKey));
		_http.DefaultRequestHeaders.TryAddWithoutValidation("0x-api-key", key);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("0x-version", "v2");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-0x-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "0x_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<ZeroXQuoteResponse> GetPriceAsync(string source,
		string destination, BigInteger amount, string taker,
		CancellationToken cancellationToken)
		=> SendSwapAsync("price", source, destination, amount, taker, null,
			cancellationToken);

	public ValueTask<ZeroXQuoteResponse> GetQuoteAsync(string source,
		string destination, BigInteger amount, string taker,
		decimal slippageTolerance, CancellationToken cancellationToken)
	{
		if (slippageTolerance is < 0 or > 100)
			throw new ArgumentOutOfRangeException(nameof(slippageTolerance));
		var bps = checked((int)decimal.Round(slippageTolerance * 100m, 0,
			MidpointRounding.AwayFromZero));
		return SendSwapAsync("quote", source, destination, amount, taker, bps,
			cancellationToken);
	}

	private ValueTask<ZeroXQuoteResponse> SendSwapAsync(string operation,
		string source, string destination, BigInteger amount, string taker,
		int? slippageBps, CancellationToken cancellationToken)
	{
		source = source.NormalizeAddress();
		destination = destination.NormalizeAddress();
		if (source.EqualsIgnoreCase(destination))
			throw new ArgumentException(
				"0x quote tokens must be different.", nameof(destination));
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (!taker.IsEmpty())
			taker = taker.NormalizeAddress();

		var query = "swap/allowance-holder/" + operation +
			"?chainId=" + ((int)_chain).ToString(CultureInfo.InvariantCulture) +
			"&sellToken=" + Escape(source) +
			"&buyToken=" + Escape(destination) +
			"&sellAmount=" + Escape(amount.ToString(
				CultureInfo.InvariantCulture));
		if (!taker.IsEmpty())
			query += "&taker=" + Escape(taker);
		if (slippageBps is int bps)
			query += "&slippageBps=" + bps.ToString(
				CultureInfo.InvariantCulture);
		return SendAsync<ZeroXQuoteResponse>(query, cancellationToken);
	}

	private async ValueTask<TResult> SendAsync<TResult>(string path,
		CancellationToken cancellationToken)
	{
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content,
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
				throw CreateApiException(response.StatusCode, body);
			try
			{
				var result = JsonConvert.DeserializeObject<TResult>(body,
					_jsonSettings);
				return result is null
					? throw new InvalidDataException(
						"0x API returned an empty JSON value.")
					: result;
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"0x API returned an unexpected response shape.", error);
			}
		}
	}

	private ZeroXApiException CreateApiException(HttpStatusCode statusCode,
		string body)
	{
		ZeroXApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<ZeroXApiError>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message;
		if (detail.IsEmpty())
			detail = error?.Reason;
		if (detail.IsEmpty() && error?.ValidationErrors?.Length > 0)
			detail = string.Join("; ", error.ValidationErrors.Select(
				static item => $"{item.Field}: {item.Reason}"));
		if (detail.IsEmpty())
			detail = body?.Trim().Truncate(512, string.Empty);
		if (detail.IsEmpty())
			detail = "request rejected";
		if (error?.Code.IsEmpty() == false)
			detail += $" ({error.Code})";
		return new(statusCode, $"0x HTTP {(int)statusCode}: {detail}");
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(1050);
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
				"0x response exceeds the 8 MiB safety limit.");
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
					"0x response exceeds the 8 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}
