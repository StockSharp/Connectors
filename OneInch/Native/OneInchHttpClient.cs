namespace StockSharp.OneInch.Native;

sealed class OneInchHttpClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly OneInchChains _chain;
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

	public OneInchHttpClient(string endpoint, OneInchChains chain,
		SecureString apiKey)
	{
		if (!System.Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported 1inch chain.");
		_chain = chain;
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
			"/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"1inch API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		var key = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
		if (key.IsEmpty())
			throw new ArgumentException(
				"A 1inch Business Portal API key is required.", nameof(apiKey));
		_http.DefaultRequestHeaders.Authorization = new("Bearer", key);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-1inch-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "1inch_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<string> GetSpenderAsync(
		CancellationToken cancellationToken)
	{
		var response = await SendAsync<OneInchSpenderResponse>(
			"approve/spender", cancellationToken);
		return response.Address.NormalizeAddress();
	}

	public ValueTask<OneInchQuoteResponse> GetQuoteAsync(string source,
		string destination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		source = source.NormalizeAddress();
		destination = destination.NormalizeAddress();
		if (source.EqualsIgnoreCase(destination))
			throw new ArgumentException(
				"1inch quote tokens must be different.", nameof(destination));
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var query = "quote?src=" + Escape(source) +
			"&dst=" + Escape(destination) +
			"&amount=" + Escape(amount.ToString(CultureInfo.InvariantCulture)) +
			"&includeTokensInfo=true&includeGas=true";
		return SendAsync<OneInchQuoteResponse>(query, cancellationToken);
	}

	public ValueTask<OneInchSwapResponse> GetSwapAsync(string source,
		string destination, BigInteger amount, string walletAddress,
		decimal slippageTolerance, CancellationToken cancellationToken)
	{
		source = source.NormalizeAddress();
		destination = destination.NormalizeAddress();
		walletAddress = walletAddress.NormalizeAddress();
		if (source.EqualsIgnoreCase(destination))
			throw new ArgumentException(
				"1inch swap tokens must be different.", nameof(destination));
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (slippageTolerance is <= 0 or > 50)
			throw new ArgumentOutOfRangeException(nameof(slippageTolerance));
		var query = "swap?src=" + Escape(source) +
			"&dst=" + Escape(destination) +
			"&amount=" + Escape(amount.ToString(CultureInfo.InvariantCulture)) +
			"&from=" + Escape(walletAddress) +
			"&origin=" + Escape(walletAddress) +
			"&receiver=" + Escape(walletAddress) +
			"&slippage=" + Escape(slippageTolerance.ToString(
				"0.##", CultureInfo.InvariantCulture)) +
			"&allowPartialFill=false&disableEstimate=false" +
			"&includeTokensInfo=true&includeGas=true";
		return SendAsync<OneInchSwapResponse>(query, cancellationToken);
	}

	private async ValueTask<TResult> SendAsync<TResult>(string path,
		CancellationToken cancellationToken)
	{
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		var relative = $"{(int)_chain}/{path}";
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, relative));
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
						"1inch API returned an empty JSON value.")
					: result;
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"1inch API returned an unexpected response shape.", error);
			}
		}
	}

	private OneInchApiException CreateApiException(HttpStatusCode statusCode,
		string body)
	{
		OneInchApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<OneInchApiError>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Description;
		if (detail.IsEmpty())
			detail = error?.Error;
		if (detail.IsEmpty())
			detail = body?.Trim().Truncate(512, string.Empty);
		if (detail.IsEmpty())
			detail = "request rejected";
		if (error?.RequestId.IsEmpty() == false)
			detail += $" (request {error.RequestId})";
		return new(statusCode, $"1inch HTTP {(int)statusCode}: {detail}");
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
				"1inch response exceeds the 8 MiB safety limit.");
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
					"1inch response exceeds the 8 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}
