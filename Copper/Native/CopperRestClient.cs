namespace StockSharp.Copper.Native;

sealed class CopperApiException : InvalidOperationException
{
	public CopperApiException(HttpStatusCode statusCode, string code,
		string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public string Code { get; }
}

sealed class CopperRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private static readonly TimeSpan _minimumRequestInterval =
		TimeSpan.FromMilliseconds(300);
	private readonly HttpClient _client;
	private readonly string _apiKey;
	private readonly byte[] _secret;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _lastRequestTime;
	private bool _isDisposed;

	public CopperRestClient(string endpoint, string apiKey,
		SecureString apiSecret)
	{
		endpoint = endpoint.NormalizeCopperEndpoint().ThrowIfEmpty(
			nameof(endpoint));
		_apiKey = apiKey.ThrowIfEmpty(nameof(apiKey)).Trim();
		if (apiSecret.IsEmpty())
			throw new ArgumentNullException(nameof(apiSecret));
		_secret = Encoding.UTF8.GetBytes(apiSecret.UnSecure());
		_client = new()
		{
			BaseAddress = new(endpoint + "/", UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.Accept.Add(new("application/json"));
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Copper/1.0");
	}

	public override string Name => "Copper_REST";

	public ValueTask<CopperCurrency[]> GetCurrenciesAsync(
		CancellationToken cancellationToken)
		=> GetArrayAsync<CopperCurrenciesResponse, CopperCurrency>("currencies",
			static response => response?.Currencies ?? [], cancellationToken);

	public async ValueTask<CopperPortfolio[]> GetPortfoliosAsync(int pageSize,
		int maximum, CancellationToken cancellationToken)
	{
		ValidatePage(pageSize, maximum);
		var result = new List<CopperPortfolio>();
		while (result.Count < maximum)
		{
			var take = Math.Min(pageSize, maximum - result.Count);
			var path = "portfolios?isActive=true&limit=" + Format(take) +
				"&offset=" + Format(result.Count);
			var page = await GetAsync<CopperPortfoliosResponse>(path,
				cancellationToken);
			var items = page?.Portfolios ?? [];
			result.AddRange(items);
			if (items.Length < take)
				break;
		}
		return [.. result.Take(maximum)];
	}

	public async ValueTask<CopperWallet[]> GetWalletsAsync(int pageSize,
		int maximum, CancellationToken cancellationToken)
	{
		ValidatePage(pageSize, maximum);
		var result = new List<CopperWallet>();
		while (result.Count < maximum)
		{
			var take = Math.Min(pageSize, maximum - result.Count);
			var path = "wallets?nonEmpty=false&limit=" + Format(take) +
				"&offset=" + Format(result.Count);
			var page = await GetAsync<CopperWalletsResponse>(path,
				cancellationToken);
			var items = page?.Wallets ?? [];
			result.AddRange(items);
			if (items.Length < take)
				break;
		}
		return [.. result.Take(maximum)];
	}

	public async ValueTask<CopperClearLoopPortfolio[]>
		TryGetClearLoopPortfoliosAsync(CancellationToken cancellationToken)
	{
		try
		{
			var response = await GetAsync<CopperClearLoopPortfoliosResponse>(
				"clearloop/portfolios", cancellationToken);
			return response?.Portfolios ?? [];
		}
		catch (CopperApiException error) when (error.StatusCode is
			HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
		{
			return [];
		}
	}

	public async ValueTask<CopperClearLoopBalance[]>
		TryGetClearLoopBalancesAsync(CancellationToken cancellationToken)
	{
		try
		{
			var response = await GetAsync<CopperClearLoopBalancesResponse>(
				"clearloop/balances?nonEmpty=false&fetchSubaccounts=true",
				cancellationToken);
			return response?.Balances ?? [];
		}
		catch (CopperApiException error) when (error.StatusCode is
			HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
		{
			return [];
		}
	}

	public ValueTask<CopperOrder> CreateOrderAsync(
		CopperCreateOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendJsonAsync<CopperOrder, CopperCreateOrderRequest>(
			HttpMethod.Post, "orders", request, "application/json", false,
			cancellationToken);
	}

	public ValueTask<CopperOrder> GetOrderAsync(string id,
		CancellationToken cancellationToken)
		=> GetAsync<CopperOrder>("orders/" + Escape(
			id.ThrowIfEmpty(nameof(id))), cancellationToken);

	public async ValueTask<CopperOrder> TryGetOrderByExternalIdAsync(
		string externalId, CancellationToken cancellationToken)
	{
		var path = "orders?limit=2&offset=0&externalOrderId=" + Escape(
			externalId.ThrowIfEmpty(nameof(externalId)));
		var response = await GetAsync<CopperOrdersResponse>(path,
			cancellationToken);
		return response?.Orders?.FirstOrDefault(order =>
			order.ExternalOrderId.EqualsIgnoreCase(externalId));
	}

	public async ValueTask<CopperOrder[]> GetOrdersAsync(DateTime? from,
		DateTime? to, string portfolioId, string currency, int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum is < 1 or > 10000)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var result = new List<CopperOrder>();
		while (result.Count < maximum)
		{
			var take = Math.Min(1000, maximum - result.Count);
			var path = "orders?limit=" + Format(take) + "&offset=" +
				Format(result.Count);
			if (from is DateTime start)
				path += "&createdAtFrom=" + Format(
					start.ToCopperMilliseconds());
			if (to is DateTime end)
				path += "&createdAtTo=" + Format(end.ToCopperMilliseconds());
			if (!portfolioId.IsEmpty())
				path += "&portfolioIds=" + Escape(portfolioId);
			if (!currency.IsEmpty())
				path += "&baseCurrencies=" + Escape(currency);
			var page = await GetAsync<CopperOrdersResponse>(path,
				cancellationToken);
			var items = page?.Orders ?? [];
			result.AddRange(items);
			if (items.Length < take)
				break;
		}
		return [.. result.Take(maximum)];
	}

	public ValueTask<CopperOrder> CancelOrderAsync(string id, string reason,
		CancellationToken cancellationToken)
		=> SendJsonAsync<CopperOrder, CopperCancelOrderRequest>(
			HttpMethod.Patch,
			"orders/" + Escape(id.ThrowIfEmpty(nameof(id))),
			new() { Reason = reason }, "application/vnd.cancel-order+json",
			false, cancellationToken);

	private async ValueTask<TItem[]> GetArrayAsync<TResponse, TItem>(string path,
		Func<TResponse, TItem[]> selector, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(selector);
		return selector(await GetAsync<TResponse>(path, cancellationToken));
	}

	private ValueTask<TResponse> GetAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(HttpMethod.Get, path, [], null, true,
			cancellationToken);

	private ValueTask<TResponse> SendJsonAsync<TResponse, TRequest>(
		HttpMethod method, string path, TRequest request, string contentType,
		bool isRetryAllowed, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var json = JsonConvert.SerializeObject(request, _settings);
		return SendAsync<TResponse>(method, path, Encoding.UTF8.GetBytes(json),
			contentType, isRetryAllowed, cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, byte[] body, string contentType, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		await _gate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				await EnforceRequestIntervalAsync(cancellationToken);
				var uri = new Uri(_client.BaseAddress, path);
				var timestamp = Format(checked((long)(DateTime.UtcNow -
					DateTime.UnixEpoch).TotalMilliseconds));
				using var request = new HttpRequestMessage(method, path);
				request.Headers.TryAddWithoutValidation("Authorization",
					"ApiKey " + _apiKey);
				request.Headers.TryAddWithoutValidation("X-Timestamp", timestamp);
				request.Headers.TryAddWithoutValidation("X-Signature",
					CreateSignature(timestamp, method, uri.PathAndQuery, body));
				if (body.Length > 0)
					request.Content = new ByteArrayContent(body)
					{
						Headers = { ContentType = new(contentType) },
					};

				HttpResponseMessage response;
				try
				{
					response = await _client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
				}
				catch (Exception error) when (isRetryAllowed && attempt < 3 &&
					!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					await DelayRetryAsync(attempt, null, cancellationToken);
					continue;
				}
				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var bytes = await ReadResponseAsync(response,
						cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						return JsonConvert.DeserializeObject<TResponse>(
							Encoding.UTF8.GetString(bytes), _settings);
					}
					if (isRetryAllowed && attempt < 3 &&
						IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, bytes);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async ValueTask EnforceRequestIntervalAsync(
		CancellationToken cancellationToken)
	{
		var remaining = _minimumRequestInterval -
			(DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ?? TimeSpan.FromMilliseconds(300 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private string CreateSignature(string timestamp, HttpMethod method,
		string pathAndQuery, byte[] body)
	{
		var prefix = timestamp + method.Method.ToUpperInvariant() + pathAndQuery;
		var prefixBytes = Encoding.UTF8.GetBytes(prefix);
		var data = new byte[prefixBytes.Length + body.Length];
		Buffer.BlockCopy(prefixBytes, 0, data, 0, prefixBytes.Length);
		Buffer.BlockCopy(body, 0, data, prefixBytes.Length, body.Length);
		try
		{
			return Convert.ToHexString(HMACSHA256.HashData(_secret, data))
				.ToLowerInvariant();
		}
		finally
		{
			CryptographicOperations.ZeroMemory(data);
		}
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Copper response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Copper response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private CopperApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		CopperErrorResponse error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<CopperErrorResponse>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var message = error?.Message;
		if (message.IsEmpty())
			message = $"Copper API returned HTTP {(int)statusCode} " +
				$"({statusCode}).";
		else
			message = $"Copper API returned HTTP {(int)statusCode}: {message}";
		return new(statusCode, error?.Error, message);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private static void ValidatePage(int pageSize, int maximum)
	{
		if (pageSize is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		if (maximum < 1)
			throw new ArgumentOutOfRangeException(nameof(maximum));
	}

	private static string Format(long value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		CryptographicOperations.ZeroMemory(_secret);
		base.DisposeManaged();
	}
}
