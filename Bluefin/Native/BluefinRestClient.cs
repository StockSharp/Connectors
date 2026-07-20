namespace StockSharp.Bluefin.Native;

sealed class BluefinRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;

	private readonly HttpClient _exchangeClient;
	private readonly HttpClient _tradeClient;
	private readonly HttpClient _authClient;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _sync = new();
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private string _accessToken;
	private bool _isDisposed;

	public BluefinRestClient(string exchangeEndpoint, string tradeEndpoint,
		string authEndpoint)
	{
		_exchangeClient = CreateClient(exchangeEndpoint, "ExchangeEndpoint");
		_tradeClient = CreateClient(tradeEndpoint, "TradeEndpoint");
		_authClient = CreateClient(authEndpoint, "AuthEndpoint");
	}

	public override string Name => "Bluefin_REST";

	public string AccessToken
	{
		get
		{
			using (_sync.EnterScope())
				return _accessToken;
		}
	}

	public void SetAccessToken(string accessToken)
	{
		using (_sync.EnterScope())
			_accessToken = accessToken;
	}

	public ValueTask<BluefinExchangeInfo> GetExchangeInfoAsync(
		CancellationToken cancellationToken)
		=> SendAsync<BluefinExchangeInfo>(_exchangeClient, HttpMethod.Get,
			"v1/exchange/info", null, false, true, false, cancellationToken);

	public ValueTask<BluefinTicker> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<BluefinTicker>(_exchangeClient, HttpMethod.Get,
			"v1/exchange/ticker?symbol=" + Escape(symbol), null, false, true,
			false, cancellationToken);

	public ValueTask<BluefinDepth> GetDepthAsync(string symbol, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		return SendAsync<BluefinDepth>(_exchangeClient, HttpMethod.Get,
			"v1/exchange/depth?symbol=" + Escape(symbol) + "&limit=" +
			limit.ToString(CultureInfo.InvariantCulture), null, false, true,
			false, cancellationToken);
	}

	public ValueTask<BluefinTrade[]> GetTradesAsync(string symbol,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = "v1/exchange/trades?symbol=" + Escape(symbol) + "&limit=" +
			limit.ToString(CultureInfo.InvariantCulture);
		if (from is DateTime start)
			path += "&startTimeAtMillis=" + start.ToBluefinMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&endTimeAtMillis=" + end.ToBluefinMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		return SendAsync<BluefinTrade[]>(_exchangeClient, HttpMethod.Get, path,
			null, false, true, false, cancellationToken);
	}

	public ValueTask<string[][]> GetCandlesAsync(string symbol,
		string interval, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = "v1/exchange/candlesticks?symbol=" + Escape(symbol) +
			"&interval=" + Escape(interval) + "&type=LAST&limit=" +
			limit.ToString(CultureInfo.InvariantCulture);
		if (from is DateTime start)
			path += "&startTimeAtMillis=" + start.ToBluefinMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&endTimeAtMillis=" + end.ToBluefinMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		return SendAsync<string[][]>(_exchangeClient, HttpMethod.Get, path, null,
			false, true, false, cancellationToken);
	}

	public ValueTask<BluefinLoginResponse> AuthenticateAsync(
		BluefinLoginRequest request, string signature,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		signature = signature.ThrowIfEmpty(nameof(signature));
		return SendAsync<BluefinLoginResponse>(_authClient, HttpMethod.Post,
			"auth/v2/token", request, false, false, false, cancellationToken,
			message => message.Headers.TryAddWithoutValidation("payloadSignature",
				signature));
	}

	public ValueTask<BluefinAccount> GetAccountAsync(string accountAddress,
		CancellationToken cancellationToken)
	{
		var path = "api/v1/account";
		if (!accountAddress.IsEmpty())
			path += "?accountAddress=" + Escape(accountAddress);
		return SendAsync<BluefinAccount>(_exchangeClient, HttpMethod.Get, path,
			null, false, true, false, cancellationToken);
	}

	public ValueTask<BluefinTrade[]> GetAccountTradesAsync(string symbol,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = "api/v1/account/trades?limit=" +
			limit.ToString(CultureInfo.InvariantCulture);
		if (!symbol.IsEmpty())
			path += "&symbol=" + Escape(symbol);
		if (from is DateTime start)
			path += "&startTimeAtMillis=" + start.ToBluefinMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&endTimeAtMillis=" + end.ToBluefinMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		return SendAsync<BluefinTrade[]>(_exchangeClient, HttpMethod.Get, path,
			null, true, true, false, cancellationToken);
	}

	public ValueTask<BluefinOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var path = "api/v1/trade/openOrders";
		if (!symbol.IsEmpty())
			path += "?symbol=" + Escape(symbol);
		return SendAsync<BluefinOrder[]>(_tradeClient, HttpMethod.Get, path, null,
			true, true, false, cancellationToken);
	}

	public ValueTask<BluefinCreateOrderResponse> CreateOrderAsync(
		BluefinCreateOrderRequest request, CancellationToken cancellationToken)
		=> SendAsync<BluefinCreateOrderResponse>(_tradeClient, HttpMethod.Post,
			"api/v1/trade/orders", request, true, false, false,
			cancellationToken);

	public async ValueTask CancelOrdersAsync(BluefinCancelOrdersRequest request,
		CancellationToken cancellationToken)
	{
		_ = await SendAsync<BluefinApiError>(_tradeClient, HttpMethod.Put,
			"api/v1/trade/orders/cancel", request, true, false, true,
			cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpClient client,
		HttpMethod method, string path, object body, bool isPrivate,
		bool isRetryAllowed, bool isEmptyAllowed,
		CancellationToken cancellationToken,
		Action<HttpRequestMessage> configure = null)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		var json = body is null
			? string.Empty
			: JsonConvert.SerializeObject(body, Formatting.None, _settings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (!json.IsEmpty())
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			if (isPrivate)
			{
				var token = AccessToken;
				if (token.IsEmpty())
					throw new InvalidOperationException(
						"Bluefin authentication is required for this operation.");
				request.Headers.TryAddWithoutValidation("Authorization",
					"Bearer " + token);
			}
			configure?.Invoke(request);
			using var response = await client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (isRetryAllowed && attempt < 2 &&
				(response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			var responseBody = await ReadBodyAsync(response.Content, path,
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw CreateException(response.StatusCode, responseBody);
			if (responseBody.IsEmpty() && isEmptyAllowed)
				return default;
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(responseBody,
					_settings) ?? throw new BluefinApiException(
						"Bluefin returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new BluefinApiException(
					"Bluefin returned malformed JSON.", error);
			}
		}
	}

	private async ValueTask WaitAsync(CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static HttpClient CreateClient(string endpoint, string name)
	{
		var client = new HttpClient
		{
			BaseAddress = endpoint.NormalizeHttpEndpoint(name),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Bluefin/1.0");
		return client;
	}

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Bluefin history limit must be between one and 1000.");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static BluefinApiException CreateException(HttpStatusCode status,
		string body)
	{
		BluefinApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<BluefinApiError>(body);
		}
		catch (JsonException)
		{
		}
		var message = error?.Message ?? error?.Error ?? error?.Reason;
		if (message.IsEmpty())
			message = body.IsEmpty()
				? "empty response"
				: body[..Math.Min(body.Length, 1024)];
		return new($"Bluefin HTTP {(int)status} ({status}): {message}");
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		string path, CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new BluefinApiException(
				$"Bluefin response for '{path}' exceeds the safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseLength)
				throw new BluefinApiException(
					$"Bluefin response for '{path}' exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_exchangeClient.Dispose();
		_tradeClient.Dispose();
		_authClient.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
