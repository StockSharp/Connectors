namespace StockSharp.DeltaExchangeIndia.Native;

sealed class DeltaExchangeIndiaRestClient : BaseLogReceiver
{
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new();
	private readonly string _key;
	private readonly string _secret;
	private readonly SemaphoreSlim _requestSync = new(1, 1);
	private long _lastTimestamp;

	public DeltaExchangeIndiaRestClient(
		string endpoint,
		SecureString key,
		SecureString secret)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			_endpoint.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Delta Exchange India REST endpoint must be an " +
					"absolute HTTP URL.",
				nameof(endpoint));
		_key = key.UnSecure();
		_secret = secret.UnSecure();
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-DeltaExchangeIndia-Connector/1.0");
	}

	public override string Name => "DeltaExchangeIndia_REST";

	public bool IsCredentialsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<DeltaProduct[]> GetProductsAsync(
		CancellationToken cancellationToken)
	{
		var result = new List<DeltaProduct>();
		string after = null;
		do
		{
			var query = Query(
				("states", "live,upcoming"),
				("page_size", "100"),
				("after", after));
			var root = await SendAsync(
				HttpMethod.Get,
				"/v2/products",
				query,
				null,
				false,
				cancellationToken);
			result.AddRange(
				(root["result"] as JArray ?? [])
					.OfType<JObject>()
					.Select(ParseProduct)
					.Where(static product => product is not null));
			after = root["meta"]?.Value<string>("after");
		}
		while (!after.IsEmpty());
		return [.. result
			.GroupBy(static product => product.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())];
	}

	public async ValueTask<DeltaTicker> GetTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			$"/v2/tickers/{EscapePath(symbol)}",
			[],
			null,
			false,
			cancellationToken);
		return ParseTicker(root["result"] as JObject);
	}

	public async ValueTask<DeltaBook> GetOrderBookAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			$"/v2/l2orderbook/{EscapePath(symbol)}",
			[],
			null,
			false,
			cancellationToken);
		return ParseBook(root["result"] as JObject, depth);
	}

	public async ValueTask<DeltaTrade[]> GetTradesAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			$"/v2/trades/{EscapePath(symbol)}",
			[],
			null,
			false,
			cancellationToken);
		return ParseTrades(root["result"], symbol);
	}

	public async ValueTask<DeltaCandle[]> GetCandlesAsync(
		string symbol,
		TimeSpan timeFrame,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
	{
		var end = (to ?? DateTime.UtcNow).ToUniversalTime();
		var maximumSpan = TimeSpan.FromTicks(
			timeFrame.Ticks * 1999);
		var start = (from ?? end.Subtract(maximumSpan))
			.ToUniversalTime();
		if (end - start > maximumSpan)
			start = end.Subtract(maximumSpan);
		var root = await SendAsync(
			HttpMethod.Get,
			"/v2/history/candles",
			Query(
				("resolution",
					DeltaExchangeIndiaExtensions.ToResolution(
						timeFrame)),
				("symbol", symbol.ThrowIfEmpty(nameof(symbol))),
				("start", new DateTimeOffset(start)
					.ToUnixTimeSeconds().ToString(
						CultureInfo.InvariantCulture)),
				("end", new DateTimeOffset(end)
					.ToUnixTimeSeconds().ToString(
						CultureInfo.InvariantCulture))),
			null,
			false,
			cancellationToken);
		return ParseCandles(root["result"], symbol, timeFrame);
	}

	public async ValueTask<DeltaBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			"/v2/wallet/balances",
			[],
			null,
			true,
			cancellationToken);
		return ParseBalances(root["result"]);
	}

	public async ValueTask<DeltaPosition[]> GetPositionsAsync(
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			"/v2/positions/margined",
			[],
			null,
			true,
			cancellationToken);
		return ParsePositions(root["result"]);
	}

	public async ValueTask<DeltaOrder[]> GetOpenOrdersAsync(
		int? productId,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			"/v2/orders",
			Query(
				("product_ids", productId?.ToString(
					CultureInfo.InvariantCulture)),
				("states", "open,pending"),
				("page_size", "50")),
			null,
			true,
			cancellationToken);
		return ParseOrders(root["result"]);
	}

	public async ValueTask<DeltaOrder[]> GetOrderHistoryAsync(
		int? productId,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			"/v2/orders/history",
			Query(
				("product_ids", productId?.ToString(
					CultureInfo.InvariantCulture)),
				("start_time", ToMicroseconds(from)),
				("end_time", ToMicroseconds(to)),
				("page_size", limit.Max(1).Min(50).ToString(
					CultureInfo.InvariantCulture))),
			null,
			true,
			cancellationToken);
		return ParseOrders(root["result"]);
	}

	public async ValueTask<DeltaFill[]> GetFillsAsync(
		int? productId,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			"/v2/fills",
			Query(
				("product_ids", productId?.ToString(
					CultureInfo.InvariantCulture)),
				("start_time", ToMicroseconds(from)),
				("end_time", ToMicroseconds(to)),
				("page_size", limit.Max(1).Min(50).ToString(
					CultureInfo.InvariantCulture))),
			null,
			true,
			cancellationToken);
		return ParseFills(root["result"]);
	}

	public async ValueTask<DeltaOrder> GetOrderAsync(
		long orderId,
		CancellationToken cancellationToken)
	{
		var root = await SendAsync(
			HttpMethod.Get,
			$"/v2/orders/{orderId.ToString(
				CultureInfo.InvariantCulture)}",
			[],
			null,
			true,
			cancellationToken);
		return ParseOrder(root["result"] as JObject);
	}

	public async ValueTask<DeltaOrder> PlaceOrderAsync(
		int productId,
		int size,
		Sides side,
		OrderTypes orderType,
		decimal? price,
		TimeInForce? timeInForce,
		bool postOnly,
		bool reduceOnly,
		decimal? stopPrice,
		string clientOrderId,
		CancellationToken cancellationToken)
	{
		var body = new JObject
		{
			["product_id"] = productId,
			["size"] = size,
			["side"] = side == Sides.Buy ? "buy" : "sell",
			["order_type"] = orderType == OrderTypes.Market
				? "market_order"
				: "limit_order",
			["post_only"] = postOnly,
			["reduce_only"] = reduceOnly,
			["client_order_id"] = clientOrderId,
		};
		if (orderType != OrderTypes.Market)
			body["limit_price"] = price?.ToString(
				CultureInfo.InvariantCulture);
		if (timeInForce is not null)
			body["time_in_force"] =
				DeltaExchangeIndiaExtensions.ToTimeInForce(
					timeInForce.Value);
		if (stopPrice is > 0)
		{
			body["stop_order_type"] = "stop_loss_order";
			body["stop_price"] = stopPrice.Value.ToString(
				CultureInfo.InvariantCulture);
		}
		var root = await SendAsync(
			HttpMethod.Post,
			"/v2/orders",
			[],
			body,
			true,
			cancellationToken);
		return ParseOrder(root["result"] as JObject);
	}

	public async ValueTask<DeltaOrder> EditOrderAsync(
		long orderId,
		int productId,
		int size,
		decimal? price,
		bool postOnly,
		decimal? stopPrice,
		CancellationToken cancellationToken)
	{
		var body = new JObject
		{
			["id"] = orderId,
			["product_id"] = productId,
			["size"] = size,
			["post_only"] = postOnly,
		};
		if (price is > 0)
			body["limit_price"] = price.Value.ToString(
				CultureInfo.InvariantCulture);
		if (stopPrice is > 0)
			body["stop_price"] = stopPrice.Value.ToString(
				CultureInfo.InvariantCulture);
		var root = await SendAsync(
			HttpMethod.Put,
			"/v2/orders",
			[],
			body,
			true,
			cancellationToken);
		return ParseOrder(root["result"] as JObject);
	}

	public async ValueTask<DeltaOrder> CancelOrderAsync(
		long? orderId,
		string clientOrderId,
		int productId,
		CancellationToken cancellationToken)
	{
		var body = new JObject
		{
			["product_id"] = productId,
		};
		if (orderId is > 0)
			body["id"] = orderId.Value;
		else
			body["client_order_id"] =
				clientOrderId.ThrowIfEmpty(nameof(clientOrderId));
		var root = await SendAsync(
			HttpMethod.Delete,
			"/v2/orders",
			[],
			body,
			true,
			cancellationToken);
		return ParseOrder(root["result"] as JObject);
	}

	public async ValueTask<DeltaOrder[]> CancelAllOrdersAsync(
		int? productId,
		CancellationToken cancellationToken)
	{
		var body = productId is > 0
			? new JObject
			{
				["product_id"] = productId.Value,
			}
			: new JObject();
		var root = await SendAsync(
			HttpMethod.Delete,
			"/v2/orders/all",
			[],
			body,
			true,
			cancellationToken);
		return ParseOrders(root["result"]);
	}

	internal static string GenerateSignature(
		string method,
		string timestamp,
		string path,
		string queryString,
		string body,
		string secret)
	{
		var payload =
			method.ThrowIfEmpty(nameof(method)).ToUpperInvariant() +
			timestamp.ThrowIfEmpty(nameof(timestamp)) +
			path.ThrowIfEmpty(nameof(path)) +
			(queryString ?? string.Empty) +
			(body ?? string.Empty);
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToHexString(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)))
			.ToLowerInvariant();
	}

	internal static DeltaProduct[] DeserializeProducts(string json)
		=> [.. GetResult(json)
			.Children<JObject>()
			.Select(ParseProduct)
			.Where(static product => product is not null)];

	internal static DeltaTicker DeserializeTicker(string json)
		=> ParseTicker(GetResult(json) as JObject);

	internal static DeltaBook DeserializeBook(
		string json,
		int depth = int.MaxValue)
		=> ParseBook(GetResult(json) as JObject, depth);

	internal static DeltaTrade[] DeserializeTrades(
		string json,
		string symbol)
		=> ParseTrades(GetResult(json), symbol);

	internal static DeltaCandle[] DeserializeCandles(
		string json,
		string symbol,
		TimeSpan timeFrame)
		=> ParseCandles(GetResult(json), symbol, timeFrame);

	internal static DeltaOrder[] DeserializeOrders(string json)
		=> ParseOrders(GetResult(json));

	internal static DeltaBalance[] DeserializeBalances(string json)
		=> ParseBalances(GetResult(json));

	internal static DeltaPosition[] DeserializePositions(string json)
		=> ParsePositions(GetResult(json));

	internal static DeltaFill[] DeserializeFills(string json)
		=> ParseFills(GetResult(json));

	internal static DeltaProduct ParseProduct(JObject value)
	{
		if (value is null)
			return null;
		var symbol = value.Value<string>("symbol");
		if (symbol.IsEmpty())
			return null;
		return new()
		{
			Id = Int(value["id"]),
			Symbol = symbol,
			Description = value.Value<string>("description"),
			ContractType = value.Value<string>("contract_type"),
			State = value.Value<string>("state"),
			TradingStatus = value.Value<string>("trading_status"),
			UnderlyingAsset =
				value["underlying_asset"]?.Value<string>("symbol") ??
				value.Value<string>("underlying_asset_symbol"),
			QuotingAsset =
				value["quoting_asset"]?.Value<string>("symbol"),
			SettlingAsset =
				value["settling_asset"]?.Value<string>("symbol"),
			PriceStep = Decimal(value["tick_size"]) ?? 0,
			ContractValue = Decimal(value["contract_value"]) ?? 0,
			Strike = Decimal(value["strike_price"]),
			Expiry = Time(value["settlement_time"]),
		};
	}

	internal static DeltaTicker ParseTicker(JObject value)
	{
		if (value is null)
			return null;
		var quotes = value["quotes"];
		return new()
		{
			Symbol = value.Value<string>("symbol"),
			Time = Time(value["timestamp"]) ??
				Time(value["time"]) ?? DateTime.UtcNow,
			Open = Decimal(value["open"]),
			High = Decimal(value["high"]),
			Low = Decimal(value["low"]),
			Last = Decimal(value["close"]),
			MarkPrice = Decimal(value["mark_price"]),
			SpotPrice = Decimal(value["spot_price"]),
			BestBid = Decimal(quotes?["best_bid"]),
			BestAsk = Decimal(quotes?["best_ask"]),
			BidVolume = Decimal(quotes?["bid_size"]),
			AskVolume = Decimal(quotes?["ask_size"]),
			Volume = Decimal(value["volume"]),
			OpenInterest = Decimal(value["oi_contracts"]) ??
				Decimal(value["oi"]),
			FundingRate = Decimal(value["funding_rate"]),
		};
	}

	internal static DeltaBook ParseBook(
		JObject value,
		int depth)
	{
		if (value is null)
			return null;
		depth = depth.Max(1);
		return new()
		{
			Symbol = value.Value<string>("symbol") ??
				value.Value<string>("sy"),
			Time = Time(value["last_updated_at"]) ??
				Time(value["ts"]) ?? DateTime.UtcNow,
			Bids = ParseRestQuotes(value["buy"] ?? value["b"], depth),
			Asks = ParseRestQuotes(value["sell"] ?? value["a"], depth),
		};
	}

	internal static DeltaTrade[] ParseTrades(
		JToken value,
		string symbol)
		=> [.. AsArray(value)
			.OfType<JObject>()
			.Select(item =>
			{
				var time = Time(item["timestamp"] ?? item["t"]) ??
					DateTime.UtcNow;
				var price = Decimal(item["price"] ?? item["p"]) ?? 0;
				var volume = Decimal(item["size"] ?? item["s"]) ?? 0;
				var buyerRole = item.Value<string>("buyer_role") ??
					item.Value<string>("r");
				var sideText = item.Value<string>("side");
				var side = !sideText.IsEmpty()
					? sideText.EqualsIgnoreCase("sell")
						? Sides.Sell
						: Sides.Buy
					: buyerRole is "maker" or "m"
						? Sides.Sell
						: Sides.Buy;
				var actualSymbol = item.Value<string>("symbol") ??
					item.Value<string>("sy") ?? symbol;
				var id = item.Value<string>("id") ??
					$"{new DateTimeOffset(time).ToUnixTimeMilliseconds()}:" +
					$"{price.ToString(CultureInfo.InvariantCulture)}:" +
					$"{volume.ToString(CultureInfo.InvariantCulture)}:" +
					side;
				return new DeltaTrade
				{
					Id = id,
					Symbol = actualSymbol,
					Time = time,
					Price = price,
					Volume = volume,
					Side = side,
				};
			})
			.Where(static trade =>
				!trade.Symbol.IsEmpty() &&
				trade.Price > 0 &&
				trade.Volume > 0)];

	internal static DeltaCandle[] ParseCandles(
		JToken value,
		string symbol,
		TimeSpan timeFrame)
		=> [.. AsArray(value)
			.OfType<JObject>()
			.Select(item => new DeltaCandle
			{
				Symbol = item.Value<string>("sy") ?? symbol,
				TimeFrame = timeFrame,
				OpenTime = Time(item["time"] ?? item["ts"]) ??
					DateTime.UtcNow,
				Open = Decimal(item["open"] ?? item["o"]) ?? 0,
				High = Decimal(item["high"] ?? item["h"]) ?? 0,
				Low = Decimal(item["low"] ?? item["l"]) ?? 0,
				Close = Decimal(item["close"] ?? item["c"]) ?? 0,
				Volume = Decimal(item["volume"] ?? item["v"]) ?? 0,
			})
			.Where(static candle =>
				!candle.Symbol.IsEmpty() &&
				candle.OpenTime != default)];

	internal static DeltaBalance[] ParseBalances(JToken value)
		=> [.. AsArray(value)
			.OfType<JObject>()
			.Select(item =>
			{
				var current = Decimal(item["balance"]) ?? 0;
				var available =
					Decimal(item["available_balance"]) ?? current;
				return new DeltaBalance
				{
					Asset = item.Value<string>("asset_symbol") ??
						item["asset"]?.Value<string>("symbol"),
					Current = current,
					Available = available,
					Blocked =
						Decimal(item["blocked_margin"]) ??
						(current - available).Max(0),
				};
			})
			.Where(static balance => !balance.Asset.IsEmpty())];

	internal static DeltaPosition[] ParsePositions(JToken value)
		=> [.. AsArray(value)
			.OfType<JObject>()
			.Select(ParsePosition)
			.Where(static position =>
				position is not null &&
				!position.Symbol.IsEmpty())];

	internal static DeltaPosition ParsePosition(JObject item)
	{
		if (item is null)
			return null;
		return new()
		{
			ProductId = Int(item["product_id"]),
			Symbol = item.Value<string>("product_symbol") ??
				item.Value<string>("symbol"),
			Size = Decimal(item["size"]) ?? 0,
			EntryPrice = Decimal(item["entry_price"]) ?? 0,
			LiquidationPrice =
				Decimal(item["liquidation_price"]) ?? 0,
			Margin = Decimal(item["margin"]) ?? 0,
			RealizedPnl = Decimal(item["realized_pnl"]) ?? 0,
			UnrealizedPnl =
				Decimal(item["unrealized_pnl"]) ??
				Decimal(item["unrealized_cashflow"]) ?? 0,
		};
	}

	internal static DeltaOrder[] ParseOrders(JToken value)
		=> [.. AsArray(value)
			.OfType<JObject>()
			.Select(ParseOrder)
			.Where(static order => order is not null)];

	internal static DeltaOrder ParseOrder(JObject item)
	{
		if (item is null)
			return null;
		var id = Long(item["id"] ?? item["order_id"]);
		if (id <= 0)
			return null;
		var orderType = item.Value<string>("order_type");
		var stopType = item.Value<string>("stop_order_type");
		return new()
		{
			Id = id,
			ClientOrderId = item.Value<string>("client_order_id"),
			ProductId = Int(item["product_id"]),
			Symbol = item.Value<string>("product_symbol") ??
				item.Value<string>("symbol") ??
				item["product"]?.Value<string>("symbol"),
			Side = item.Value<string>("side")
				.EqualsIgnoreCase("sell")
					? Sides.Sell
					: Sides.Buy,
			OrderType = !stopType.IsEmpty()
				? OrderTypes.Conditional
				: orderType.EqualsIgnoreCase("market_order")
					? OrderTypes.Market
					: OrderTypes.Limit,
			State = ParseOrderState(item.Value<string>("state")),
			Price = Decimal(item["limit_price"]) ?? 0,
			StopPrice = Decimal(item["stop_price"]) ?? 0,
			Volume = Decimal(item["size"]) ?? 0,
			Balance = Decimal(item["unfilled_size"]) ?? 0,
			AveragePrice =
				Decimal(item["average_fill_price"]) ?? 0,
			ReduceOnly = item.Value<bool?>("reduce_only") ?? false,
			TimeInForce = ParseTimeInForce(
				item.Value<string>("time_in_force")),
			CreatedAt = Time(item["created_at"]) ??
				Time(item["timestamp"]) ?? DateTime.UtcNow,
			UpdatedAt = Time(item["updated_at"]) ??
				Time(item["timestamp"]) ?? DateTime.UtcNow,
		};
	}

	internal static DeltaFill[] ParseFills(JToken value)
		=> [.. AsArray(value)
			.OfType<JObject>()
			.Select(ParseFill)
			.Where(static fill => fill is not null)];

	internal static DeltaFill ParseFill(JObject item)
	{
		if (item is null)
			return null;
		var id = item.Value<string>("id") ??
			item.Value<string>("fill_id") ??
			item.Value<string>("f");
		if (id.IsEmpty())
			return null;
		return new()
		{
			Id = id,
			OrderId = Long(item["order_id"] ?? item["o"]),
			ClientOrderId = item.Value<string>("client_order_id") ??
				item.Value<string>("c"),
			ProductId = Int(item["product_id"]),
			Symbol = item.Value<string>("product_symbol") ??
				item.Value<string>("symbol") ??
				item.Value<string>("sy"),
			Side = (item.Value<string>("side") ??
				item.Value<string>("S")).EqualsIgnoreCase("sell")
					? Sides.Sell
					: Sides.Buy,
			Price = Decimal(item["price"] ?? item["p"]) ?? 0,
			Volume = Decimal(item["size"] ?? item["s"]) ?? 0,
			Commission = Decimal(item["commission"]) ?? 0,
			CommissionCurrency =
				item.Value<string>("commission_asset_symbol") ??
				item.Value<string>("commission_currency"),
			Time = Time(item["created_at"] ??
				item["timestamp"] ?? item["t"]) ?? DateTime.UtcNow,
		};
	}

	private async ValueTask<JObject> SendAsync(
		HttpMethod method,
		string path,
		KeyValuePair<string, string>[] query,
		JToken body,
		bool auth,
		CancellationToken cancellationToken)
	{
		if (auth && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Delta Exchange India API key and secret are " +
					"required for private operations.");
		path = path.ThrowIfEmpty(nameof(path));
		var queryString = BuildQueryString(query);
		var bodyText = body?.ToString(Formatting.None) ?? string.Empty;
		await _requestSync.WaitAsync(cancellationToken);
		try
		{
			using var request = new HttpRequestMessage(
				method,
				new Uri(_endpoint,
					path.TrimStart('/') + queryString));
			request.Headers.Accept.Add(
				new MediaTypeWithQualityHeaderValue(
					"application/json"));
			if (!bodyText.IsEmpty())
				request.Content = new StringContent(
					bodyText,
					Encoding.UTF8,
					"application/json");
			if (auth)
			{
				var timestamp = NextTimestamp();
				request.Headers.TryAddWithoutValidation(
					"api-key", _key);
				request.Headers.TryAddWithoutValidation(
					"timestamp", timestamp);
				request.Headers.TryAddWithoutValidation(
					"signature",
					GenerateSignature(
						method.Method,
						timestamp,
						path,
						queryString,
						bodyText,
						_secret));
			}

			using var response = await _http.SendAsync(
				request,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
			var text = await response.Content.ReadAsStringAsync(
				cancellationToken);
			JObject root;
			try
			{
				root = JObject.Parse(text);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Delta Exchange India returned malformed JSON.",
					error);
			}
			if (!response.IsSuccessStatusCode ||
				root.Value<bool?>("success") == false)
			{
				var error = root["error"];
				throw new InvalidOperationException(
					$"Delta Exchange India API request failed " +
						$"({(int)response.StatusCode}): " +
						(error?["message"] ??
							error?["code"] ??
							root["message"] ??
							response.ReasonPhrase));
			}
			return root;
		}
		finally
		{
			_requestSync.Release();
		}
	}

	private string NextTimestamp()
	{
		while (true)
		{
			var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			var previous = Interlocked.Read(ref _lastTimestamp);
			var next = Math.Max(now, previous);
			if (Interlocked.CompareExchange(
				ref _lastTimestamp, next, previous) == previous)
				return next.ToString(CultureInfo.InvariantCulture);
		}
	}

	private static JToken GetResult(string json)
	{
		try
		{
			var root = JObject.Parse(
				json.ThrowIfEmpty(nameof(json)));
			if (root.Value<bool?>("success") == false)
				throw new InvalidDataException(
					"Delta Exchange India response reports failure.");
			return root["result"] ?? JValue.CreateNull();
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Delta Exchange India returned malformed JSON.",
				error);
		}
	}

	private static JArray AsArray(JToken value)
		=> value switch
		{
			JArray array => array,
			JObject item => new(item),
			_ => [],
		};

	private static DeltaQuote[] ParseRestQuotes(
		JToken value,
		int depth)
		=> [.. AsArray(value)
			.Take(depth)
			.Select(item => item switch
			{
				JArray array when array.Count >= 2 => new DeltaQuote
				{
					Price = Decimal(array[0]) ?? 0,
					Volume = Decimal(array[1]) ?? 0,
				},
				JObject level => new DeltaQuote
				{
					Price = Decimal(level["price"]) ?? 0,
					Volume = Decimal(level["size"]) ?? 0,
				},
				_ => null,
			})
			.Where(static quote =>
				quote is not null &&
				quote.Price > 0 &&
				quote.Volume >= 0)];

	internal static decimal? Decimal(JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		return decimal.TryParse(
			value.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: null;
	}

	internal static DateTime? Time(JToken value)
	{
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		var text = value.ToString();
		if (long.TryParse(
			text,
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var numeric))
		{
			try
			{
				if (numeric >= 100000000000000)
					return DateTimeOffset.FromUnixTimeMilliseconds(
						numeric / 1000).UtcDateTime;
				if (numeric >= 100000000000)
					return DateTimeOffset.FromUnixTimeMilliseconds(
						numeric).UtcDateTime;
				return DateTimeOffset.FromUnixTimeSeconds(
					numeric).UtcDateTime;
			}
			catch (ArgumentOutOfRangeException)
			{
				return null;
			}
		}
		return DateTimeOffset.TryParse(
			text,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var date)
				? date.UtcDateTime
				: null;
	}

	private static int Int(JToken value)
		=> int.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static long Long(JToken value)
		=> long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static OrderStates ParseOrderState(string state)
		=> state?.ToLowerInvariant() switch
		{
			"pending" => OrderStates.Pending,
			"open" => OrderStates.Active,
			"closed" or "cancelled" => OrderStates.Done,
			"rejected" => OrderStates.Failed,
			_ => OrderStates.None,
		};

	private static TimeInForce? ParseTimeInForce(string value)
		=> value?.ToLowerInvariant() switch
		{
			"gtc" => TimeInForce.PutInQueue,
			"ioc" => TimeInForce.CancelBalance,
			"fok" => TimeInForce.MatchOrCancel,
			_ => null,
		};

	private static string ToMicroseconds(DateTime? value)
		=> value is null
			? null
			: (new DateTimeOffset(value.Value.ToUniversalTime())
				.ToUnixTimeMilliseconds() * 1000)
				.ToString(CultureInfo.InvariantCulture);

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(
			value.ThrowIfEmpty(nameof(value)).Trim()
				.ToUpperInvariant());

	private static KeyValuePair<string, string>[] Query(
		params (string Name, string Value)[] values)
		=> [.. values
			.Where(static value => !value.Value.IsEmpty())
			.Select(static value =>
				new KeyValuePair<string, string>(
					value.Name, value.Value))];

	private static string BuildQueryString(
		IEnumerable<KeyValuePair<string, string>> query)
	{
		var values = (query ?? [])
			.Where(static pair => !pair.Value.IsEmpty())
			.Select(static pair =>
				$"{Uri.EscapeDataString(pair.Key)}=" +
				Uri.EscapeDataString(pair.Value))
			.ToArray();
		return values.Length == 0
			? string.Empty
			: "?" + string.Join("&", values);
	}
}
