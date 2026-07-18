namespace StockSharp.Weex.Native;

sealed class WeexRestClient : BaseLogReceiver
{
	private const int _maxAttempts = 4;
	private readonly Uri _spotEndpoint;
	private readonly Uri _futuresEndpoint;
	private readonly HttpClient _http = new();
	private readonly string _apiKey;
	private readonly string _passphrase;
	private readonly HMACSHA256 _hasher;
	private readonly Lock _signSync = new();
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};
	private long _spotTimeOffset;
	private long _futuresTimeOffset;

	public WeexRestClient(string spotEndpoint, string futuresEndpoint, SecureString key,
		SecureString secret, SecureString passphrase)
	{
		_spotEndpoint = new Uri(NormalizeEndpoint(spotEndpoint), UriKind.Absolute);
		_futuresEndpoint = new Uri(NormalizeEndpoint(futuresEndpoint), UriKind.Absolute);
		_apiKey = key.IsEmpty() ? null : key.UnSecure();
		_passphrase = passphrase.IsEmpty() ? null : passphrase.UnSecure();
		_hasher = secret.IsEmpty() ? null : new HMACSHA256(secret.UnSecure().UTF8());
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-WEEX-Connector/1.0");
	}

	public override string Name => nameof(Weex) + "_Rest";

	public bool HasCredentials => !_apiKey.IsEmpty() && _hasher is not null && !_passphrase.IsEmpty();

	protected override void DisposeManaged()
	{
		_hasher?.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask SyncTimeAsync(WeexSections section, CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot ? "/api/v3/time" : "/capi/v3/market/time";
		var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var response = await SendPublicAsync<WeexServerTime>(section, path, null, cancellationToken);
		var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var offset = response.ServerTime - ((before + after) / 2);
		if (section == WeexSections.Spot)
			Volatile.Write(ref _spotTimeOffset, offset);
		else
			Volatile.Write(ref _futuresTimeOffset, offset);
	}

	public ValueTask<WeexSpotExchangeInfo> GetSpotExchangeInfoAsync(CancellationToken cancellationToken)
		=> SendPublicAsync<WeexSpotExchangeInfo>(WeexSections.Spot, "/api/v3/exchangeInfo",
			"symbolStatus=TRADING", cancellationToken);

	public ValueTask<WeexFuturesExchangeInfo> GetFuturesExchangeInfoAsync(CancellationToken cancellationToken)
		=> SendPublicAsync<WeexFuturesExchangeInfo>(WeexSections.Futures,
			"/capi/v3/market/exchangeInfo", null, cancellationToken);

	public async ValueTask<WeexTicker> GetTickerAsync(WeexSections section, string symbol,
		CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot
			? "/api/v3/market/ticker/24hr"
			: "/capi/v3/market/ticker/24hr";
		var query = section == WeexSections.Spot
			? $"symbols={Escape(symbol)}"
			: $"symbol={Escape(symbol)}";
		return (await SendPublicAsync<WeexTicker[]>(section, path, query, cancellationToken))
			.FirstOrDefault(item => item.Symbol.EqualsIgnoreCase(symbol));
	}

	public ValueTask<WeexOrderBook> GetOrderBookAsync(WeexSections section, string symbol, int depth,
		CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot ? "/api/v3/market/depth" : "/capi/v3/market/depth";
		return SendPublicAsync<WeexOrderBook>(section, path,
			$"symbol={Escape(symbol)}&limit={NormalizeDepth(depth)}", cancellationToken);
	}

	public ValueTask<WeexPublicTrade[]> GetTradesAsync(WeexSections section, string symbol, int limit,
		CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot ? "/api/v3/market/trades" : "/capi/v3/market/trades";
		return SendPublicAsync<WeexPublicTrade[]>(section, path,
			$"symbol={Escape(symbol)}&limit={limit.Min(1000).Max(1)}", cancellationToken);
	}

	public async ValueTask<WeexKline[]> GetCandlesAsync(WeexSections section, string symbol,
		TimeSpan timeFrame, DateTime? from, DateTime? to, int count, CancellationToken cancellationToken)
	{
		var interval = timeFrame.ToNative();
		count = count.Min(1000).Max(1);
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var start = (from ?? end - TimeSpan.FromTicks(timeFrame.Ticks * count)).ToUniversalTime();

		if (section == WeexSections.Spot)
		{
			var response = await SendPublicAsync<WeexKline[]>(section, "/api/v3/market/klines",
				$"symbol={Escape(symbol)}&interval={Escape(interval)}&limit={count}", cancellationToken);
			return response
				.Where(item => item.OpenTime.ToUtcTime() >= start && item.OpenTime.ToUtcTime() <= end)
				.OrderBy(static item => item.OpenTime)
				.TakeLast(count)
				.ToArray();
		}

		if (interval is "2h" or "6h" or "8h" or "1M")
		{
			var response = await SendPublicAsync<WeexKline[]>(section, "/capi/v3/market/klines",
				$"symbol={Escape(symbol)}&interval={Escape(interval)}&limit={count}", cancellationToken);
			return response.OrderBy(static item => item.OpenTime).TakeLast(count).ToArray();
		}

		var result = new List<WeexKline>(count);
		var cursor = start;
		while (cursor <= end && result.Count < count)
		{
			var batchLimit = (count - result.Count).Min(100);
			var query = $"symbol={Escape(symbol)}&interval={Escape(interval)}" +
				$"&startTime={ToUnixMilliseconds(cursor)}&endTime={ToUnixMilliseconds(end)}" +
				$"&limit={batchLimit}&priceType=LAST";
			var batch = (await SendPublicAsync<WeexKline[]>(section, "/capi/v3/market/historyKlines",
				query, cancellationToken)).OrderBy(static item => item.OpenTime).ToArray();
			if (batch.Length == 0)
				break;
			result.AddRange(batch.Where(item => result.Count == 0 || item.OpenTime > result[^1].OpenTime));
			var next = batch[^1].OpenTime.ToUtcTime() + timeFrame;
			if (next <= cursor)
				break;
			cursor = next;
		}
		return result.Take(count).ToArray();
	}

	public ValueTask<WeexSpotAccount> GetSpotAccountAsync(CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexSpotAccount>(WeexSections.Spot, HttpMethod.Get, "/api/v3/account",
			null, true, cancellationToken);

	public ValueTask<WeexFuturesBalance[]> GetFuturesBalancesAsync(CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexFuturesBalance[]>(WeexSections.Futures, HttpMethod.Get,
			"/capi/v3/account/balance", null, true, cancellationToken);

	public ValueTask<WeexPosition[]> GetPositionsAsync(string symbol, CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexPosition[]>(WeexSections.Futures, HttpMethod.Get,
			symbol.IsEmpty() ? "/capi/v3/account/position/allPosition" : "/capi/v3/account/position/singlePosition",
			symbol.IsEmpty() ? null : $"symbol={Escape(symbol)}", true, cancellationToken);

	public ValueTask<WeexOrderActionResult> RegisterSpotOrderAsync(WeexSpotOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexOrderActionResult, WeexSpotOrderRequest>(WeexSections.Spot,
			HttpMethod.Post, "/api/v3/order", null, request, false, cancellationToken);

	public ValueTask<WeexOrderActionResult> RegisterFuturesOrderAsync(WeexFuturesOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexOrderActionResult, WeexFuturesOrderRequest>(WeexSections.Futures,
			HttpMethod.Post, "/capi/v3/order", null, request, false, cancellationToken);

	public ValueTask<WeexOrderActionResult> RegisterAlgoOrderAsync(WeexAlgoOrderRequest request,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexOrderActionResult, WeexAlgoOrderRequest>(WeexSections.Futures,
			HttpMethod.Post, "/capi/v3/algoOrder", null, request, false, cancellationToken);

	public ValueTask<WeexOrderActionResult> CancelOrderAsync(WeexSections section, string symbol,
		string orderId, bool isConditional, CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot
			? "/api/v3/order"
			: isConditional ? "/capi/v3/algoOrder" : "/capi/v3/order";
		var query = section == WeexSections.Spot
			? $"symbol={Escape(symbol)}&orderId={Escape(orderId)}"
			: $"orderId={Escape(orderId)}";
		return SendPrivateAsync<WeexOrderActionResult>(section, HttpMethod.Delete, path, query,
			false, cancellationToken);
	}

	public ValueTask<WeexOrderActionResult[]> CancelAllOrdersAsync(WeexSections section, string symbol,
		bool isConditional, CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot
			? "/api/v3/openOrders"
			: isConditional ? "/capi/v3/algoOpenOrders" : "/capi/v3/allOpenOrders";
		var query = symbol.IsEmpty() ? null : $"symbol={Escape(symbol)}";
		return SendPrivateAsync<WeexOrderActionResult[]>(section, HttpMethod.Delete, path, query,
			false, cancellationToken);
	}

	public ValueTask<WeexClosePositionResult[]> ClosePositionsAsync(string symbol, long? positionId,
		CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexClosePositionResult[], WeexClosePositionsRequest>(WeexSections.Futures,
			HttpMethod.Post, "/capi/v3/closePositions", null, new()
			{
				Symbol = symbol,
				PositionId = positionId,
			}, false, cancellationToken);

	public ValueTask<WeexOrder[]> GetOpenOrdersAsync(WeexSections section, string symbol,
		CancellationToken cancellationToken)
	{
		var path = section == WeexSections.Spot ? "/api/v3/openOrders" : "/capi/v3/openOrders";
		var query = symbol.IsEmpty() ? null : $"symbol={Escape(symbol)}";
		return SendPrivateAsync<WeexOrder[]>(section, HttpMethod.Get, path, query, true, cancellationToken);
	}

	public ValueTask<WeexOrder[]> GetOrderHistoryAsync(WeexSections section, string symbol, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
	{
		if (section == WeexSections.Spot && symbol.IsEmpty())
			return new ValueTask<WeexOrder[]>([]);

		var path = section == WeexSections.Spot ? "/api/v3/allOrders" : "/capi/v3/order/history";
		var query = BuildHistoryQuery(symbol, from, to, limit, section == WeexSections.Spot ? 1 : 0);
		return SendPrivateAsync<WeexOrder[]>(section, HttpMethod.Get, path, query, true, cancellationToken);
	}

	public ValueTask<WeexUserTrade[]> GetUserTradesAsync(WeexSections section, string symbol, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
	{
		if (section == WeexSections.Spot && symbol.IsEmpty())
			return new ValueTask<WeexUserTrade[]>([]);

		var path = section == WeexSections.Spot ? "/api/v3/myTrades" : "/capi/v3/userTrades";
		var query = BuildHistoryQuery(symbol, from, to, limit.Min(section == WeexSections.Spot ? 1000 : 100), null);
		return SendPrivateAsync<WeexUserTrade[]>(section, HttpMethod.Get, path, query, true, cancellationToken);
	}

	public ValueTask<WeexAlgoOrder[]> GetOpenAlgoOrdersAsync(string symbol, CancellationToken cancellationToken)
		=> SendPrivateAsync<WeexAlgoOrder[]>(WeexSections.Futures, HttpMethod.Get,
			"/capi/v3/openAlgoOrders", symbol.IsEmpty() ? "page=1&limit=100" : $"symbol={Escape(symbol)}&page=1&limit=100",
			true, cancellationToken);

	public async ValueTask<WeexAlgoOrder[]> GetAlgoOrderHistoryAsync(string symbol, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
		=> (await SendPrivateAsync<WeexAlgoOrderHistory>(WeexSections.Futures, HttpMethod.Get,
			"/capi/v3/allAlgoOrders", BuildHistoryQuery(symbol, from, to, limit, null), true,
			cancellationToken)).Orders ?? [];

	public WeexWebSocketAuthentication CreateWebSocketAuthentication(WeexSections section)
	{
		EnsureCredentials();
		var timestamp = GetTimestamp(section).ToString(CultureInfo.InvariantCulture);
		const string path = "/v3/ws/private";
		return new()
		{
			ApiKey = _apiKey,
			Passphrase = _passphrase,
			Timestamp = timestamp,
			Signature = Sign(timestamp + path),
		};
	}

	private ValueTask<TResult> SendPublicAsync<TResult>(WeexSections section, string path, string query,
		CancellationToken cancellationToken)
		where TResult : class
		=> SendAsync<TResult>(section, HttpMethod.Get, path, query, null, true, true, cancellationToken);

	private ValueTask<TResult> SendPrivateAsync<TResult>(WeexSections section, HttpMethod method,
		string path, string query, bool safe, CancellationToken cancellationToken)
		where TResult : class
	{
		EnsureCredentials();
		return SendAsync<TResult>(section, method, path, query, null, false, safe, cancellationToken);
	}

	private ValueTask<TResult> SendPrivateAsync<TResult, TRequest>(WeexSections section, HttpMethod method,
		string path, string query, TRequest request, bool safe, CancellationToken cancellationToken)
		where TResult : class
		where TRequest : class
	{
		EnsureCredentials();
		var body = JsonConvert.SerializeObject(request, _jsonSettings);
		return SendAsync<TResult>(section, method, path, query, body, false, safe, cancellationToken);
	}

	private async ValueTask<TResult> SendAsync<TResult>(WeexSections section, HttpMethod method,
		string path, string query, string body, bool isPublic, bool safe, CancellationToken cancellationToken)
		where TResult : class
	{
		var endpoint = section == WeexSections.Spot ? _spotEndpoint : _futuresEndpoint;
		var relative = path + (query.IsEmpty() ? string.Empty : "?" + query);

		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, new Uri(endpoint, relative));
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
			if (!isPublic)
			{
				var timestamp = GetTimestamp(section).ToString(CultureInfo.InvariantCulture);
				var payload = timestamp + method.Method.ToUpperInvariant() + path +
					(query.IsEmpty() ? string.Empty : "?" + query) + (body ?? string.Empty);
				request.Headers.TryAddWithoutValidation("ACCESS-KEY", _apiKey);
				request.Headers.TryAddWithoutValidation("ACCESS-SIGN", Sign(payload));
				request.Headers.TryAddWithoutValidation("ACCESS-PASSPHRASE", _passphrase);
				request.Headers.TryAddWithoutValidation("ACCESS-TIMESTAMP", timestamp);
			}
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8, "application/json");

			HttpResponseMessage response;
			try
			{
				response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			}
			catch (HttpRequestException error) when (safe && attempt < _maxAttempts)
			{
				this.AddWarningLog("WEEX {0} transport error. Retrying safe request: {1}", path, error.Message);
				await Task.Delay(GetRetryDelay(null, attempt), cancellationToken);
				continue;
			}
			catch (HttpRequestException error)
			{
				throw new InvalidOperationException(CreateTransportError(path, safe, error.Message), error);
			}

			using (response)
			{
				var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
				if (safe && (response.StatusCode == HttpStatusCode.TooManyRequests ||
					(int)response.StatusCode >= 500) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}

				if (!response.IsSuccessStatusCode)
					throw CreateError(response.StatusCode, path, responseBody, safe);

				ThrowIfApiError(path, responseBody, safe);

				try
				{
					return JsonConvert.DeserializeObject<TResult>(responseBody, _jsonSettings)
						?? throw new InvalidDataException($"WEEX {path} returned no JSON value.");
				}
				catch (JsonException error)
				{
					throw new InvalidDataException($"WEEX {path} returned invalid JSON.", error);
				}
			}
		}
	}

	private long GetTimestamp(WeexSections section)
		=> DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + (section == WeexSections.Spot
			? Volatile.Read(ref _spotTimeOffset)
			: Volatile.Read(ref _futuresTimeOffset));

	private string Sign(string payload)
	{
		byte[] hash;
		using (_signSync.EnterScope())
			hash = _hasher.ComputeHash(payload.UTF8());
		return Convert.ToBase64String(hash);
	}

	private void EnsureCredentials()
	{
		if (_apiKey.IsEmpty())
			throw new InvalidOperationException("WEEX API key is not specified.");
		if (_hasher is null)
			throw new InvalidOperationException("WEEX API secret is not specified.");
		if (_passphrase.IsEmpty())
			throw new InvalidOperationException("WEEX API passphrase is not specified.");
	}

	private static string BuildHistoryQuery(string symbol, DateTime? from, DateTime? to, int limit,
		int? page)
	{
		var query = new StringBuilder();
		AppendQuery(query, "symbol", symbol.IsEmpty() ? null : symbol);
		AppendQuery(query, "startTime", from is null ? null
			: ToUnixMilliseconds(from.Value).ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "endTime", to is null ? null
			: ToUnixMilliseconds(to.Value).ToString(CultureInfo.InvariantCulture));
		AppendQuery(query, "limit", limit.Min(1000).Max(1).ToString(CultureInfo.InvariantCulture));
		if (page is not null)
			AppendQuery(query, "page", page.Value.ToString(CultureInfo.InvariantCulture));
		return query.ToString();
	}

	private static void AppendQuery(StringBuilder query, string name, string value)
	{
		if (value.IsEmpty())
			return;
		if (query.Length > 0)
			query.Append('&');
		query.Append(name).Append('=').Append(Escape(value));
	}

	private static Exception CreateError(HttpStatusCode statusCode, string path, string body, bool safe)
	{
		WeexApiError error;
		try
		{
			error = JsonConvert.DeserializeObject<WeexApiError>(body);
		}
		catch (JsonException)
		{
			error = null;
		}

		var detail = error is null
			? body.IsEmpty("empty response")
			: $"{error.Code}: {error.Message}";
		var outcome = safe || (int)statusCode < 500
			? string.Empty
			: " Trading operation outcome is unknown; do not submit it again without checking order state.";
		return new InvalidOperationException($"WEEX {path} failed ({(int)statusCode}): {detail}.{outcome}");
	}

	private static void ThrowIfApiError(string path, string body, bool safe)
	{
		WeexApiError error;
		try
		{
			error = JsonConvert.DeserializeObject<WeexApiError>(body);
		}
		catch (JsonException)
		{
			return;
		}

		if (error?.Code.IsEmpty() != false || error.Code is "0" or "00000" or "SUCCESS")
			return;

		var outcome = safe
			? string.Empty
			: " Trading operation outcome is unknown; verify order state before retrying.";
		throw new InvalidOperationException($"WEEX {path} failed: {error.Code}: {error.Message}.{outcome}");
	}

	private static string CreateTransportError(string path, bool safe, string detail)
		=> $"WEEX {path} failed: {detail}." + (safe
			? string.Empty
			: " Trading operation outcome is unknown; it was not retried.");

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response?.Headers.RetryAfter?.Delta is TimeSpan retryAfter && retryAfter > TimeSpan.Zero)
			return retryAfter.Min(TimeSpan.FromSeconds(60));
		return TimeSpan.FromSeconds(1 << attempt.Min(5));
	}

	private static int NormalizeDepth(int depth) => depth > 15 ? 200 : 15;

	private static long ToUnixMilliseconds(DateTime value)
		=> new DateTimeOffset(value.ToUniversalTime()).ToUnixTimeMilliseconds();

	private static string Escape(string value) => Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		return endpoint.TrimEnd('/') + "/";
	}
}

sealed class WeexWebSocketAuthentication
{
	public string ApiKey { get; init; }
	public string Passphrase { get; init; }
	public string Timestamp { get; init; }
	public string Signature { get; init; }
}
