namespace StockSharp.KyberSwap.Native;

sealed class KyberSwapHttpClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly string _chain;
	private readonly string _clientId;
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

	public KyberSwapHttpClient(string endpoint, KyberSwapChains chain,
		string clientId)
	{
		if (!System.Enum.IsDefined(chain))
			throw new ArgumentOutOfRangeException(nameof(chain), chain,
				"Unsupported KyberSwap chain.");
		_chain = chain.GetApiName();
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') +
			"/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"KyberSwap API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_clientId = NormalizeClientId(clientId);
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"x-client-id", _clientId);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-KyberSwap-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "KyberSwap_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<KyberSwapRouteData> GetRouteAsync(string source,
		string destination, BigInteger amount, string origin,
		CancellationToken cancellationToken)
	{
		source = source.NormalizeAddress();
		destination = destination.NormalizeAddress();
		if (source.EqualsIgnoreCase(destination))
			throw new ArgumentException(
				"KyberSwap route tokens must be different.",
				nameof(destination));
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (!origin.IsEmpty())
			origin = origin.NormalizeAddress();

		var query = $"{_chain}/api/v1/routes?tokenIn={Escape(source)}" +
			$"&tokenOut={Escape(destination)}&amountIn={Escape(amount.ToString(
				CultureInfo.InvariantCulture))}";
		if (!origin.IsEmpty())
			query += "&origin=" + Escape(origin);
		var response = await SendAsync<KyberSwapRouteResponse>(HttpMethod.Get,
			query, null, cancellationToken);
		ValidateEnvelope(response?.Code, response?.Message, response?.RequestId,
			response?.Data);
		return response.Data;
	}

	public async ValueTask<KyberSwapBuildData> BuildRouteAsync(
		JObject routeSummary, string walletAddress, decimal slippageBps,
		DateTime deadline, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(routeSummary);
		walletAddress = walletAddress.NormalizeAddress();
		if (slippageBps is < 0 or > 2000)
			throw new ArgumentOutOfRangeException(nameof(slippageBps));
		deadline = deadline.ToUniversalTime();
		if (deadline <= DateTime.UtcNow ||
			deadline > DateTime.UtcNow.AddHours(1))
			throw new ArgumentOutOfRangeException(nameof(deadline));
		var request = new KyberSwapBuildRequest
		{
			RouteSummary = routeSummary,
			Sender = walletAddress,
			Origin = walletAddress,
			Recipient = walletAddress,
			Deadline = new DateTimeOffset(deadline).ToUnixTimeSeconds(),
			SlippageTolerance = slippageBps,
			IsGasEstimationEnabled = false,
			Source = _clientId,
		};
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		var response = await SendAsync<KyberSwapBuildResponse>(HttpMethod.Post,
			$"{_chain}/api/v1/route/build", body, cancellationToken);
		ValidateEnvelope(response?.Code, response?.Message, response?.RequestId,
			response?.Data);
		return response.Data;
	}

	private static void ValidateEnvelope<TData>(int? code, string message,
		string requestId, TData data)
	{
		if (code == 0 && data is not null)
			return;
		var detail = message.IsEmpty() ? "request rejected" : message.Trim();
		if (!requestId.IsEmpty())
			detail += $" (request {requestId})";
		throw new InvalidOperationException(
			$"KyberSwap API code {code?.ToString(
				CultureInfo.InvariantCulture) ?? "unknown"}: {detail}");
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
						"KyberSwap API returned an empty JSON value.")
					: result;
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"KyberSwap API returned an unexpected response shape.",
					error);
			}
		}
	}

	private KyberSwapApiException CreateApiException(
		HttpStatusCode statusCode, string body)
	{
		KyberSwapApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<KyberSwapApiError>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message;
		if (detail.IsEmpty() && error?.Details is not null)
			detail = error.Details.ToString(Formatting.None);
		if (detail.IsEmpty())
			detail = body?.Trim().Truncate(512, string.Empty);
		if (detail.IsEmpty())
			detail = "request rejected";
		if (error?.Code is int code)
			detail += $" (code {code})";
		if (error?.RequestId.IsEmpty() == false)
			detail += $" (request {error.RequestId})";
		return new(statusCode,
			$"KyberSwap HTTP {(int)statusCode}: {detail}");
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

	private static string NormalizeClientId(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (value.Length > 64 || value.Any(static ch =>
			!char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.'))
			throw new ArgumentException(
				"KyberSwap client id must contain at most 64 letters, digits, " +
				"dots, underscores, or hyphens.", nameof(value));
		return value;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"KyberSwap response exceeds the 8 MiB safety limit.");
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
					"KyberSwap response exceeds the 8 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}
