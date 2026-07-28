namespace StockSharp.Coincall.Native;

sealed class CoincallRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly CoincallProductTypes _productType;
	private readonly HttpClient _http = new();
	private readonly string _key;
	private readonly string _secret;
	private readonly long _requestValidityMilliseconds;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private long _lastTimestamp;

	public CoincallRestClient(
		string endpoint,
		CoincallProductTypes productType,
		SecureString key,
		SecureString secret,
		TimeSpan requestValidityWindow)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Coincall REST endpoint must be an absolute HTTP URL.",
				nameof(endpoint));
		if (requestValidityWindow <= TimeSpan.Zero)
			throw new ArgumentOutOfRangeException(
				nameof(requestValidityWindow));
		_productType = productType;
		_key = key.UnSecure();
		_secret = secret.UnSecure();
		_requestValidityMilliseconds =
			requestValidityWindow.TotalMilliseconds
				.Round()
				.Max(1)
				.To<long>();
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Coincall-Connector/1.0");
	}

	public override string Name => "Coincall_REST";

	public bool IsCredentialsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<CoincallInstrument[]> GetInstrumentsAsync(
		CancellationToken cancellationToken)
	{
		if (_productType == CoincallProductTypes.Futures)
		{
			var data = await SendAsync(
				HttpMethod.Get,
				"/open/futures/market/symbol/v1",
				[],
				null,
				false,
				cancellationToken);
			return ParseInstruments(data, _productType);
		}

		var config = await SendAsync(
			HttpMethod.Get,
			"/open/public/config/v1",
			[],
			null,
			false,
			cancellationToken);
		var bases = (config?["optionConfig"] as JObject)?
			.Properties()
			.Select(property =>
				property.Value.Value<string>("base") ??
					property.Name[..^Math.Min(3, property.Name.Length)])
			.Where(static value => !value.IsEmpty())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray() ?? ["BTC", "ETH"];
		var instruments = new List<CoincallInstrument>();
		foreach (var baseCurrency in bases)
		{
			var data = await SendAsync(
				HttpMethod.Get,
				$"/open/option/getInstruments/" +
					EscapePath(baseCurrency),
				[],
				null,
				false,
				cancellationToken);
			instruments.AddRange(
				ParseInstruments(data, _productType));
		}
		return [.. instruments
			.GroupBy(
				static instrument => instrument.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())];
	}

	public async ValueTask<CoincallInstrument> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		var data = _productType == CoincallProductTypes.Options
			? await SendAsync(
				HttpMethod.Get,
				$"/open/option/detail/v1/{EscapePath(symbol)}",
				[],
				null,
				false,
				cancellationToken)
			: await SendAsync(
				HttpMethod.Get,
				"/open/futures/market/symbol/v1",
				[],
				null,
				false,
				cancellationToken);
		return ParseInstruments(data, _productType)
			.FirstOrDefault(instrument =>
				instrument.Symbol.EqualsIgnoreCase(symbol));
	}

	public async ValueTask<CoincallBook> GetOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		var section = ProductSection;
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{section}/order/orderbook/v1/" +
				EscapePath(symbol),
			[],
			null,
			false,
			cancellationToken);
		return ParseBook(data, symbol, depth);
	}

	public async ValueTask<CoincallTrade[]> GetTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/trade/lasttrade/v1/" +
				EscapePath(symbol),
			[],
			null,
			false,
			cancellationToken);
		return ParseTrades(data, symbol);
	}

	public async ValueTask<CoincallCandle[]> GetCandlesAsync(
		string symbol,
		TimeSpan timeFrame,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
	{
		var version = _productType == CoincallProductTypes.Options
			? "v1"
			: "v2";
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/market/kline/history/" +
				$"{version}/{EscapePath(symbol)}",
			Query(
				("start", ToMilliseconds(from)),
				("end", ToMilliseconds(to)),
				("period", timeFrame.ToPeriod()
					.ToUpperInvariant())),
			null,
			false,
			cancellationToken);
		return ParseCandles(data, symbol, timeFrame);
	}

	public async ValueTask<CoincallAccount[]> GetAccountsAsync(
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			HttpMethod.Get,
			"/open/account/summary/v1",
			[],
			null,
			true,
			cancellationToken);
		return ParseAccounts(data);
	}

	public async ValueTask<CoincallPosition[]> GetPositionsAsync(
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/position/get/v1",
			[],
			null,
			true,
			cancellationToken);
		return ParsePositions(data);
	}

	public async ValueTask<CoincallOrder[]> GetOpenOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		var query = new List<KeyValuePair<string, string>>
		{
			new("page", "1"),
			new("pageSize", "500"),
		};
		if (_productType == CoincallProductTypes.Futures &&
			!symbol.IsEmpty())
			query.Add(new("symbol", symbol));
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/order/pending/v1",
			[.. query],
			null,
			true,
			cancellationToken);
		return ParseOrders(data);
	}

	public async ValueTask<CoincallOrder[]> GetOrderHistoryAsync(
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/order/history/v1",
			Query(
				("pageSize", limit.Max(1).Min(500).ToString(
					CultureInfo.InvariantCulture)),
				("startTime", ToMilliseconds(from)),
				("endTime", ToMilliseconds(to))),
			null,
			true,
			cancellationToken);
		return ParseOrders(data);
	}

	public async ValueTask<CoincallOrder> GetOrderAsync(
		long? orderId,
		long? clientOrderId,
		CancellationToken cancellationToken)
	{
		if (orderId is not > 0 && clientOrderId is not > 0)
			throw new ArgumentException(
				"Coincall order id or client order id is required.");
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/order/singleQuery/v1",
			Query(
				("orderId", orderId?.ToString(
					CultureInfo.InvariantCulture)),
				("clientOrderId", clientOrderId?.ToString(
					CultureInfo.InvariantCulture))),
			null,
			true,
			cancellationToken);
		return ParseOrders(data).FirstOrDefault();
	}

	public async ValueTask<CoincallFill[]> GetFillsAsync(
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/trade/history/v1",
			Query(
				("pageSize", limit.Max(1).Min(500).ToString(
					CultureInfo.InvariantCulture)),
				("startTime", ToMilliseconds(from)),
				("endTime", ToMilliseconds(to))),
			null,
			true,
			cancellationToken);
		return ParseFills(data);
	}

	public async ValueTask<long> PlaceOrderAsync(
		string symbol,
		long clientOrderId,
		Sides side,
		OrderTypes orderType,
		decimal quantity,
		decimal? price,
		TimeInForce? timeInForce,
		bool postOnly,
		bool reduceOnly,
		decimal? triggerPrice,
		CancellationToken cancellationToken)
	{
		var tradeType = triggerPrice is > 0
			? orderType == OrderTypes.Market ? 5 : 4
			: postOnly
				? 3
				: orderType == OrderTypes.Market ? 2 : 1;
		var body = new JObject
		{
			["clientOrderId"] = clientOrderId,
			["symbol"] = symbol,
			["qty"] = quantity,
			["tradeSide"] = side == Sides.Buy ? 1 : 2,
			["tradeType"] = tradeType,
			["timeInForce"] = ToTimeInForce(timeInForce),
			["reduceOnly"] = reduceOnly ? 1 : 0,
		};
		if (orderType != OrderTypes.Market)
			body["price"] = price;
		if (triggerPrice is > 0)
			body["triggerPrice"] = triggerPrice;
		var data = await SendAsync(
			HttpMethod.Post,
			$"/open/{ProductSection}/order/create/v1",
			[],
			body,
			true,
			cancellationToken);
		return Long(data) ?? throw new InvalidDataException(
			"Coincall accepted an order without returning its id.");
	}

	public async ValueTask<long> ModifyOrderAsync(
		long orderId,
		string symbol,
		decimal quantity,
		decimal? price,
		CancellationToken cancellationToken)
	{
		var body = new JObject
		{
			["orderId"] = orderId,
			["symbol"] = symbol,
			["qty"] = quantity,
			["price"] = price,
		};
		var data = await SendAsync(
			HttpMethod.Post,
			$"/open/{ProductSection}/order/modify/v1",
			[],
			body,
			true,
			cancellationToken);
		return Long(data) ?? orderId;
	}

	public async ValueTask CancelOrderAsync(
		long? orderId,
		long? clientOrderId,
		CancellationToken cancellationToken)
	{
		if (orderId is not > 0 && clientOrderId is not > 0)
			throw new ArgumentException(
				"Coincall order id or client order id is required.");
		var body = new JObject
		{
			["orderId"] = orderId,
			["clientOrderId"] = clientOrderId,
		};
		_ = await SendAsync(
			HttpMethod.Post,
			$"/open/{ProductSection}/order/cancel/v1",
			[],
			body,
			true,
			cancellationToken);
	}

	public async ValueTask CancelAllOrdersAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol.ThrowIfEmpty(nameof(symbol));
		_ = await SendAsync(
			HttpMethod.Get,
			$"/open/{ProductSection}/order/cancelOpenOrders/v1/" +
				EscapePath(symbol),
			[],
			null,
			true,
			cancellationToken);
	}

	internal static string GenerateSignature(
		string method,
		string path,
		IEnumerable<KeyValuePair<string, string>> parameters,
		string apiKey,
		long timestamp,
		long validityMilliseconds,
		string secret)
	{
		var values = (parameters ?? [])
			.Where(static pair =>
				!pair.Key.IsEmpty() && pair.Value is not null)
			.OrderBy(static pair => pair.Key, StringComparer.Ordinal)
			.Select(static pair => $"{pair.Key}={pair.Value}")
			.ToList();
		values.Add($"uuid={apiKey}");
		values.Add($"ts={timestamp.ToString(
			CultureInfo.InvariantCulture)}");
		values.Add($"x-req-ts-diff={validityMilliseconds.ToString(
			CultureInfo.InvariantCulture)}");
		var prehash =
			method.ThrowIfEmpty(nameof(method)).ToUpperInvariant() +
			path.ThrowIfEmpty(nameof(path)) +
			"?" +
			string.Join("&", values);
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(prehash)));
	}

	internal static CoincallInstrument[] DeserializeInstruments(
		string json,
		CoincallProductTypes productType)
		=> ParseInstruments(GetData(json), productType);

	internal static CoincallBook DeserializeBook(
		string json,
		string symbol,
		int depth = int.MaxValue)
		=> ParseBook(GetData(json), symbol, depth);

	internal static CoincallTrade[] DeserializeTrades(
		string json,
		string symbol)
		=> ParseTrades(GetData(json), symbol);

	internal static CoincallCandle[] DeserializeCandles(
		string json,
		string symbol,
		TimeSpan timeFrame)
		=> ParseCandles(GetData(json), symbol, timeFrame);

	internal static CoincallOrder[] DeserializeOrders(string json)
		=> ParseOrders(GetData(json));

	internal static CoincallFill[] DeserializeFills(string json)
		=> ParseFills(GetData(json));

	internal static CoincallAccount[] DeserializeAccounts(string json)
		=> ParseAccounts(GetData(json));

	internal static CoincallPosition[] DeserializePositions(string json)
		=> ParsePositions(GetData(json));

	private string ProductSection
		=> _productType == CoincallProductTypes.Options
			? "option"
			: "futures";

	private async ValueTask<JToken> SendAsync(
		HttpMethod method,
		string path,
		KeyValuePair<string, string>[] query,
		JObject body,
		bool signed,
		CancellationToken cancellationToken)
	{
		if (signed && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Coincall API key and secret are required.");
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			var queryString = CreateQueryString(query);
			using var request = new HttpRequestMessage(
				method,
				new Uri(
					_endpoint,
					path.TrimStart('/') + queryString));
			string bodyText = null;
			if (body is not null)
			{
				bodyText = body.ToString(Formatting.None);
				request.Content = new StringContent(
					bodyText,
					Encoding.UTF8,
					"application/json");
			}
			if (signed)
			{
				var timestamp = NextTimestamp();
				var values = body is null
					? query
					: ToSigningParameters(body);
				request.Headers.TryAddWithoutValidation(
					"X-CC-APIKEY", _key);
				request.Headers.TryAddWithoutValidation(
					"ts", timestamp.ToString(
						CultureInfo.InvariantCulture));
				request.Headers.TryAddWithoutValidation(
					"X-REQ-TS-DIFF",
					_requestValidityMilliseconds.ToString(
						CultureInfo.InvariantCulture));
				request.Headers.TryAddWithoutValidation(
					"sign",
					GenerateSignature(
						method.Method,
						path,
						values,
						_key,
						timestamp,
						_requestValidityMilliseconds,
						_secret));
			}
			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var responseText = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new InvalidDataException(
					$"Coincall HTTP {(int)response.StatusCode} " +
						$"({response.ReasonPhrase}): {responseText}");
			return GetData(responseText);
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private long NextTimestamp()
	{
		var current = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		_lastTimestamp = Math.Max(current, _lastTimestamp + 1);
		return _lastTimestamp;
	}

	private static KeyValuePair<string, string>[] ToSigningParameters(
		JObject body)
		=> [.. body.Properties()
			.Where(static property =>
				property.Value.Type is not (
					JTokenType.Null or JTokenType.Undefined))
			.Select(static property =>
				new KeyValuePair<string, string>(
					property.Name,
					property.Value.Type == JTokenType.String
						? property.Value.Value<string>()
						: property.Value.ToString(Formatting.None)))];

	private static string CreateQueryString(
		IEnumerable<KeyValuePair<string, string>> query)
	{
		var values = (query ?? [])
			.Where(static pair =>
				!pair.Key.IsEmpty() && pair.Value is not null)
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" +
					Uri.EscapeDataString(pair.Value))
			.ToArray();
		return values.Length == 0
			? string.Empty
			: "?" + string.Join("&", values);
	}

	private static KeyValuePair<string, string>[] Query(
		params (string Key, string Value)[] values)
		=> [.. values
			.Where(static pair =>
				!pair.Key.IsEmpty() && pair.Value is not null)
			.Select(static pair =>
				new KeyValuePair<string, string>(
					pair.Key, pair.Value))];

	private static string ToMilliseconds(DateTime? value)
		=> value is null
			? null
			: new DateTimeOffset(
				value.Value.ToUniversalTime())
				.ToUnixTimeMilliseconds()
				.ToString(CultureInfo.InvariantCulture);

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(
			value.ThrowIfEmpty(nameof(value)));

	private static string ToTimeInForce(TimeInForce? value)
		=> value switch
		{
			null or TimeInForce.PutInQueue => "GTC",
			TimeInForce.MatchOrCancel => "IOC",
			TimeInForce.CancelBalance => "FOK",
			_ => throw new NotSupportedException(
				$"Coincall does not support {value} time in force."),
		};

	private static JToken GetData(string json)
	{
		JObject root;
		try
		{
			root = JObject.Parse(
				json.ThrowIfEmpty(nameof(json)));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coincall returned invalid JSON.", error);
		}
		var code = Long(root["code"]);
		if (code != 0)
			throw new InvalidDataException(
				$"Coincall request failed ({code}): " +
					(root.Value<string>("msg") ??
						root.Value<string>("message")));
		return root["data"];
	}

	private static CoincallInstrument[] ParseInstruments(
		JToken data,
		CoincallProductTypes productType)
		=> [.. AsArray(data)
			.OfType<JObject>()
			.Select(value => ParseInstrument(value, productType))
			.Where(static instrument => instrument is not null)];

	private static CoincallInstrument ParseInstrument(
		JObject value,
		CoincallProductTypes productType)
	{
		var symbol = String(
			value["symbol"] ??
				value["symbolName"] ??
				value["ticker_id"]);
		if (symbol.IsEmpty())
			return null;
		symbol = symbol.Replace("-", string.Empty)
			.Replace("_", string.Empty)
			.ToUpperInvariant();
		if (productType == CoincallProductTypes.Options &&
			value["symbolName"] is not null)
			symbol = String(value["symbolName"]).ToUpperInvariant();
		var baseCurrency = String(
			value["baseToken"] ??
				value["baseCurrency"] ??
				value["base_currency"]);
		var quoteCurrency = String(
			value["quoteToken"] ??
				value["quoteCurrency"] ??
				value["quote_currency"]);
		if (quoteCurrency.IsEmpty())
			quoteCurrency = "USD";
		if (baseCurrency.IsEmpty())
		{
			var index = symbol.IndexOf(quoteCurrency,
				StringComparison.OrdinalIgnoreCase);
			baseCurrency = index > 0
				? symbol[..index]
				: symbol.Split('-')[0];
		}
		OptionTypes? optionType = productType ==
			CoincallProductTypes.Options
				? symbol.EndsWith("-P",
					StringComparison.OrdinalIgnoreCase)
					? OptionTypes.Put
					: OptionTypes.Call
				: null;
		return new()
		{
			ProductType = productType,
			Symbol = symbol,
			DisplayName = String(
				value["displayName"] ?? value["symbolName"]),
			BaseCurrency = baseCurrency,
			QuoteCurrency = quoteCurrency,
			IsActive = Bool(value["isActive"]) ??
				!String(value["state"]).EqualsIgnoreCase("offline"),
			PriceStep = Decimal(
				value["tickSize"] ?? value["priceStep"]) ?? 0,
			VolumeStep = Decimal(
				value["minQty"] ?? value["qtyStep"]) ?? 0,
			MinVolume = Decimal(value["minQty"]),
			Strike = Decimal(value["strike"]),
			Expiry = Time(
				value["expirationTimestamp"] ??
					value["endTime"]),
			OptionType = optionType,
			LastPrice = Decimal(
				value["lastPrice"] ?? value["last_price"]),
			MarkPrice = Decimal(value["markPrice"]),
			IndexPrice = Decimal(value["indexPrice"]),
			BestBid = Decimal(value["bid"]),
			BestAsk = Decimal(value["ask"]),
			High = Decimal(
				value["price24hHigh"] ?? value["high"]),
			Low = Decimal(
				value["price24hLow"] ?? value["low"]),
			Volume = Decimal(
				value["volume24h"] ??
					value["base_volume"] ??
					value["volume"]),
			OpenInterest = Decimal(
				value["openInterest"] ??
					value["open_interest"]),
		};
	}

	private static CoincallBook ParseBook(
		JToken data,
		string symbol,
		int depth)
	{
		var value = data as JObject;
		if (value is null)
			return null;
		return new()
		{
			Symbol = String(
				value["symbol"] ?? value["optionName"]) ?? symbol,
			Time = Time(value["timestamp"] ?? value["ts"]) ??
				DateTime.UtcNow,
			Bids = ParseQuotes(value["bids"], depth),
			Asks = ParseQuotes(value["asks"], depth),
		};
	}

	private static CoincallQuote[] ParseQuotes(
		JToken token,
		int depth)
		=> [.. AsArray(token)
			.Take(depth.Max(1))
			.Select(ParseQuote)
			.Where(static quote =>
				quote.Price > 0 && quote.Volume >= 0)];

	private static CoincallQuote ParseQuote(JToken token)
	{
		if (token is JArray array)
			return new(
				Decimal(array.ElementAtOrDefault(0)) ?? 0,
				Decimal(array.ElementAtOrDefault(1)) ?? 0);
		return new(
			Decimal(token?["price"] ?? token?["pr"]) ?? 0,
			Decimal(token?["size"] ?? token?["sz"]) ?? 0);
	}

	private static CoincallTrade[] ParseTrades(
		JToken data,
		string symbol)
		=> [.. AsArray(data)
			.OfType<JObject>()
			.Select(value =>
			{
				var time = Time(
					value["time"] ??
						value["tradeTime"] ??
						value["ts"]) ?? DateTime.UtcNow;
				var price = Decimal(
					value["price"] ??
						value["matchPrice"] ??
						value["pr"]) ?? 0;
				var volume = Decimal(
					value["qty"] ??
						value["matchQty"] ??
						value["q"]) ?? 0;
				var side = Int(
					value["tradeSide"] ??
						value["sd"]);
				return new CoincallTrade
				{
					Id = String(
						value["tradeId"] ?? value["id"]) ??
							$"{new DateTimeOffset(time)
								.ToUnixTimeMilliseconds()}:" +
							$"{price.ToWire()}:{volume.ToWire()}",
					Symbol = String(value["symbol"] ?? value["s"]) ??
						symbol,
					Time = time,
					Price = price,
					Volume = volume,
					Side = side == 1
						? Sides.Buy
						: side == 2 ? Sides.Sell : null,
				};
			})
			.Where(static trade =>
				trade.Price > 0 && trade.Volume > 0)];

	private static CoincallCandle[] ParseCandles(
		JToken data,
		string symbol,
		TimeSpan timeFrame)
		=> [.. AsArray(data)
			.OfType<JObject>()
			.Select(value => new CoincallCandle
			{
				Symbol = String(value["symbol"] ?? value["s"]) ??
					symbol,
				OpenTime = Time(value["time"] ?? value["ts"]) ??
					DateTime.UtcNow,
				TimeFrame = timeFrame,
				Open = Decimal(value["open"]) ?? 0,
				High = Decimal(value["high"]) ?? 0,
				Low = Decimal(value["low"]) ?? 0,
				Close = Decimal(value["close"]) ?? 0,
				Volume = Decimal(value["volume"] ?? value["v"]) ?? 0,
			})
			.Where(static candle =>
				candle.Open > 0 && candle.Close > 0)];

	private static CoincallAccount[] ParseAccounts(JToken data)
		=> [.. AsArray(data?["accounts"])
			.OfType<JObject>()
			.Select(value => new CoincallAccount
			{
				Currency = String(value["coin"] ?? value["coinView"]),
				Equity = Decimal(
					value["equityAmount"] ??
						value["marginBalance"]) ?? 0,
				Available = Decimal(value["availableBalance"]) ?? 0,
				Margin = Decimal(value["imAmount"]) ?? 0,
				UnrealizedPnl =
					Decimal(value["unrealizedAmount"]) ?? 0,
			})
			.Where(static account =>
				!account.Currency.IsEmpty())];

	private static CoincallPosition[] ParsePositions(JToken data)
		=> [.. AsArray(data)
			.OfType<JObject>()
			.Select(ParsePosition)
			.Where(static position =>
				position?.Symbol.IsEmpty() == false)];

	private static CoincallPosition ParsePosition(JObject value)
	{
		if (value is null)
			return null;
		var side = Int(value["tradeSide"] ?? value["si"]) == 2
			? Sides.Sell
			: Sides.Buy;
		return new()
		{
			Id = String(value["positionId"] ?? value["id"]),
			Symbol = String(value["symbol"] ?? value["s"]),
			Time = Time(
				value["updateTime"] ?? value["ts"]) ??
					DateTime.UtcNow,
			Quantity = (Decimal(value["qty"] ?? value["q"]) ?? 0)
				.Abs(),
			AveragePrice = Decimal(
				value["avgPrice"] ?? value["ap"]) ?? 0,
			MarkPrice = Decimal(
				value["markPrice"] ?? value["mp"]) ?? 0,
			LiquidationPrice = Decimal(
				value["elp"] ?? value["liquidationPrice"]),
			InitialMargin = Decimal(
				value["initMargin"] ?? value["im"]) ?? 0,
			UnrealizedPnl = Decimal(value["upnl"]) ?? 0,
			Leverage = Decimal(value["leverage"] ?? value["le"]),
			Side = side,
		};
	}

	private static CoincallOrder[] ParseOrders(JToken data)
		=> [.. AsArray(data?["list"] ?? data)
			.OfType<JObject>()
			.Select(ParseOrder)
			.Where(static order => order?.Id > 0)];

	private static CoincallOrder ParseOrder(JObject value)
	{
		if (value is null)
			return null;
		var quantity = Decimal(
			value["qty"] ?? value["q"]) ?? 0;
		var filled = Decimal(
			value["fillQty"] ?? value["fq"]) ?? 0;
		var remaining = Decimal(
			value["remainQty"] ?? value["rq"]) ??
				(quantity - filled).Max(0);
		return new()
		{
			Id = Long(
				value["orderId"] ??
					value["oid"] ??
					value["id"]) ?? 0,
			ClientOrderId = Long(
				value["clientOrderId"] ?? value["coid"]),
			Symbol = String(value["symbol"] ?? value["s"]),
			Time = Time(
				value["createTime"] ??
					value["ct"] ??
					value["ts"]) ?? DateTime.UtcNow,
			Quantity = quantity,
			FilledQuantity = filled,
			RemainingQuantity = remaining,
			Price = Decimal(value["price"] ?? value["pr"]) ?? 0,
			AveragePrice = Decimal(
				value["avgPrice"] ?? value["ap"]) ?? 0,
			Fee = Decimal(value["fee"]),
			RealizedPnl = Decimal(value["rpnl"]),
			Side = Int(
				value["tradeSide"] ?? value["si"]) == 2
					? Sides.Sell
					: Sides.Buy,
			OrderType = Int(
				value["tradeType"] ?? value["ty"]) == 2
					? OrderTypes.Market
					: OrderTypes.Limit,
			State = ToOrderState(Int(
				value["state"] ?? value["os"])),
			TimeInForce = ToTimeInForce(
				String(value["timeInForce"]),
				Int(value["tif"])),
			ReduceOnly = Int(
				value["reduceOnly"] ?? value["ro"]) == 1,
			TriggerPrice = Decimal(value["triggerPrice"]),
		};
	}

	private static CoincallFill[] ParseFills(JToken data)
		=> [.. AsArray(data?["list"] ?? data)
			.OfType<JObject>()
			.Select(value => new CoincallFill
			{
				Id = Long(
					value["tradeId"] ??
						value["tid"] ??
						value["id"]) ?? 0,
				OrderId = Long(
					value["orderId"] ?? value["oid"]) ?? 0,
				ClientOrderId = Long(
					value["clientOrderId"] ?? value["coid"]),
				Symbol = String(value["symbol"] ?? value["s"]),
				Time = Time(value["time"] ?? value["ts"]) ??
					DateTime.UtcNow,
				Price = Decimal(
					value["price"] ??
						value["matchPrice"] ??
						value["mpr"]) ?? 0,
				Quantity = Decimal(
					value["qty"] ??
						value["matchQty"] ??
						value["mq"]) ?? 0,
				Fee = Decimal(value["fee"]),
				Side = Int(
					value["tradeSide"] ?? value["si"]) == 2
						? Sides.Sell
						: Sides.Buy,
			})
			.Where(static fill =>
				fill.Id > 0 && fill.Symbol.IsEmpty() == false)];

	private static OrderStates ToOrderState(int? value)
		=> value switch
		{
			0 or -1 or -2 => OrderStates.Active,
			1 => OrderStates.Done,
			2 => OrderStates.Active,
			3 or 4 or 5 or 6 or 10 => OrderStates.Done,
			_ => OrderStates.None,
		};

	private static TimeInForce? ToTimeInForce(
		string value,
		int? compact)
		=> value?.ToUpperInvariant() switch
		{
			"GTC" => TimeInForce.PutInQueue,
			"IOC" => TimeInForce.MatchOrCancel,
			"FOK" => TimeInForce.CancelBalance,
			_ => compact switch
			{
				0 or 1 => TimeInForce.PutInQueue,
				2 => TimeInForce.MatchOrCancel,
				3 => TimeInForce.CancelBalance,
				_ => null,
			},
		};

	private static JArray AsArray(JToken token)
		=> token switch
		{
			JArray array => array,
			JObject value => new(value),
			_ => [],
		};

	private static string String(JToken value)
		=> value?.Type is JTokenType.Null or JTokenType.Undefined
			? null
			: value?.ToString();

	private static decimal? Decimal(JToken value)
		=> decimal.TryParse(
			String(value),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static long? Long(JToken value)
		=> long.TryParse(
			String(value),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static int? Int(JToken value)
		=> int.TryParse(
			String(value),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;

	private static bool? Bool(JToken value)
		=> bool.TryParse(String(value), out var result)
			? result
			: Int(value) is int number
				? number != 0
				: null;

	private static DateTime? Time(JToken value)
	{
		var timestamp = Long(value);
		if (timestamp is null)
			return null;
		if (timestamp > 10_000_000_000_000)
			timestamp /= 1000;
		if (timestamp < 10_000_000_000)
			timestamp *= 1000;
		try
		{
			return DateTimeOffset
				.FromUnixTimeMilliseconds(timestamp.Value)
				.UtcDateTime;
		}
		catch (ArgumentOutOfRangeException)
		{
			return null;
		}
	}
}
