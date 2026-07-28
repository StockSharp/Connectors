namespace StockSharp.BigOne.Native;

sealed class BigOneRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;

	private readonly Uri _spotEndpoint;
	private readonly Uri _contractEndpoint;
	private readonly HttpClient _http = new();
	private readonly BigOneAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public BigOneRestClient(
		string spotEndpoint,
		string contractEndpoint,
		SecureString key,
		SecureString secret)
	{
		_spotEndpoint = CreateEndpoint(
			spotEndpoint, nameof(spotEndpoint));
		_contractEndpoint = CreateEndpoint(
			contractEndpoint, nameof(contractEndpoint));
		_authenticator = new(key, secret);
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-BigONE-Connector/1.0");
	}

	public override string Name => "BigONE_REST";

	public bool IsCredentialsAvailable => _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<BigOneSymbol[]> GetSymbolsAsync(
		CancellationToken cancellationToken)
	{
		var spot = await SendSpotAsync<BigOneSpotPair[]>(
			HttpMethod.Get, "/asset_pairs", null, null, false,
			cancellationToken) ?? [];
		var contracts = await GetContractInstrumentsAsync(
			cancellationToken);
		return
		[
			.. spot.Where(static pair =>
				pair?.Name.IsEmpty() == false &&
				pair.BaseAsset?.Symbol.IsEmpty() == false &&
				pair.QuoteAsset?.Symbol.IsEmpty() == false),
			.. contracts.Where(static instrument =>
				instrument?.Symbol.IsEmpty() == false),
		];
	}

	public async ValueTask<BigOneContractInstrument[]>
		GetContractInstrumentsAsync(
			CancellationToken cancellationToken)
		=> await SendContractAsync<BigOneContractInstrument[]>(
			HttpMethod.Get, "/instruments", null, null, false,
			cancellationToken) ?? [];

	public async ValueTask<BigOneTicker> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
		{
			var instrument = (await GetContractInstrumentsAsync(
				cancellationToken)).FirstOrDefault(value =>
					value.Symbol.EqualsIgnoreCase(symbol));
			return instrument?.ToTicker();
		}

		var ticker = await SendSpotAsync<BigOneSpotTicker>(
			HttpMethod.Get,
			$"/asset_pairs/{Escape(symbol)}/ticker",
			null, null, false, cancellationToken);
		return ticker?.ToTicker();
	}

	public async ValueTask<BigOneOrderBook> GetOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		depth = NormalizeDepth(depth);
		BigOneOrderBook result;
		if (IsContractSymbol(symbol))
		{
			var book = await SendContractAsync<BigOneContractDepth>(
				HttpMethod.Get,
				$"/depth@{Escape(symbol)}/snapshot",
				null, null, false, cancellationToken);
			result = book?.ToOrderBook(symbol);
		}
		else
		{
			var book = await SendSpotAsync<BigOneSpotDepth>(
				HttpMethod.Get,
				$"/asset_pairs/{Escape(symbol)}/depth",
				Query(("limit", depth)), null, false,
				cancellationToken);
			result = book?.ToOrderBook();
			if (result is not null && result.Pair.IsEmpty())
				result.Pair = symbol;
		}
		if (result is not null)
			result.Limit = depth;
		return result;
	}

	public async ValueTask<BigOneTrade[]> GetPublicTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
			return [];
		var trades = await SendSpotAsync<BigOneSpotTrade[]>(
			HttpMethod.Get,
			$"/asset_pairs/{Escape(symbol)}/trades",
			null, null, false, cancellationToken) ?? [];
		return [.. trades.Select(trade => trade.ToTrade(symbol))];
	}

	public async ValueTask<BigOneCandle[]> GetCandlesAsync(
		string symbol,
		string resolution,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
			return [];
		var timeFrame = BigOneExtensions.TimeFrames.FirstOrDefault(
			value => value.ToBigOneSpotPeriod()
				.EqualsIgnoreCase(resolution));
		if (timeFrame <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(resolution), resolution,
				"Unsupported BigONE spot candle period.");
		var count = ((long)Math.Ceiling(
			(to.ToUtc() - from.ToUtc()).Ticks /
			(double)timeFrame.Ticks) + 1).Max(1).Min(500).To<int>();
		var candles = await SendSpotAsync<BigOneSpotCandle[]>(
			HttpMethod.Get,
			$"/asset_pairs/{Escape(symbol)}/candles",
			Query(
				("period", resolution),
				("time", to.ToUtc().ToString(
					"O", CultureInfo.InvariantCulture)),
				("limit", count),
				("direction", "ASC")),
			null, false, cancellationToken) ?? [];
		return [.. candles
			.Where(candle =>
				candle.Time.ToUtc() >= from.ToUtc() &&
				candle.Time.ToUtc() <= to.ToUtc())
			.OrderBy(static candle => candle.Time)
			.Select(static candle => candle.ToCandle())];
	}

	public async ValueTask<BigOneBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		var spot = await SendSpotAsync<BigOneSpotAccount[]>(
			HttpMethod.Get, "/viewer/accounts", null, null, true,
			cancellationToken) ?? [];
		var contracts = await GetContractAccountsAsync(
			cancellationToken);
		return
		[
			.. spot.Select(static account => account.ToBalance()),
			.. contracts
				.Where(static account => account?.Cash is not null)
				.Select(static account => account.Cash.ToBalance()),
		];
	}

	public async ValueTask<BigOneContractAccount[]>
		GetContractAccountsAsync(
			CancellationToken cancellationToken)
		=> await SendContractAsync<BigOneContractAccount[]>(
			HttpMethod.Get, "/accounts", null, null, true,
			cancellationToken) ?? [];

	public async ValueTask<BigOneOrder[]> GetOpenOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
		{
			var orders = await SendContractAsync<
				BigOneContractOrder[]>(
				HttpMethod.Get, "/orders/opening",
				Query(("symbol", symbol), ("limit", 200)),
				null, true, cancellationToken) ?? [];
			return [.. orders.Select(static order => order.ToOrder())];
		}
		var spot = await SendSpotAsync<BigOneSpotOrder[]>(
			HttpMethod.Get, "/viewer/orders",
			Query(
				("asset_pair_name", symbol),
				("state", "PENDING"),
				("limit", 200)),
			null, true, cancellationToken) ?? [];
		return [.. spot.Select(static order => order.ToOrder())];
	}

	public async ValueTask<BigOneOrder> GetOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		orderId = orderId.ThrowIfEmpty(nameof(orderId));
		if (IsContractSymbol(symbol))
			return (await SendContractAsync<BigOneContractOrder>(
				HttpMethod.Get,
				$"/orders/{Escape(orderId)}",
				null, null, true, cancellationToken))?.ToOrder();
		return (await SendSpotAsync<BigOneSpotOrder>(
			HttpMethod.Get,
			$"/viewer/orders/{Escape(orderId)}",
			null, null, true, cancellationToken))?.ToOrder();
	}

	public async ValueTask<BigOneOrder[]> GetOrdersAsync(
		string symbol,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
		{
			var orders = await SendContractAsync<
				BigOneContractOrder[]>(
				HttpMethod.Get, "/orders",
				Query(
					("symbol", symbol),
					("start-time", from?.ToUtc()
						.ToBigOneMilliseconds()),
					("end-time", to?.ToUtc()
						.ToBigOneMilliseconds()),
					("limit", limit.Max(1).Min(200))),
				null, true, cancellationToken) ?? [];
			return [.. orders.Select(static order => order.ToOrder())];
		}
		var spot = await SendSpotAsync<BigOneSpotOrder[]>(
			HttpMethod.Get, "/viewer/orders",
			Query(
				("asset_pair_name", symbol),
				("state", "ALL"),
				("limit", limit.Max(1).Min(200))),
			null, true, cancellationToken) ?? [];
		return [.. spot.Select(static order => order.ToOrder())];
	}

	public async ValueTask<BigOnePrivateTrade[]>
		GetPrivateTradesAsync(
			string symbol,
			DateTime? from,
			DateTime? to,
			int limit,
			CancellationToken cancellationToken)
	{
		_ = from;
		_ = to;
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
		{
			var trades = await SendContractAsync<
				BigOneContractTradeExecution[]>(
				HttpMethod.Get, "/trades",
				Query(
					("symbol", symbol),
					("limit", limit.Max(1).Min(200))),
				null, true, cancellationToken) ?? [];
			return [.. trades.Select(static trade => trade.ToTrade())];
		}
		var spot = await SendSpotAsync<BigOneSpotUserTrade[]>(
			HttpMethod.Get, "/viewer/trades",
			Query(
				("asset_pair_name", symbol),
				("limit", limit.Max(1).Min(200))),
			null, true, cancellationToken) ?? [];
		return [.. spot.Select(static trade => trade.ToTrade())];
	}

	public async ValueTask<BigOnePlaceOrderResult>
		PlaceOrderAsync(
			string symbol,
			BigOnePlaceOrderRequest order,
			CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		if (IsContractSymbol(symbol))
		{
			var type = NormalizeContractOrderType(order);
			var body = new JObject
			{
				["symbol"] = symbol,
				["side"] = order.Side.ToSide()
					.ToBigOneContract(),
				["type"] = type,
				["size"] = decimal.Parse(
					order.Volume,
					CultureInfo.InvariantCulture),
				["reduceOnly"] = order.ReduceOnly,
			};
			if (type != "MARKET")
				body["price"] = decimal.Parse(
					order.Price.ThrowIfEmpty(nameof(order.Price)),
					CultureInfo.InvariantCulture);
			var result = await SendContractAsync<
				BigOneContractOrder>(
				HttpMethod.Post, "/orders", null, body, true,
				cancellationToken);
			return new() { Order = result?.ToOrder() };
		}

		var spotType = NormalizeSpotOrderType(order);
		var spotBody = new JObject
		{
			["asset_pair_name"] = symbol,
			["side"] = order.Side.ToSide().ToBigOne(),
			["amount"] = order.Volume,
			["type"] = spotType,
			["client_order_id"] = order.ClientOid,
		};
		if (spotType is "LIMIT" or "STOP_LIMIT")
			spotBody["price"] =
				order.Price.ThrowIfEmpty(nameof(order.Price));
		if (spotType.StartsWith("STOP", StringComparison.Ordinal))
		{
			spotBody["stop_price"] =
				order.StopPrice.ThrowIfEmpty(nameof(order.StopPrice));
			spotBody["operator"] = order.TriggerAbove
				? "GTE"
				: "LTE";
		}
		if (order.PostOnly)
			spotBody["post_only"] = true;
		if (order.OrderType.EqualsIgnoreCase("ioc_limit"))
			spotBody["immediate_or_cancel"] = true;
		var created = await SendSpotAsync<BigOneSpotOrder>(
			HttpMethod.Post, "/viewer/orders", null, spotBody, true,
			cancellationToken);
		return new() { Order = created?.ToOrder() };
	}

	public async ValueTask CancelOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		orderId = orderId.ThrowIfEmpty(nameof(orderId));
		if (IsContractSymbol(symbol))
		{
			_ = await SendContractAsync<JToken>(
				HttpMethod.Delete,
				$"/orders/{Escape(orderId)}",
				null, null, true, cancellationToken);
			return;
		}
		_ = await SendSpotAsync<BigOneSpotOrder>(
			HttpMethod.Post,
			$"/viewer/orders/{Escape(orderId)}/cancel",
			null, null, true, cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSymbol(symbol);
		if (IsContractSymbol(symbol))
		{
			foreach (var order in await GetOpenOrdersAsync(
				symbol, cancellationToken))
				await CancelOrderAsync(
					symbol, order.Id, cancellationToken);
			return;
		}
		_ = await SendSpotAsync<JToken>(
			HttpMethod.Post, "/viewer/orders/cancel", null,
			new JObject { ["asset_pair_name"] = symbol },
			true, cancellationToken);
	}

	internal static T DeserializeSpot<T>(string body)
	{
		var root = Parse(body);
		if (root is JObject obj && obj["code"] is not null)
		{
			var code = obj.Value<int?>("code") ?? 0;
			if (code != 0)
				throw new InvalidDataException(
					$"BigONE spot error {code}: " +
					$"{obj.Value<string>("message") ??
						obj.Value<string>("msg")}");
			root = obj["data"] ?? JValue.CreateNull();
		}
		return root.Type == JTokenType.Null
			? default
			: root.ToObject<T>(JsonSerializer.Create(
				CreateJsonSettings()));
	}

	internal static T DeserializeContract<T>(string body)
	{
		var root = Parse(body);
		if (root is JObject obj)
		{
			if (obj["anomaly"] is not null)
				throw new InvalidDataException(
					$"BigONE contract error: " +
					$"{obj.Value<string>("anomaly")}");
			root = obj["value"] ?? obj["data"] ?? root;
		}
		return root.Type == JTokenType.Null
			? default
			: root.ToObject<T>(JsonSerializer.Create(
				CreateJsonSettings()));
	}

	internal static int NormalizeDepth(int depth)
		=> depth <= 0 ? 50 : depth.Min(200).Max(1);

	private ValueTask<T> SendSpotAsync<T>(
		HttpMethod method,
		string path,
		IDictionary<string, string> query,
		JToken body,
		bool isPrivate,
		CancellationToken cancellationToken)
		=> SendAsync<T>(
			_spotEndpoint, method, path, query, body,
			isPrivate, false, cancellationToken);

	private ValueTask<T> SendContractAsync<T>(
		HttpMethod method,
		string path,
		IDictionary<string, string> query,
		JToken body,
		bool isPrivate,
		CancellationToken cancellationToken)
		=> SendAsync<T>(
			_contractEndpoint, method, path, query, body,
			isPrivate, true, cancellationToken);

	private async ValueTask<T> SendAsync<T>(
		Uri endpoint,
		HttpMethod method,
		string path,
		IDictionary<string, string> query,
		JToken body,
		bool isPrivate,
		bool isContract,
		CancellationToken cancellationToken)
	{
		Exception lastError = null;
		for (var attempt = 0; attempt < _maximumReadAttempts;
			attempt++)
		{
			try
			{
				await WaitRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(
					method, CreateRequestUri(endpoint, path, query));
				request.Headers.Accept.ParseAdd("application/json");
				if (isPrivate)
				request.Headers.Authorization =
					new("Bearer", isContract
						? _authenticator.CreateContractToken()
						: _authenticator.CreateSpotToken());
				if (body is not null)
					request.Content = new StringContent(
						body.ToString(Formatting.None),
						Encoding.UTF8, "application/json");
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseContentRead,
					cancellationToken);
				var responseBody = await response.Content
					.ReadAsStringAsync(cancellationToken);
				if (!response.IsSuccessStatusCode)
					throw new HttpRequestException(
						$"BigONE {(isContract ? "contract" : "spot")} " +
						$"HTTP {(int)response.StatusCode}: " +
						responseBody);
				if (typeof(T) == typeof(JToken) &&
					responseBody.IsEmpty())
					return default;
				return isContract
					? DeserializeContract<T>(responseBody)
					: DeserializeSpot<T>(responseBody);
			}
			catch (Exception error) when (
				error is HttpRequestException or IOException &&
				attempt + 1 < _maximumReadAttempts &&
				!cancellationToken.IsCancellationRequested)
			{
				lastError = error;
				await Task.Delay(
					TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
			}
		}
		throw lastError ?? new InvalidOperationException(
			"BigONE request failed.");
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var now = DateTime.UtcNow;
			if (_nextRequestTime > now)
				await Task.Delay(
					_nextRequestTime - now, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(80);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static Uri CreateEndpoint(string endpoint, string name)
	{
		endpoint = endpoint.ThrowIfEmpty(name).Trim().TrimEnd('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				$"Invalid BigONE REST endpoint '{endpoint}'.", name);
		return uri;
	}

	private static Uri CreateRequestUri(
		Uri endpoint,
		string path,
		IDictionary<string, string> query)
	{
		var builder = new StringBuilder(
			endpoint.ToString().TrimEnd('/'));
		builder.Append('/').Append(path.TrimStart('/'));
		if (query is { Count: > 0 })
			builder.Append('?').Append(string.Join("&",
				query.Where(static pair => !pair.Value.IsEmpty())
					.Select(static pair =>
						$"{Escape(pair.Key)}={Escape(pair.Value)}")));
		return new(builder.ToString(), UriKind.Absolute);
	}

	private static Dictionary<string, string> Query(
		params (string name, object value)[] values)
		=> values
			.Where(static pair => pair.value is not null)
			.ToDictionary(
				static pair => pair.name,
				static pair => Convert.ToString(
					pair.value, CultureInfo.InvariantCulture),
				StringComparer.Ordinal);

	private static string NormalizeSymbol(string symbol)
	{
		symbol = symbol.ThrowIfEmpty(nameof(symbol)).Trim()
			.ToUpperInvariant();
		if (symbol.Contains('/') || symbol.Contains('_'))
			symbol = symbol.ToBigOneSpotSymbol();
		return symbol;
	}

	private static bool IsContractSymbol(string symbol)
		=> !symbol.Contains('-');

	private static string NormalizeSpotOrderType(
		BigOnePlaceOrderRequest order)
		=> order.OrderType?.Trim().ToLowerInvariant() switch
		{
			"market" => "MARKET",
			"stop_market" => "STOP_MARKET",
			"stop_limit" => "STOP_LIMIT",
			_ => "LIMIT",
		};

	private static string NormalizeContractOrderType(
		BigOnePlaceOrderRequest order)
		=> order.OrderType?.Trim().ToLowerInvariant() switch
		{
			"market" or "stop_market" => "MARKET",
			"ioc_limit" => "IOC",
			"fok_limit" => "FOK",
			"post_only" => "POST_ONLY",
			_ => "LIMIT",
		};

	private static string Escape(string value)
		=> Uri.EscapeDataString(value ?? string.Empty);

	private static JToken Parse(string body)
	{
		try
		{
			return JToken.Parse(
				body.ThrowIfEmpty(nameof(body)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"BigONE returned malformed JSON.", error);
		}
	}

	private static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			DateParseHandling = DateParseHandling.DateTime,
			DateTimeZoneHandling = DateTimeZoneHandling.Utc,
			NullValueHandling = NullValueHandling.Ignore,
			Culture = CultureInfo.InvariantCulture,
		};
}
