namespace StockSharp.ZondaCrypto.Native;

sealed class ZondaCryptoRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly ZondaCryptoAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private long _lastTimestamp;

	public ZondaCryptoRestClient(
		string endpoint,
		SecureString key,
		SecureString secret)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_authenticator = new(key, secret);
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
			"StockSharp-ZondaCrypto-Connector/1.0");
	}

	public override string Name => "ZondaCrypto_REST";

	public bool IsCredentialsAvailable
		=> _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<ZondaCryptoTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> DeserializeTickers(await SendAsync(
			HttpMethod.Get,
			"trading/ticker",
			[],
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<ZondaCryptoTicker> GetTickerAsync(
		string marketCode,
		CancellationToken cancellationToken)
		=> DeserializeTickers(await SendAsync(
			HttpMethod.Get,
			$"trading/ticker/{EscapePath(marketCode)}",
			[],
			null,
			false,
			true,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<ZondaCryptoOrderBook> GetOrderBookAsync(
		string marketCode,
		CancellationToken cancellationToken)
		=> DeserializeOrderBook(await SendAsync(
			HttpMethod.Get,
			$"trading/orderbook/{EscapePath(marketCode)}",
			[],
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<ZondaCryptoTrade[]> GetTradesAsync(
		string marketCode,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeTrades(await SendAsync(
			HttpMethod.Get,
			$"trading/transactions/{EscapePath(marketCode)}",
			Query(("limit", limit.Max(1).Min(100).ToString(
				CultureInfo.InvariantCulture))),
			null,
			false,
			true,
			cancellationToken), marketCode);

	public async ValueTask<ZondaCryptoWallet[]> GetWalletsAsync(
		CancellationToken cancellationToken)
		=> DeserializeWallets(await SendAsync(
			HttpMethod.Get,
			"balances/BITBAY/balance",
			[],
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<ZondaCryptoOffer[]> GetOffersAsync(
		string marketCode,
		CancellationToken cancellationToken)
		=> DeserializeOffers(await SendAsync(
			HttpMethod.Get,
			marketCode.IsEmpty()
				? "trading/offer"
				: $"trading/offer/{EscapePath(marketCode)}",
			[],
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<ZondaCryptoPrivateTrade[]>
		GetPrivateTradesAsync(
			string marketCode,
			DateTime? from,
			DateTime? to,
			int limit,
			CancellationToken cancellationToken)
		=> DeserializePrivateTrades(await SendAsync(
			HttpMethod.Get,
			"trading/history/transactions",
			Query(
				("markets", marketCode),
				("fromTime", ToTimestamp(from)),
				("toTime", ToTimestamp(to)),
				("limit", limit.Max(1).Min(100).ToString(
					CultureInfo.InvariantCulture))),
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<ZondaCryptoOffer> PlaceOrderAsync(
		ZondaCryptoPlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		var isMarket = order.OrderType == OrderTypes.Market;
		var body = new JObject
		{
			["offerType"] = order.Side.ToZonda(),
			["amount"] = order.Amount.ToWire(),
			["price"] = null,
			["rate"] = isMarket ? null : order.Price.ToWire(),
			["postOnly"] = order.PostOnly,
			["mode"] = isMarket ? "market" : "limit",
			["fillOrKill"] =
				order.TimeInForce == TimeInForce.CancelBalance,
			["immediateOrCancel"] =
				order.TimeInForce == TimeInForce.MatchOrCancel,
			["firstBalanceId"] = null,
			["secondBalanceId"] = null,
		};
		var response = await SendAsync(
			HttpMethod.Post,
			$"trading/offer/{EscapePath(order.MarketCode)}",
			[],
			body,
			true,
			false,
			cancellationToken);
		return DeserializePlacedOffer(response, order);
	}

	public async ValueTask<ZondaCryptoOffer> CancelOrderAsync(
		string marketCode,
		string orderId,
		Sides side,
		decimal price,
		CancellationToken cancellationToken)
	{
		var response = await SendAsync(
			HttpMethod.Delete,
			$"trading/offer/{EscapePath(marketCode)}/" +
				$"{EscapePath(orderId)}/{side.ToZonda()}/" +
				EscapePath(price.ToWire()),
			[],
			null,
			true,
			false,
			cancellationToken);
		_ = ParseRoot(response);
		return new()
		{
			Id = orderId,
			MarketCode = marketCode,
			Side = side,
			OrderType = OrderTypes.Limit,
			Price = price,
			State = OrderStates.Done,
			CreatedAt = DateTime.UtcNow,
		};
	}

	internal static ZondaCryptoTicker[] DeserializeTickers(
		string body)
	{
		var root = ParseRoot(body);
		var tickerToken = root["ticker"] ?? root["tickers"];
		var values = new List<(JObject Value, string Code)>();
		switch (tickerToken)
		{
			case JObject value when value["market"] is not null:
				values.Add((value, null));
				break;

			case JObject map:
				foreach (var property in map.Properties())
				{
					if (property.Value is JObject ticker)
						values.Add((ticker, property.Name));
				}
				break;

			case JArray array:
				values.AddRange(array.OfType<JObject>()
					.Select(static value => (value, (string)null)));
				break;
		}
		return [.. values
			.Select(static pair => ParseTicker(
				pair.Value, pair.Code))
			.Where(static ticker =>
				ticker?.Market is not null)
			.OrderBy(static ticker =>
				ticker.Market.SecurityCode,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static ZondaCryptoOrderBook DeserializeOrderBook(
		string body)
		=> ParseOrderBook(ParseRoot(body));

	internal static ZondaCryptoTrade[] DeserializeTrades(
		string body,
		string marketCode)
	{
		var root = ParseRoot(body);
		var values = root["items"] as JArray ??
			root["transactions"] as JArray ?? [];
		return [.. values
			.OfType<JObject>()
			.Select(value => ParseTrade(value, marketCode))
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];
	}

	internal static ZondaCryptoWallet[] DeserializeWallets(
		string body)
	{
		var root = ParseRoot(body);
		var values = root["balances"] as JArray ??
			root["wallets"] as JArray;
		if (values is null && root["balance"] is JObject balance)
			values = new(balance);
		if (values is null && root["balance"] is JArray balances)
			values = balances;
		return [.. (values ?? [])
			.OfType<JObject>()
			.Select(ParseWallet)
			.Where(static wallet => wallet is not null)
			.OrderBy(static wallet => wallet.Currency,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static ZondaCryptoOffer[] DeserializeOffers(
		string body)
	{
		var root = ParseRoot(body);
		var values = root["items"] as JArray ??
			root["offers"] as JArray;
		if (values is null && root["offer"] is JObject offer)
			values = new(offer);
		return [.. (values ?? [])
			.OfType<JObject>()
			.Select(ParseOffer)
			.Where(static item => item is not null)];
	}

	internal static ZondaCryptoPrivateTrade[]
		DeserializePrivateTrades(string body)
	{
		var root = ParseRoot(body);
		return [.. (root["items"] as JArray ?? [])
			.OfType<JObject>()
			.Select(ParsePrivateTrade)
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];
	}

	internal static ZondaCryptoTicker ParseTicker(
		JObject value,
		string marketCode = null)
	{
		if (value is null)
			return null;
		var marketToken = value["market"] as JObject;
		marketCode =
			marketToken?.Value<string>("code") ??
			value.Value<string>("marketCode") ??
			value.Value<string>("market") ??
			marketCode;
		var market = ParseMarket(marketToken, marketCode);
		if (market is null)
			return null;
		return new()
		{
			Market = market,
			Time = ReadTimestamp(value["time"] ?? value["timestamp"]),
			BidPrice = ReadNullableDecimal(
				value["highestBid"] ?? value["bid"]),
			AskPrice = ReadNullableDecimal(
				value["lowestAsk"] ?? value["ask"]),
			LastPrice = ReadNullableDecimal(
				value["rate"] ?? value["last"]),
			PreviousPrice = ReadNullableDecimal(
				value["previousRate"] ?? value["previous"]),
		};
	}

	internal static ZondaCryptoOrderBook ParseOrderBook(
		JObject value)
		=> new()
		{
			Bids = ParseQuotes(value?["buy"] ?? value?["bids"], false),
			Asks = ParseQuotes(value?["sell"] ?? value?["asks"], true),
			Time = ReadTimestamp(value?["timestamp"] ?? value?["time"]),
			Sequence = ReadLong(value?["seqNo"] ?? value?["sequence"]),
		};

	internal static ZondaCryptoTrade ParseTrade(
		JObject value,
		string marketCode)
	{
		if (value is null)
			return null;
		var price = ReadDecimal(value["r"] ?? value["rate"]);
		var volume = ReadDecimal(value["a"] ?? value["amount"]);
		if (price <= 0 || volume <= 0)
			return null;
		return new()
		{
			Id = value["id"]?.ToString(),
			MarketCode =
				value.Value<string>("market") ??
				value.Value<string>("marketCode") ??
				marketCode,
			Time = ReadTimestamp(value["t"] ?? value["time"]),
			Volume = volume,
			Price = price,
			Side = (value.Value<string>("ty") ??
				value.Value<string>("type") ??
				value.Value<string>("side")).ToSide(),
		};
	}

	internal static ZondaCryptoWallet ParseWallet(
		JObject value)
	{
		if (value?.Value<string>("currency").IsEmpty() != false)
			return null;
		return new()
		{
			Id = value.Value<string>("id"),
			Currency = value.Value<string>("currency")
				.ToUpperInvariant(),
			Name = value.Value<string>("name"),
			Available = ReadDecimal(value["availableFunds"]),
			Locked = ReadDecimal(value["lockedFunds"]),
			Total = ReadDecimal(value["totalFunds"]),
		};
	}

	internal static ZondaCryptoOffer ParseOffer(
		JObject value)
	{
		if (value is null)
			return null;
		if (value["state"] is JObject state)
			value = state;
		var id =
			value["id"]?.ToString() ??
			value["offerId"]?.ToString();
		var marketCode =
			value.Value<string>("market") ??
			value.Value<string>("marketCode");
		if (id.IsEmpty() || marketCode.IsEmpty())
			return null;
		var originalAmount = ReadDecimal(
			value["startAmount"] ??
			value["amount"] ??
			value["originalAmount"]);
		var remainingAmount = ReadNullableDecimal(
			value["currentAmount"] ??
			value["remainingAmount"] ??
			value["leftAmount"]) ?? originalAmount;
		var mode =
			value.Value<string>("mode") ??
			value.Value<string>("orderType");
		var status =
			value.Value<string>("status") ??
			value.Value<string>("action") ??
			"active";
		var flags = value["flags"] as JArray;
		return new()
		{
			Id = id,
			MarketCode = marketCode,
			Side = (value.Value<string>("offerType") ??
				value.Value<string>("type") ??
				value.Value<string>("side")).ToSide(),
			OrderType = mode.ToOrderType(),
			TimeInForce = ReadTimeInForce(value, flags),
			PostOnly =
				ReadBoolean(value["postOnly"]) ||
				(flags?.Any(item =>
					item.ToString().EqualsIgnoreCase(
						"postOnly")) == true),
			State = status.ToOrderState(),
			CreatedAt = ReadNullableTimestamp(
				value["time"] ?? value["createdAt"]),
			Price = ReadDecimal(value["rate"] ?? value["price"]),
			OriginalAmount = originalAmount,
			RemainingAmount = remainingAmount,
		};
	}

	private static ZondaCryptoPrivateTrade ParsePrivateTrade(
		JObject value)
	{
		if (value is null)
			return null;
		var market = value.Value<string>("market");
		var id = value["id"]?.ToString();
		var volume = ReadDecimal(value["amount"]);
		var price = ReadDecimal(value["rate"]);
		if (market.IsEmpty() || id.IsEmpty() ||
			volume <= 0 || price <= 0)
			return null;
		return new()
		{
			Id = id,
			MarketCode = market,
			Time = ReadTimestamp(value["time"]),
			Volume = volume,
			Price = price,
			Side = (value.Value<string>("userAction") ??
				value.Value<string>("initializedBy")).ToSide(),
			IsTaker = ReadBoolean(value["wasTaker"]),
		};
	}

	private static ZondaCryptoOffer DeserializePlacedOffer(
		string body,
		ZondaCryptoPlaceOrderRequest request)
	{
		var root = ParseRoot(body);
		var id = root["offerId"]?.ToString() ??
			root["id"]?.ToString();
		if (id.IsEmpty())
			throw new InvalidDataException(
				"zondacrypto accepted an order without an ID.");
		var completed = ReadBoolean(root["completed"]);
		return new()
		{
			Id = id,
			MarketCode = request.MarketCode,
			Side = request.Side,
			OrderType = request.OrderType,
			TimeInForce = request.TimeInForce ??
				TimeInForce.PutInQueue,
			PostOnly = request.PostOnly,
			State = completed
				? OrderStates.Done
				: OrderStates.Active,
			CreatedAt = DateTime.UtcNow,
			Price = request.Price,
			OriginalAmount = request.Amount,
			RemainingAmount = completed ? 0 : request.Amount,
		};
	}

	private async ValueTask<string> SendAsync(
		HttpMethod method,
		string path,
		KeyValuePair<string, string>[] query,
		JObject body,
		bool isPrivate,
		bool retryable,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"zondacrypto API key and secret are required for " +
					"private operations.");
		var target = CreateUri(path, query);
		var bodyText = body?.ToString(Formatting.None);
		var attempts = retryable ? _maximumReadAttempts : 1;
		Exception lastError = null;
		for (var attempt = 0; attempt < attempts; attempt++)
		{
			try
			{
				await WaitRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(
					method, target);
				if (bodyText is not null)
					request.Content = new StringContent(
						bodyText, Encoding.UTF8, "application/json");
				if (isPrivate)
					AddAuthentication(request, bodyText);
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var responseBody =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (responseBody.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"zondacrypto response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
				{
					_ = ParseRoot(responseBody);
					return responseBody;
				}
				var error = CreateHttpError(
					response.StatusCode,
					responseBody,
					response.ReasonPhrase);
				if (attempt + 1 >= attempts ||
					!IsTransient(response.StatusCode))
					throw error;
				lastError = error;
				await Task.Delay(
					response.Headers.RetryAfter?.Delta ??
						TimeSpan.FromMilliseconds(
							250 * (1 << attempt)),
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
			"zondacrypto API request failed.");
	}

	private void AddAuthentication(
		HttpRequestMessage request,
		string body)
	{
		var timestamp = NextTimestamp().ToString(
			CultureInfo.InvariantCulture);
		request.Headers.TryAddWithoutValidation(
			"API-Key", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"API-Hash",
			_authenticator.Sign(timestamp, body));
		request.Headers.TryAddWithoutValidation(
			"operation-id", Guid.NewGuid().ToString());
		request.Headers.TryAddWithoutValidation(
			"Request-Timestamp", timestamp);
	}

	private long NextTimestamp()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastTimestamp);
			var next = Math.Max(
				DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
				current + 1);
			if (Interlocked.CompareExchange(
				ref _lastTimestamp, next, current) == current)
				return next;
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

	private Uri CreateUri(
		string path,
		IEnumerable<KeyValuePair<string, string>> query)
	{
		var target = new Uri(
			_endpoint,
			path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
		var queryString = (query ?? [])
			.Where(static value =>
				!value.Key.IsEmpty() && value.Value is not null)
			.Select(static value =>
				Uri.EscapeDataString(value.Key) + "=" +
				Uri.EscapeDataString(value.Value))
			.Join("&");
		if (queryString.IsEmpty())
			return target;
		var builder = new UriBuilder(target)
		{
			Query = queryString,
		};
		return builder.Uri;
	}

	private static JObject ParseRoot(string body)
	{
		try
		{
			var root = JObject.Parse(
				body.ThrowIfEmpty(nameof(body)));
			var status = root.Value<string>("status");
			if (status.EqualsIgnoreCase("Fail") ||
				root["error"] is not null)
			{
				var details =
					root["errors"]?.ToString(Formatting.None) ??
					root["error"]?.ToString(Formatting.None) ??
					root.Value<string>("message");
				throw new InvalidDataException(
					$"zondacrypto request failed: {details}");
			}
			return root;
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"zondacrypto returned malformed JSON.", error);
		}
	}

	private static ZondaCryptoMarket ParseMarket(
		JObject value,
		string marketCode)
	{
		marketCode =
			value?.Value<string>("code") ??
			marketCode;
		if (marketCode.IsEmpty())
			return null;
		string baseCurrency = null;
		string quoteCurrency = null;
		try
		{
			var parts = marketCode.ToZondaMarketCode().Split('-');
			baseCurrency = parts[0];
			quoteCurrency = parts[1];
			marketCode = $"{baseCurrency}-{quoteCurrency}";
		}
		catch (FormatException)
		{
			return null;
		}
		var first = value?["first"] as JObject;
		var second = value?["second"] as JObject;
		return new()
		{
			Code = marketCode,
			BaseCurrency =
				first?.Value<string>("currency") ??
				baseCurrency,
			QuoteCurrency =
				second?.Value<string>("currency") ??
				quoteCurrency,
			AmountPrecision =
				value?.Value<int?>("amountPrecision") ??
				first?.Value<int?>("scale") ?? 0,
			PricePrecision =
				value?.Value<int?>("pricePrecision") ??
				second?.Value<int?>("scale") ?? 0,
			RatePrecision =
				value?.Value<int?>("ratePrecision") ??
				value?.Value<int?>("pricePrecision") ??
				second?.Value<int?>("scale") ?? 0,
			MinimumBaseAmount = ReadDecimal(first?["minOffer"]),
			MinimumQuoteAmount = ReadDecimal(second?["minOffer"]),
		};
	}

	private static ZondaCryptoQuote[] ParseQuotes(
		JToken values,
		bool isAsk)
	{
		var quotes = (values as JArray ?? [])
			.OfType<JObject>()
			.Select(static value => new ZondaCryptoQuote
			{
				Price = ReadDecimal(
					value["ra"] ?? value["rate"] ?? value["price"]),
				Volume = ReadDecimal(
					value["ca"] ?? value["amount"] ?? value["volume"]),
				OrderCount = ReadInt(
					value["co"] ?? value["orderCount"]),
			})
			.Where(static quote =>
				quote.Price > 0 && quote.Volume > 0);
		return [.. (isAsk
			? quotes.OrderBy(static quote => quote.Price)
			: quotes.OrderByDescending(
				static quote => quote.Price))];
	}

	private static TimeInForce ReadTimeInForce(
		JObject value,
		JArray flags)
	{
		if (ReadBoolean(value?["fillOrKill"]) ||
			flags?.Any(item =>
				item.ToString().EqualsIgnoreCase("fillOrKill")) == true)
			return TimeInForce.CancelBalance;
		if (ReadBoolean(value?["immediateOrCancel"]) ||
			flags?.Any(item =>
				item.ToString().EqualsIgnoreCase(
					"immediateOrCancel")) == true)
			return TimeInForce.MatchOrCancel;
		return TimeInForce.PutInQueue;
	}

	private static decimal ReadDecimal(JToken value)
		=> ReadNullableDecimal(value) ?? 0;

	private static decimal? ReadNullableDecimal(JToken value)
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

	private static int ReadInt(JToken value)
		=> int.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static long ReadLong(JToken value)
		=> long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static bool ReadBoolean(JToken value)
		=> bool.TryParse(value?.ToString(), out var result) &&
			result;

	private static DateTime ReadTimestamp(JToken value)
		=> ReadNullableTimestamp(value) ?? DateTime.UtcNow;

	private static DateTime? ReadNullableTimestamp(JToken value)
	{
		var timestamp = ReadLong(value);
		return timestamp > 0
			? timestamp.FromZondaTimestamp()
			: null;
	}

	private static string ToTimestamp(DateTime? value)
		=> value is DateTime time
			? new DateTimeOffset(time.ToUniversalTime())
				.ToUnixTimeMilliseconds()
				.ToString(CultureInfo.InvariantCulture)
			: null;

	private static KeyValuePair<string, string>[] Query(
		params (string Name, string Value)[] values)
		=> [.. values
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.Select(static value =>
				new KeyValuePair<string, string>(
					value.Name, value.Value))];

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(
			value.ThrowIfEmpty(nameof(value)).Trim());

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static Exception CreateHttpError(
		HttpStatusCode statusCode,
		string body,
		string reasonPhrase)
	{
		var details = body?.Trim();
		try
		{
			var root = JObject.Parse(body);
			details =
				root["errors"]?.ToString(Formatting.None) ??
				root["error"]?.ToString(Formatting.None) ??
				root.Value<string>("message") ??
				details;
		}
		catch (JsonException)
		{
		}
		if (details.IsEmpty())
			details = reasonPhrase;
		if (details?.Length > 512)
			details = details[..512];
		return new HttpRequestException(
			$"zondacrypto HTTP {(int)statusCode} " +
				$"({statusCode}): {details}",
			null,
			statusCode);
	}

	private static Uri ValidateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.EndsWith('/'))
			value += "/";
		if (!Uri.TryCreate(
			value,
			UriKind.Absolute,
			out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("https"))
			throw new ArgumentException(
				"zondacrypto endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}
}
