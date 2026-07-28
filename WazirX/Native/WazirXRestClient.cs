namespace StockSharp.WazirX.Native;

sealed class WazirXRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly string _key;
	private readonly string _secret;
	private readonly long _receiveWindow;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private long _timeOffset;

	public WazirXRestClient(
		string endpoint,
		SecureString key,
		SecureString secret,
		long receiveWindow)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty()
			? null
			: secret.UnSecure().Trim();
		if (receiveWindow is <= 0 or > 60000)
			throw new ArgumentOutOfRangeException(
				nameof(receiveWindow));
		_receiveWindow = receiveWindow;
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-WazirX-Connector/1.0");
	}

	public override string Name => "WazirX_REST";

	public bool IsCredentialsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask SynchronizeTimeAsync(
		CancellationToken cancellationToken)
	{
		var started = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var root = ParseObject(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/time",
			[],
			false,
			true,
			cancellationToken));
		var completed =
			DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		var serverTime = ReadLong(root["serverTime"]);
		if (serverTime > 0)
			_timeOffset = serverTime -
				(started + (completed - started) / 2);
	}

	public async ValueTask<WazirXMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> DeserializeMarkets(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/exchangeInfo",
			[],
			false,
			true,
			cancellationToken));

	public async ValueTask<WazirXTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> DeserializeTickers(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/tickers/24hr",
			[],
			false,
			true,
			cancellationToken));

	public async ValueTask<WazirXTicker> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> DeserializeTickers(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/ticker/24hr",
			Values(("symbol", NormalizeSymbol(symbol))),
			false,
			true,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<WazirXBook> GetBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> DeserializeBook(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/depth",
			Values(
				("symbol", NormalizeSymbol(symbol)),
				("limit", NormalizeDepth(depth))),
			false,
			true,
			cancellationToken), NormalizeSymbol(symbol));

	public async ValueTask<WazirXTrade[]> GetTradesAsync(
		string symbol,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeTrades(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/trades",
			Values(
				("symbol", NormalizeSymbol(symbol)),
				("limit", limit.Max(1).Min(1000))),
			false,
			true,
			cancellationToken), NormalizeSymbol(symbol));

	public async ValueTask<WazirXCandle[]> GetCandlesAsync(
		string symbol,
		TimeSpan timeFrame,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeCandles(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/klines",
			Values(
				("symbol", NormalizeSymbol(symbol)),
				("interval", timeFrame.ToWazirXInterval()),
				("startTime", from is null
					? null
					: new DateTimeOffset(
						from.Value.ToUniversalTime())
						.ToUnixTimeSeconds()),
				("endTime", to is null
					? null
					: new DateTimeOffset(
						to.Value.ToUniversalTime())
						.ToUnixTimeSeconds()),
				("limit", limit.Max(1).Min(2000))),
			false,
			true,
			cancellationToken),
			NormalizeSymbol(symbol),
			timeFrame);

	public async ValueTask<WazirXBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> DeserializeBalances(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/funds",
			[],
			true,
			true,
			cancellationToken));

	public async ValueTask<WazirXOrder> PlaceOrderAsync(
		WazirXMarket market,
		Sides side,
		OrderTypes orderType,
		decimal volume,
		decimal price,
		decimal? stopPrice,
		string clientOrderId,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Post,
			"/sapi/v1/order",
			Values(
				("symbol", NormalizeSymbol(market?.Symbol)),
				("side", side.ToWazirX()),
				("type", orderType == OrderTypes.Conditional
					? "stop_limit"
					: "limit"),
				("quantity", volume),
				("price", price),
				("stopPrice", stopPrice),
				("clientOrderId", clientOrderId)),
			true,
			false,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<WazirXOrder> CancelOrderAsync(
		string symbol,
		long? orderId,
		string clientOrderId,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Delete,
			"/sapi/v1/order",
			Values(
				("symbol", NormalizeSymbol(symbol)),
				("orderId", orderId),
				("clientOrderId", clientOrderId)),
			true,
			false,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<WazirXOrder[]> CancelAllOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Delete,
			"/sapi/v1/openOrders",
			Values(("symbol", NormalizeSymbol(symbol))),
			true,
			false,
			cancellationToken));

	public async ValueTask<WazirXOrder> GetOrderAsync(
		long? orderId,
		string clientOrderId,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/order",
			Values(
				("orderId", orderId),
				("clientOrderId", clientOrderId)),
			true,
			true,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<WazirXOrder[]> GetOpenOrdersAsync(
		string symbol,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/openOrders",
			Values(
				("symbol", symbol.IsEmpty()
					? null
					: NormalizeSymbol(symbol)),
				("startTime", ToMilliseconds(from)),
				("endTime", ToMilliseconds(to))),
			true,
			true,
			cancellationToken));

	public async ValueTask<WazirXOrder[]> GetAllOrdersAsync(
		string symbol,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/allOrders",
			Values(
				("symbol", NormalizeSymbol(symbol)),
				("startTime", ToMilliseconds(from)),
				("endTime", ToMilliseconds(to)),
				("limit", limit.Max(1).Min(1000))),
			true,
			true,
			cancellationToken));

	public async ValueTask<WazirXUserTrade[]> GetUserTradesAsync(
		string symbol,
		long? orderId,
		long? fromId,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeUserTrades(await SendAsync(
			HttpMethod.Get,
			"/sapi/v1/myTrades",
			Values(
				("symbol", symbol.IsEmpty()
					? null
					: NormalizeSymbol(symbol)),
				("orderId", orderId),
				("fromId", fromId),
				("startTime", ToMilliseconds(from)),
				("endTime", ToMilliseconds(to)),
				("limit", limit.Max(1).Min(1000))),
			true,
			true,
			cancellationToken));

	public async ValueTask<WazirXAuthToken> CreateAuthTokenAsync(
		CancellationToken cancellationToken)
	{
		var root = ParseObject(await SendAsync(
			HttpMethod.Post,
			"/sapi/v1/create_auth_token",
			[],
			true,
			false,
			cancellationToken));
		var key = ReadString(root["auth_key"]);
		if (key.IsEmpty())
			throw new InvalidDataException(
				"WazirX returned no WebSocket auth key.");
		return new()
		{
			Key = key,
			Lifetime = TimeSpan.FromSeconds(
				ReadLong(root["timeout_duration"]).Max(1)),
		};
	}

	internal static string GenerateSignature(
		string payload,
		string secret)
	{
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(
			secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(
				payload.ThrowIfEmpty(nameof(payload)))))
			.ToLowerInvariant();
	}

	internal static WazirXMarket[] DeserializeMarkets(
		string body)
	{
		var symbols = ParseObject(body)["symbols"] as JArray ?? [];
		return [.. symbols
			.OfType<JObject>()
			.Select(static value =>
			{
				var filters = value["filters"] as JArray ?? [];
				var price = FindFilter(filters, "PRICE_FILTER");
				var lot = FindFilter(filters, "LOT_SIZE");
				var orderTypes = value["orderTypes"]?
					.Values<string>() ?? [];
				return new WazirXMarket
				{
					Symbol = ReadString(value["symbol"])
						?.ToLowerInvariant(),
					BaseAsset = ReadString(value["baseAsset"])
						?.ToLowerInvariant(),
					QuoteAsset = ReadString(value["quoteAsset"])
						?.ToLowerInvariant(),
					BasePrecision = ReadInt(
						value["baseAssetPrecision"]),
					QuotePrecision = ReadInt(
						value["quoteAssetPrecision"]),
					PriceStep = ReadDecimal(
						price?["tickSize"]),
					MinimumPrice = ReadDecimal(
						price?["minPrice"]),
					VolumeStep = ReadDecimal(
						lot?["stepSize"]),
					MinimumVolume = ReadDecimal(
						lot?["minQty"]),
					MaximumVolume = ReadDecimal(
						lot?["maxQty"]),
					IsActive =
						ReadBoolean(
							value["isSpotTradingAllowed"]) &&
						ReadString(value["status"])
							.EqualsIgnoreCase("trading"),
					SupportsStopLimit = orderTypes.Any(
						type => type.EqualsIgnoreCase(
							"stop_limit")),
				};
			})
			.Where(static market =>
				!market.Symbol.IsEmpty() &&
				!market.BaseAsset.IsEmpty() &&
				!market.QuoteAsset.IsEmpty())
			.OrderBy(static market => market.Symbol,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static WazirXTicker[] DeserializeTickers(
		string body)
	{
		var root = ParseToken(body);
		IEnumerable<JObject> values = root switch
		{
			JArray array => array.OfType<JObject>(),
			JObject value => [value],
			_ => [],
		};
		return [.. values
			.Select(ParseTicker)
			.Where(static ticker => ticker is not null)];
	}

	internal static WazirXBook DeserializeBook(
		string body,
		string symbol)
		=> ParseBook(
			ParseObject(body),
			symbol,
			true);

	internal static WazirXTrade[] DeserializeTrades(
		string body,
		string symbol)
		=> [.. (ParseToken(body) as JArray ?? [])
			.OfType<JObject>()
			.Select(value => ParseTrade(value, symbol))
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];

	internal static WazirXCandle[] DeserializeCandles(
		string body,
		string symbol = null,
		TimeSpan timeFrame = default)
		=> [.. (ParseToken(body) as JArray ?? [])
			.OfType<JArray>()
			.Where(static value => value.Count >= 6)
			.Select(value => new WazirXCandle
			{
				Symbol = symbol?.ToLowerInvariant(),
				TimeFrame = timeFrame,
				OpenTime = ReadLong(value[0])
					.FromWazirXTimestamp(),
				CloseTime = timeFrame > TimeSpan.Zero
					? ReadLong(value[0])
						.FromWazirXTimestamp() + timeFrame
					: default,
				Open = ReadDecimal(value[1]),
				High = ReadDecimal(value[2]),
				Low = ReadDecimal(value[3]),
				Close = ReadDecimal(value[4]),
				Volume = ReadDecimal(value[5]),
			})
			.OrderBy(static candle => candle.OpenTime)];

	internal static WazirXOrder[] DeserializeOrders(string body)
	{
		var root = ParseToken(body);
		IEnumerable<JObject> values = root switch
		{
			JArray array => array.OfType<JObject>(),
			JObject value => [value],
			_ => [],
		};
		return [.. values
			.Select(ParseOrder)
			.Where(static order => order is not null)];
	}

	internal static WazirXBalance[] DeserializeBalances(
		string body)
		=> ParseBalances(ParseToken(body));

	internal static WazirXUserTrade[] DeserializeUserTrades(
		string body)
		=> [.. (ParseToken(body) as JArray ?? [])
			.OfType<JObject>()
			.Select(ParseUserTrade)
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];

	internal static WazirXTicker ParseTicker(JObject value)
	{
		if (value is null)
			return null;
		var symbol = ReadString(
			value["symbol"] ?? value["s"]);
		if (symbol.IsEmpty())
			return null;
		var time = ReadLong(value["at"] ?? value["E"]);
		return new()
		{
			Symbol = symbol.ToLowerInvariant(),
			Time = time.FromWazirXTimestamp(),
			OpenPrice = ReadDecimal(
				value["openPrice"] ?? value["o"]),
			HighPrice = ReadDecimal(
				value["highPrice"] ?? value["h"]),
			LowPrice = ReadDecimal(
				value["lowPrice"] ?? value["l"]),
			LastPrice = ReadDecimal(
				value["lastPrice"] ?? value["c"]),
			Volume = ReadDecimal(
				value["volume"] ?? value["q"]),
			BidPrice = ReadDecimal(
				value["bidPrice"] ?? value["b"]),
			AskPrice = ReadDecimal(
				value["askPrice"] ?? value["a"]),
		};
	}

	internal static WazirXBook ParseBook(
		JObject value,
		string symbol,
		bool isSnapshot)
	{
		if (value is null)
			return null;
		return new()
		{
			Symbol = (
				ReadString(value["s"]) ?? symbol)
				?.ToLowerInvariant(),
			Time = ReadLong(
				value["lastUpdateAt"] ?? value["E"])
				.FromWazirXTimestamp(),
			IsSnapshot = isSnapshot,
			Bids = ParseQuotes(
				value["bids"] ?? value["b"],
				Sides.Buy,
				isSnapshot),
			Asks = ParseQuotes(
				value["asks"] ?? value["a"],
				Sides.Sell,
				isSnapshot),
		};
	}

	internal static WazirXTrade ParseTrade(
		JObject value,
		string symbol = null)
	{
		if (value is null)
			return null;
		var id = ReadLong(value["id"] ?? value["t"]);
		var price = ReadDecimal(value["price"] ?? value["p"]);
		var volume = ReadDecimal(value["qty"] ?? value["q"]);
		if (id <= 0 || price <= 0 || volume <= 0)
			return null;
		var maker = ReadBoolean(
			value["isBuyerMaker"] ?? value["m"]);
		return new()
		{
			Id = id,
			Symbol = (
				ReadString(value["s"]) ?? symbol)
				?.ToLowerInvariant(),
			Time = ReadLong(value["time"] ?? value["E"])
				.FromWazirXTimestamp(),
			Price = price,
			Volume = volume,
			Side = maker ? Sides.Sell : Sides.Buy,
		};
	}

	internal static WazirXOrder ParseOrder(JObject value)
	{
		if (value is null)
			return null;
		var id = ReadLong(value["id"] ?? value["i"]);
		var symbol = ReadString(value["symbol"] ?? value["s"]);
		if (id <= 0 || symbol.IsEmpty())
			return null;
		var original = ReadDecimal(
			value["origQty"] ?? value["V"]);
		var executed = ReadDecimal(
			value["executedQty"] ?? value["z"]);
		if (original <= 0 &&
			value["q"] is not null &&
			executed >= 0)
			original = executed + ReadDecimal(value["q"]);
		return new()
		{
			Id = id,
			ClientOrderId = ReadString(
				value["clientOrderId"] ?? value["c"]),
			Symbol = symbol.ToLowerInvariant(),
			Price = ReadDecimal(value["price"] ?? value["p"]),
			StopPrice = ReadDecimal(value["stopPrice"]),
			OriginalVolume = original,
			ExecutedVolume = executed,
			State = ReadString(
				value["status"] ?? value["X"])
				.ToWazirXState(),
			OrderType = ReadString(
				value["type"] ?? value["o"])
				.ToWazirXOrderType(),
			Side = ReadString(value["side"] ?? value["S"])
				.ToWazirXSide(),
			CreatedAt = ReadLong(
				value["createdTime"] ?? value["E"])
				.FromWazirXTimestamp(),
			UpdatedAt = ReadLong(
				value["updatedTime"] ?? value["O"] ??
					value["E"])
				.FromWazirXTimestamp(),
		};
	}

	internal static WazirXBalance[] ParseBalances(JToken token)
	{
		if (token is JObject wrapper)
			token = wrapper["spot"] ?? wrapper["B"];
		if (token is not JArray values)
			return [];
		return [.. values
			.OfType<JObject>()
			.Select(static value =>
			{
				var asset = ReadString(
					value["asset"] ?? value["a"]);
				return asset.IsEmpty()
					? null
					: new WazirXBalance
					{
						Asset = asset.ToLowerInvariant(),
						Available = ReadDecimal(
							value["free"] ?? value["b"]),
						Locked = ReadDecimal(
							value["locked"] ?? value["l"]),
						ReservedFee = ReadDecimal(
							value["reservedFee"]),
					};
			})
			.Where(static balance => balance is not null)];
	}

	internal static WazirXUserTrade ParseUserTrade(
		JObject value)
	{
		if (value is null)
			return null;
		var id = ReadLong(value["id"] ?? value["t"]);
		var orderId = ReadLong(value["orderId"] ?? value["o"]);
		var symbol = ReadString(value["symbol"] ?? value["s"]);
		if (id <= 0 || orderId <= 0 || symbol.IsEmpty())
			return null;
		var side = ReadString(value["side"]);
		if (side.IsEmpty())
			side = ReadString(value["S"]);
		return new()
		{
			Id = id,
			OrderId = orderId,
			ClientOrderId = ReadString(
				value["clientOrderId"] ?? value["c"]),
			Symbol = symbol.ToLowerInvariant(),
			Fee = ReadDecimal(value["fee"] ?? value["f"]),
			FeeCurrency = ReadString(
				value["feeCurrency"] ?? value["U"]),
			Price = ReadDecimal(value["price"] ?? value["p"]),
			Volume = ReadDecimal(value["qty"] ?? value["q"]),
			Side = side.ToWazirXSide(),
			Time = ReadLong(value["time"] ?? value["E"])
				.FromWazirXTimestamp(),
		};
	}

	private async ValueTask<string> SendAsync(
		HttpMethod method,
		string path,
		IEnumerable<KeyValuePair<string, string>> values,
		bool isPrivate,
		bool retryable,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"WazirX API key and secret are required for " +
					"private operations.");
		path = path.ThrowIfEmpty(nameof(path));
		var baseValues = (values ?? []).ToList();
		var attempts = retryable ? _maximumReadAttempts : 1;
		Exception lastError = null;
		for (var attempt = 0; attempt < attempts; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var requestValues = new List<
				KeyValuePair<string, string>>(baseValues);
			if (isPrivate)
			{
				requestValues.Add(new(
					"recvWindow",
					_receiveWindow.ToString(
						CultureInfo.InvariantCulture)));
				requestValues.Add(new(
					"timestamp",
					(DateTimeOffset.UtcNow
						.ToUnixTimeMilliseconds() + _timeOffset)
						.ToString(CultureInfo.InvariantCulture)));
			}
			var payload = BuildForm(requestValues);
			if (isPrivate)
				payload += "&signature=" +
					GenerateSignature(payload, _secret);

			var usesQuery = method == HttpMethod.Get;
			var target = new Uri(
				_endpoint,
				path.TrimStart('/') +
					(usesQuery && !payload.IsEmpty()
						? "?" + payload
						: string.Empty));
			using var request = new HttpRequestMessage(
				method, target);
			if (!usesQuery)
				request.Content = new StringContent(
					payload,
					Encoding.UTF8,
					"application/x-www-form-urlencoded");
			if (isPrivate)
				request.Headers.TryAddWithoutValidation(
					"X-API-KEY", _key);
			try
			{
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var body =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (body.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"WazirX response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
				{
					_ = ParseToken(body);
					return body;
				}
				var error = CreateHttpError(
					response.StatusCode,
					body,
					response.ReasonPhrase);
				if (attempt + 1 >= attempts ||
					!IsTransient(response.StatusCode))
					throw error;
				lastError = error;
				await Task.Delay(
					GetRetryDelay(response, attempt),
					cancellationToken);
			}
			catch (Exception error) when (
				attempt + 1 < attempts &&
				!cancellationToken.IsCancellationRequested &&
				error is HttpRequestException or
					TaskCanceledException)
			{
				lastError = error;
				await Task.Delay(
					TimeSpan.FromMilliseconds(
						250 * (1 << attempt)),
					cancellationToken);
			}
		}
		throw lastError ?? new InvalidOperationException(
			"WazirX API request failed.");
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
				DateTime.UtcNow.AddMilliseconds(1050);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static List<KeyValuePair<string, string>> Values(
		params (string Name, object Value)[] values)
		=> [.. values
			.Where(static value =>
				!value.Name.IsEmpty() &&
				value.Value is not null)
			.Select(static value => new KeyValuePair<
				string, string>(
					value.Name,
					Convert.ToString(
						value.Value,
						CultureInfo.InvariantCulture)))];

	private static string BuildForm(
		IEnumerable<KeyValuePair<string, string>> values)
		=> (values ?? [])
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" +
				Uri.EscapeDataString(pair.Value ?? string.Empty))
			.Join("&");

	private static string NormalizeSymbol(string symbol)
		=> symbol.ThrowIfEmpty(nameof(symbol))
			.Trim()
			.Replace("/", string.Empty)
			.Replace("-", string.Empty)
			.Replace("_", string.Empty)
			.ToLowerInvariant();

	internal static int NormalizeDepth(int depth)
	{
		foreach (var supported in
			new[] { 1, 5, 10, 20, 50, 100, 500, 1000 })
		{
			if (depth <= supported)
				return supported;
		}
		return 1000;
	}

	private static long? ToMilliseconds(DateTime? value)
		=> value is null
			? null
			: new DateTimeOffset(
				value.Value.ToUniversalTime())
				.ToUnixTimeMilliseconds();

	private static JObject FindFilter(
		JArray filters,
		string type)
		=> filters.OfType<JObject>().FirstOrDefault(
			filter => ReadString(filter["filterType"])
				.EqualsIgnoreCase(type));

	private static WazirXQuote[] ParseQuotes(
		JToken token,
		Sides side,
		bool isSnapshot)
	{
		if (token is not JArray values)
			return [];
		return [.. values
			.OfType<JArray>()
			.Where(static value => value.Count >= 2)
			.Select(value => new WazirXQuote
			{
				Price = ReadDecimal(value[0]),
				Volume = ReadDecimal(value[1]),
				Side = side,
			})
			.Where(quote =>
				quote.Price > 0 &&
				(!isSnapshot || quote.Volume > 0))
			.OrderBy(quote => side == Sides.Buy
				? -quote.Price
				: quote.Price)];
	}

	private static JToken ParseToken(string body)
	{
		try
		{
			var token = JToken.Parse(
				body.ThrowIfEmpty(nameof(body)));
			if (token is JObject root &&
				root["code"] is not null &&
				ReadLong(root["code"]) < 0)
				throw new InvalidDataException(
					$"WazirX API error {root["code"]}: " +
						ReadString(
							root["message"] ?? root["msg"]));
			return token;
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"WazirX returned malformed JSON.", error);
		}
	}

	private static JObject ParseObject(string body)
		=> ParseToken(body) as JObject ??
			throw new InvalidDataException(
				"WazirX returned an unexpected JSON shape.");

	internal static string ReadString(JToken value)
		=> value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined
				? null
				: value.ToString();

	internal static decimal ReadDecimal(JToken value)
		=> decimal.TryParse(
			ReadString(value),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	internal static long ReadLong(JToken value)
		=> long.TryParse(
			ReadString(value),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static int ReadInt(JToken value)
		=> int.TryParse(
			ReadString(value),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	internal static bool ReadBoolean(JToken value)
		=> bool.TryParse(ReadString(value), out var result) &&
			result;

	private static Uri ValidateEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(
			nameof(endpoint)).Trim().TrimEnd('/') + "/";
		if (!Uri.TryCreate(
			endpoint,
			UriKind.Absolute,
			out var value) ||
			!value.Scheme.EqualsIgnoreCase("https"))
			throw new ArgumentException(
				"WazirX REST endpoint must be an absolute " +
					"HTTPS URI.",
				nameof(endpoint));
		return value;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode is HttpStatusCode.TooManyRequests ||
			statusCode == (HttpStatusCode)418 ||
			(int)statusCode >= 500;

	private static TimeSpan GetRetryDelay(
		HttpResponseMessage response,
		int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
			return delta;
		if (response.Headers.RetryAfter?.Date is
			DateTimeOffset date)
			return (date - DateTimeOffset.UtcNow)
				.Max(TimeSpan.Zero);
		return TimeSpan.FromMilliseconds(
			250 * (1 << attempt));
	}

	private static Exception CreateHttpError(
		HttpStatusCode statusCode,
		string body,
		string reasonPhrase)
	{
		var details = body?.Trim();
		try
		{
			if (JToken.Parse(body) is JObject root)
				details = ReadString(
					root["message"] ?? root["msg"]) ?? details;
		}
		catch (JsonException)
		{
		}
		if (details.IsEmpty())
			details = reasonPhrase;
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"WazirX HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null,
			statusCode);
	}
}
