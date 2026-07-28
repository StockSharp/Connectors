namespace StockSharp.Tokocrypto.Native;

sealed class TokocryptoRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _receiveWindow = 5000;

	private readonly Uri _accountEndpoint;
	private readonly Uri _marketDataEndpoint;
	private readonly HttpClient _http = new();
	private readonly TokocryptoAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public TokocryptoRestClient(
		string accountEndpoint,
		string marketDataEndpoint,
		SecureString key,
		SecureString secret)
	{
		_accountEndpoint = CreateEndpoint(
			accountEndpoint, nameof(accountEndpoint));
		_marketDataEndpoint = CreateEndpoint(
			marketDataEndpoint, nameof(marketDataEndpoint));
		_authenticator = new(key, secret);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Tokocrypto-Connector/1.0");
	}

	public override string Name => "Tokocrypto_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<TokocryptoSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
	{
		var response = await SendAccountAsync<
			TokocryptoSymbolList>(
			HttpMethod.Get,
			"/open/v1/common/symbols",
			[],
			false,
			cancellationToken);
		return [.. (response?.List ?? [])
			.Where(static symbol =>
				symbol is not null &&
				symbol.SymbolType == 1 &&
				symbol.IsSpotTradingEnabled)];
	}

	public ValueTask<TokocryptoTicker> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SendMarketAsync<TokocryptoTicker>(
			HttpMethod.Get,
			"/ticker/24hr",
			Values(("symbol", NormalizeMarketSymbol(symbol))),
			cancellationToken);

	public async ValueTask<TokocryptoOrderBook> GetOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		var normalized = NormalizeMarketSymbol(symbol);
		var limit = NormalizeDepth(depth);
		var result = await SendMarketAsync<TokocryptoOrderBook>(
			HttpMethod.Get,
			"/depth",
			Values(("symbol", normalized), ("limit", limit)),
			cancellationToken);
		result.Pair = normalized;
		result.Timestamp = DateTime.UtcNow.ToTokocryptoMilliseconds();
		result.Limit = limit;
		return result;
	}

	public ValueTask<TokocryptoTrade[]> GetPublicTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> SendMarketAsync<TokocryptoTrade[]>(
			HttpMethod.Get,
			"/trades",
			Values(
				("symbol", NormalizeMarketSymbol(symbol)),
				("limit", 1000)),
			cancellationToken);

	public async ValueTask<TokocryptoCandle[]> GetCandlesAsync(
		string symbol,
		string resolution,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
	{
		if (!TokocryptoExtensions.TimeFrames.Any(
			timeFrame => timeFrame.ToTokocryptoInterval()
				.EqualsIgnoreCase(resolution)))
			throw new ArgumentOutOfRangeException(
				nameof(resolution), resolution,
				"Unsupported Tokocrypto candle interval.");

		var body = await SendRawAsync(
			_marketDataEndpoint,
			HttpMethod.Get,
			"/klines",
			Values(
				("symbol", NormalizeMarketSymbol(symbol)),
				("interval", resolution),
				("startTime", from.ToUtc()
					.ToTokocryptoMilliseconds()),
				("endTime", to.ToUtc()
					.ToTokocryptoMilliseconds()),
				("limit", 1000)),
			false,
			cancellationToken);
		return DeserializeKlines(UnwrapMarketBody(body));
	}

	public async ValueTask<TokocryptoBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> (await SendAccountAsync<TokocryptoAccount>(
			HttpMethod.Get,
			"/open/v1/account/spot",
			[],
			true,
			cancellationToken))?.Assets ?? [];

	public async ValueTask<TokocryptoOrder[]> GetOpenOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> (await SendAccountAsync<TokocryptoList<TokocryptoOrder>>(
			HttpMethod.Get,
			"/open/v1/orders",
			Values(
				("symbol", NormalizeAccountSymbol(symbol)),
				("type", 1),
				("limit", 1000)),
			true,
			cancellationToken))?.List ?? [];

	public ValueTask<TokocryptoOrder> GetOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
	{
		_ = symbol;
		return SendAccountAsync<TokocryptoOrder>(
			HttpMethod.Get,
			"/open/v1/orders/detail",
			Values(("orderId", orderId.ThrowIfEmpty(
				nameof(orderId)))),
			true,
			cancellationToken);
	}

	public async ValueTask<TokocryptoOrder[]> GetOrdersAsync(
		string symbol,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
		=> (await SendAccountAsync<TokocryptoList<TokocryptoOrder>>(
			HttpMethod.Get,
			"/open/v1/orders",
			Values(
				("symbol", NormalizeAccountSymbol(symbol)),
				("type", -1),
				("startTime", from?.ToUtc()
					.ToTokocryptoMilliseconds()),
				("endTime", to?.ToUtc()
					.ToTokocryptoMilliseconds()),
				("limit", limit.Max(1).Min(1000))),
			true,
			cancellationToken))?.List ?? [];

	public async ValueTask<TokocryptoPrivateTrade[]>
		GetPrivateTradesAsync(
			string symbol,
			DateTime? from,
			DateTime? to,
			int limit,
			CancellationToken cancellationToken)
		=> (await SendAccountAsync<
			TokocryptoList<TokocryptoPrivateTrade>>(
			HttpMethod.Get,
			"/open/v1/orders/trades",
			Values(
				("symbol", NormalizeAccountSymbol(symbol)),
				("startTime", from?.ToUtc()
					.ToTokocryptoMilliseconds()),
				("endTime", to?.ToUtc()
					.ToTokocryptoMilliseconds()),
				("limit", limit.Max(1).Min(1000))),
			true,
			cancellationToken))?.List ?? [];

	public async ValueTask<TokocryptoPlaceOrderResult>
		PlaceOrderAsync(
			string symbol,
			TokocryptoPlaceOrderRequest order,
			CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));

		var values = Values(
			("symbol", NormalizeAccountSymbol(symbol)),
			("side", ToSideCode(order.Side)),
			("type", ToOrderTypeCode(order.OrderType)),
			("timeInForce", ToTimeInForceCode(order.OrderType)),
			("quantity", order.QuoteVolume.IsEmpty()
				? order.Volume
				: null),
			("quoteOrderQty", order.QuoteVolume),
			("price", order.Price),
			("clientId", order.ClientOid),
			("stopPrice", order.StopPrice));

		var response = await SendAccountAsync<TokocryptoOrder>(
			HttpMethod.Post,
			"/open/v1/orders",
			values,
			true,
			cancellationToken);
		return new() { Order = response };
	}

	public async ValueTask CancelOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
	{
		_ = symbol;
		_ = await SendAccountAsync<JToken>(
			HttpMethod.Post,
			"/open/v1/orders/cancel",
			Values(("orderId", orderId.ThrowIfEmpty(
				nameof(orderId)))),
			true,
			cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol));
		foreach (var order in await GetOpenOrdersAsync(
			symbol, cancellationToken))
		{
			if (order?.Id.IsEmpty() == false)
				await CancelOrderAsync(
					symbol, order.Id, cancellationToken);
		}
	}

	internal static TokocryptoCandle[] DeserializeKlines(string body)
	{
		try
		{
			var root = JArray.Parse(
				body.ThrowIfEmpty(nameof(body)));
			return [.. root
				.OfType<JArray>()
				.Where(static candle => candle.Count >= 7)
				.Select(static candle => new TokocryptoCandle
				{
					OpenTime = candle[0].Value<long>(),
					Open = candle[1].Value<decimal>(),
					High = candle[2].Value<decimal>(),
					Low = candle[3].Value<decimal>(),
					Close = candle[4].Value<decimal>(),
					Volume = candle[5].Value<decimal>(),
					CloseTime = candle[6].Value<long>(),
					IsFinished = candle[6].Value<long>() <=
						DateTime.UtcNow.ToTokocryptoMilliseconds(),
				})];
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Tokocrypto returned malformed kline data.", error);
		}
	}

	internal static int NormalizeDepth(int depth)
	{
		foreach (var supported in new[] { 5, 10, 20 })
			if (depth <= supported)
				return supported;
		return 20;
	}

	private async ValueTask<TData> SendMarketAsync<TData>(
		HttpMethod method,
		string path,
		Dictionary<string, object> values,
		CancellationToken cancellationToken)
	{
		var body = await SendRawAsync(
			_marketDataEndpoint,
			method,
			path,
			values,
			false,
			cancellationToken);
		return Deserialize<TData>(
			UnwrapMarketBody(body), "market-data");
	}

	private static string UnwrapMarketBody(string body)
	{
		try
		{
			var token = JToken.Parse(
				body.ThrowIfEmpty(nameof(body)));
			if (token is not JObject root ||
				root["code"] is null)
				return body;
			var code = root.Value<int>("code");
			if (code != 0)
				throw new InvalidOperationException(
					$"Tokocrypto market-data API error {code}: " +
						$"{root.Value<string>("msg")}");
			return root["data"]?.ToString(
				Formatting.None) ??
				throw new InvalidDataException(
					"Tokocrypto market-data API returned no data.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Tokocrypto market-data API returned malformed JSON.",
				error);
		}
	}

	private async ValueTask<TData> SendAccountAsync<TData>(
		HttpMethod method,
		string path,
		Dictionary<string, object> values,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		var body = await SendRawAsync(
			_accountEndpoint,
			method,
			path,
			values,
			isPrivate,
			cancellationToken);
		var response = Deserialize<TokocryptoResponse<TData>>(
			body, "account");
		if (response.Code is not (0 or 200))
			throw new InvalidOperationException(
				$"Tokocrypto API error {response.Code}: " +
					response.Message);
		return response.Data;
	}

	private TData Deserialize<TData>(
		string body,
		string api)
	{
		try
		{
			return JsonConvert.DeserializeObject<TData>(
				body.ThrowIfEmpty(nameof(body)),
				_jsonSettings) ??
				throw new InvalidDataException(
					$"Tokocrypto {api} API returned an empty response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"Tokocrypto {api} API returned an unexpected " +
					"response shape.",
				error);
		}
	}

	private async ValueTask<string> SendRawAsync(
		Uri endpoint,
		HttpMethod method,
		string path,
		Dictionary<string, object> values,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		if (isPrivate)
			EnsureCredentials();
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		values ??= [];

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var requestValues = new Dictionary<string, object>(
				values, StringComparer.Ordinal);
			if (isPrivate)
			{
				requestValues["timestamp"] =
					DateTime.UtcNow.ToTokocryptoMilliseconds();
				requestValues["recvWindow"] = _receiveWindow;
			}
			var query = BuildQuery(requestValues);
			if (isPrivate)
				query += "&signature=" +
					_authenticator.Sign(query);

			using var request = new HttpRequestMessage(
				method,
				new Uri(endpoint, path +
					(query.IsEmpty() ? string.Empty : "?" + query)));
			if (method != HttpMethod.Get)
				request.Content = new StringContent(
					string.Empty, Encoding.UTF8,
					"application/x-www-form-urlencoded");
			if (isPrivate)
				request.Headers.TryAddWithoutValidation(
					"X-MBX-APIKEY", _authenticator.Key);

			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var responseBody =
				await response.Content.ReadAsStringAsync(
					cancellationToken);
			if (response.IsSuccessStatusCode)
				return responseBody;
			if (attempt + 1 >= _maximumReadAttempts ||
				!IsTransient(response.StatusCode))
				throw CreateHttpError(
					response.StatusCode, responseBody);
			await DelayRetryAsync(
				response, attempt, cancellationToken);
		}
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime =
				DateTime.UtcNow.AddMilliseconds(100);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Tokocrypto API key and secret are required " +
					"for private operations.");
	}

	private static Uri CreateEndpoint(
		string endpoint,
		string parameterName)
		=> new(
			endpoint.ThrowIfEmpty(parameterName)
				.TrimEnd('/') + "/",
			UriKind.Absolute);

	private static Dictionary<string, object> Values(
		params (string Name, object Value)[] values)
		=> values
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.ToDictionary(
				static value => value.Name,
				static value => value.Value,
				StringComparer.Ordinal);

	private static string BuildQuery(
		IEnumerable<KeyValuePair<string, object>> values)
		=> (values ?? [])
			.Where(static pair =>
				!pair.Key.IsEmpty() && pair.Value is not null)
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" +
				Uri.EscapeDataString(Convert.ToString(
					pair.Value,
					CultureInfo.InvariantCulture)))
			.Join("&");

	private static string NormalizeAccountSymbol(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		if (symbol.Contains('/') || symbol.Contains('-'))
			return symbol.ToTokocryptoAccountSymbol();
		if (symbol.Contains('_'))
			return symbol.ToUpperInvariant();
		throw new FormatException(
			$"Invalid Tokocrypto account symbol '{symbol}'.");
	}

	private static string NormalizeMarketSymbol(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim();
		if (symbol.Contains('/') ||
			symbol.Contains('_') ||
			symbol.Contains('-'))
			return symbol.ToTokocryptoMarketSymbol();
		return symbol.ToUpperInvariant();
	}

	private static int ToSideCode(string side)
		=> side?.Trim().ToLowerInvariant() switch
		{
			"buy" => 0,
			"sell" => 1,
			_ => throw new ArgumentOutOfRangeException(
				nameof(side), side, LocalizedStrings.InvalidValue),
		};

	private static int ToOrderTypeCode(string type)
		=> type?.Trim().ToLowerInvariant() switch
		{
			"limit" or "ioc_limit" or "fok_limit" => 1,
			"market" => 2,
			"stop_market" => 3,
			"stop_limit" => 4,
			"take_profit" => 5,
			"take_profit_limit" => 6,
			"post_only" => 7,
			_ => throw new ArgumentOutOfRangeException(
				nameof(type), type, LocalizedStrings.InvalidValue),
		};

	private static int? ToTimeInForceCode(string type)
		=> type?.Trim().ToLowerInvariant() switch
		{
			"ioc_limit" => 2,
			"fok_limit" => 3,
			"post_only" => 4,
			"market" or "stop_market" or "take_profit" => null,
			_ => 1,
		};

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(
		HttpResponseMessage response,
		int attempt,
		CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * (1 << attempt));
		await Task.Delay(delay, cancellationToken);
	}

	private static Exception CreateHttpError(
		HttpStatusCode statusCode,
		string body)
	{
		var details = body?.Trim();
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"Tokocrypto HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null,
			statusCode);
	}
}
