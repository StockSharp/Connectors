namespace StockSharp.Aevo.Native;

sealed class AevoRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private readonly HttpClient _client;
	private readonly AevoAuthenticator _authenticator;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public AevoRestClient(string endpoint, AevoAuthenticator authenticator)
	{
		_authenticator = authenticator ?? throw new ArgumentNullException(
			nameof(authenticator));
		_client = new()
		{
			BaseAddress = new(endpoint.NormalizeHttpEndpoint(nameof(endpoint)),
				UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Aevo/1.0");
	}

	public override string Name => "Aevo_REST";

	public ValueTask<AevoTimeResponse> GetTimeAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoTimeResponse>(HttpMethod.Get, "time", null, false,
			true, cancellationToken);

	public ValueTask<AevoInstrument[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoInstrument[]>(HttpMethod.Get, "markets", null, false,
			true, cancellationToken);

	public ValueTask<AevoInstrument> GetInstrumentAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<AevoInstrument>(HttpMethod.Get,
			"instrument/" + Escape(symbol), null, false, true,
			cancellationToken);

	public ValueTask<AevoOrderBook> GetOrderBookAsync(string symbol,
		CancellationToken cancellationToken)
		=> SendAsync<AevoOrderBook>(HttpMethod.Get,
			"orderbook?instrument_name=" + Escape(symbol), null, false, true,
			cancellationToken);

	public ValueTask<AevoTradesResponse> GetTradesAsync(string symbol,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var path = "instrument/" + Escape(symbol) + "/trade-history";
		var separator = '?';
		if (from is DateTime start)
		{
			path += separator + "start_time=" + start.ToAevoNanoseconds()
				.ToString(CultureInfo.InvariantCulture);
			separator = '&';
		}
		if (to is DateTime end)
			path += separator + "end_time=" + end.ToAevoNanoseconds()
				.ToString(CultureInfo.InvariantCulture);
		return SendAsync<AevoTradesResponse>(HttpMethod.Get, path, null, false,
			true, cancellationToken);
	}

	public ValueTask<AevoSuccessResponse> VerifyAuthenticationAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoSuccessResponse>(HttpMethod.Get, "auth", null, true,
			true, cancellationToken);

	public ValueTask<AevoAccount> GetAccountAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoAccount>(HttpMethod.Get, "account", null, true, true,
			cancellationToken);

	public ValueTask<AevoPortfolio> GetPortfolioAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoPortfolio>(HttpMethod.Get, "portfolio", null, true,
			true, cancellationToken);

	public ValueTask<AevoPositionsResponse> GetPositionsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoPositionsResponse>(HttpMethod.Get, "positions", null,
			true, true, cancellationToken);

	public ValueTask<AevoOrder[]> GetOpenOrdersAsync(
		CancellationToken cancellationToken)
		=> SendAsync<AevoOrder[]>(HttpMethod.Get, "orders", null, true, true,
			cancellationToken);

	public ValueTask<AevoOrderHistoryResponse> GetOrderHistoryAsync(int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		return SendAsync<AevoOrderHistoryResponse>(HttpMethod.Get,
			"order-history?start_time=0&limit=" + limit.ToString(
				CultureInfo.InvariantCulture), null, true, true, cancellationToken);
	}

	public ValueTask<AevoPrivateTradesResponse> GetTradeHistoryAsync(int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		return SendAsync<AevoPrivateTradesResponse>(HttpMethod.Get,
			"trade-history?start_time=0&limit=" + limit.ToString(
				CultureInfo.InvariantCulture), null, true, true, cancellationToken);
	}

	public ValueTask<AevoOrder> CreateOrderAsync(AevoOrderRequest request,
		CancellationToken cancellationToken)
		=> SendAsync<AevoOrder>(HttpMethod.Post, "orders", request, true, false,
			cancellationToken);

	public ValueTask<AevoOrder> ReplaceOrderAsync(string orderId,
		AevoOrderRequest request, CancellationToken cancellationToken)
		=> SendAsync<AevoOrder>(HttpMethod.Post,
			"orders/" + Escape(orderId.NormalizeOrderId()), request, true, false,
			cancellationToken);

	public ValueTask<AevoCancelOrderResponse> CancelOrderAsync(string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<AevoCancelOrderResponse>(HttpMethod.Delete,
			"orders/" + Escape(orderId.NormalizeOrderId()), null, true, false,
			cancellationToken);

	public ValueTask<AevoCancelAllResponse> CancelAllAsync(
		AevoCancelAllRequest request, CancellationToken cancellationToken)
		=> SendAsync<AevoCancelAllResponse>(HttpMethod.Delete, "orders-all",
			request, true, false, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, object body, bool isPrivate, bool isRetryAllowed,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		var json = body is null
			? string.Empty
			: JsonConvert.SerializeObject(body, _settings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (!json.IsEmpty())
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			if (isPrivate)
				_authenticator.AddRestHeaders(request, "/" + path, json);
			using var response = await _client.SendAsync(request,
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
			try
			{
				return JsonConvert.DeserializeObject<TResponse>(responseBody,
					_settings) ?? throw new AevoApiException(
						"Aevo returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new AevoApiException("Aevo returned malformed JSON.", error);
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
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_gate.Release();
		}
	}

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 50)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"Aevo history limit must be between one and 50.");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	private static AevoApiException CreateException(HttpStatusCode status,
		string body)
	{
		AevoApiError error = null;
		try
		{
			error = JsonConvert.DeserializeObject<AevoApiError>(body);
		}
		catch (JsonException)
		{
		}
		var message = error?.Error ?? error?.Message;
		if (message.IsEmpty())
			message = body.IsEmpty()
				? "empty response"
				: body[..Math.Min(body.Length, 1024)];
		return new($"Aevo HTTP {(int)status} ({status}): {message}");
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		string path, CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new AevoApiException(
				$"Aevo response for '{path}' exceeds the safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseLength)
				throw new AevoApiException(
					$"Aevo response for '{path}' exceeds the safety limit.");
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
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
