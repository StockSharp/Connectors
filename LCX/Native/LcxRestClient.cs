namespace StockSharp.LCX.Native;

sealed class LcxRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly Uri _klineEndpoint;
	private readonly string _apiVersion;
	private readonly string _key;
	private readonly string _secret;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;

	public LcxRestClient(
		string endpoint,
		string klineEndpoint,
		string apiVersion,
		SecureString key,
		SecureString secret)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_klineEndpoint = ValidateEndpoint(klineEndpoint);
		_apiVersion = apiVersion.ThrowIfEmpty(
			nameof(apiVersion)).Trim();
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty()
			? null
			: secret.UnSecure().Trim();
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
			"StockSharp-LCX-Connector/1.0");
	}

	public override string Name => "LCX_REST";

	public bool IsCredentialsAvailable
		=> !_key.IsEmpty() && !_secret.IsEmpty();

	public string KeyId => _key;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<LcxMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> DeserializeMarkets(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/pairs",
			null,
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<LcxTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> DeserializeTickers(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/tickers",
			null,
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<LcxBook> GetBookAsync(
		string pair,
		CancellationToken cancellationToken)
	{
		var parsed = DeserializeBook(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/book",
			new Dictionary<string, string>()
			{
				["pair"] = pair,
			},
			null,
			false,
			true,
			cancellationToken));
		return new()
		{
			Symbol = pair,
			IsSnapshot = parsed.IsSnapshot,
			Bids = parsed.Bids,
			Asks = parsed.Asks,
		};
	}

	public async ValueTask<LcxPublicTrade[]> GetTradesAsync(
		string pair,
		int offset,
		CancellationToken cancellationToken)
		=> DeserializePublicTrades(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/trades",
			new Dictionary<string, string>()
			{
				["pair"] = pair,
				["offset"] = offset.ToString(
					CultureInfo.InvariantCulture),
			},
			null,
			false,
			true,
			cancellationToken), pair);

	public async ValueTask<LcxCandle[]> GetCandlesAsync(
		string pair,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
		=> DeserializeCandles(await SendAsync(
			HttpMethod.Get,
			_klineEndpoint,
			"/v1/market/kline",
			new Dictionary<string, string>()
			{
				["pair"] = pair,
				["resolution"] = ToResolution(timeFrame),
				["from"] = new DateTimeOffset(
					from.ToUniversalTime()).ToUnixTimeSeconds()
					.ToString(CultureInfo.InvariantCulture),
				["to"] = new DateTimeOffset(
					to.ToUniversalTime()).ToUnixTimeSeconds()
					.ToString(CultureInfo.InvariantCulture),
			},
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<LcxBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> DeserializeBalances(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/balances",
			null,
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<LcxOrder> PlaceOrderAsync(
		LcxMarket market,
		Sides side,
		OrderTypes orderType,
		decimal amount,
		decimal price,
		string clientOrderId,
		CancellationToken cancellationToken)
	{
		var body = new JObject
		{
			["Pair"] = market.Symbol,
			["Amount"] = amount,
		};
		if (orderType == OrderTypes.Limit)
			body["Price"] = price;
		body["OrderType"] = orderType.ToLcx();
		body["Side"] = side.ToLcx();
		if (!clientOrderId.IsEmpty())
			body["ClientOrderId"] = clientOrderId;
		return DeserializeOrders(await SendAsync(
			HttpMethod.Post,
			_endpoint,
			"/api/create",
			null,
			body,
			true,
			false,
			cancellationToken)).FirstOrDefault();
	}

	public async ValueTask<LcxOrder> ModifyOrderAsync(
		string orderId,
		decimal amount,
		decimal price,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Put,
			_endpoint,
			"/api/modify",
			null,
			new JObject
			{
				["OrderId"] = orderId,
				["Amount"] = amount,
				["Price"] = price,
			},
			true,
			false,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<LcxOrder> CancelOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Delete,
			_endpoint,
			"/api/cancel",
			new Dictionary<string, string>()
			{
				["orderId"] = orderId,
			},
			null,
			true,
			false,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<LcxOrder[]> GetOpenOrdersAsync(
		string pair,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/open",
			CreateHistoryQuery(pair, from, to),
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<LcxOrder> GetOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/order",
			new Dictionary<string, string>()
			{
				["orderId"] = orderId,
			},
			null,
			true,
			true,
			cancellationToken)).FirstOrDefault();

	public async ValueTask<LcxOrder[]> GetOrderHistoryAsync(
		string pair,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/orderHistory",
			CreateHistoryQuery(pair, from, to),
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<LcxUserTrade[]> GetUserTradesAsync(
		string pair,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
		=> DeserializeUserTrades(await SendAsync(
			HttpMethod.Get,
			_endpoint,
			"/api/uHistory",
			CreateHistoryQuery(pair, from, to),
			null,
			true,
			true,
			cancellationToken));

	internal static string GenerateSignature(
		string method,
		string endpoint,
		string body,
		string secret)
	{
		var message =
			method.ThrowIfEmpty(nameof(method)).ToUpperInvariant() +
			endpoint.ThrowIfEmpty(nameof(endpoint)) +
			(body.IsEmpty() ? "{}" : body);
		using var hmac = new HMACSHA256(
			Encoding.UTF8.GetBytes(
				secret.ThrowIfEmpty(nameof(secret))));
		return Convert.ToBase64String(
			hmac.ComputeHash(Encoding.UTF8.GetBytes(message)));
	}

	internal static LcxMarket[] DeserializeMarkets(string body)
		=> [.. ReadDataArray(body)
			.OfType<JObject>()
			.Select(static value =>
			{
				var precision = GetObject(
					value, "Orderprecision") ??
					GetObject(value, "Precision");
				var minimum = GetObject(value, "MinOrder");
				var maximum = GetObject(value, "MaxOrder");
				return new LcxMarket
				{
					Id = ReadString(value, "Id"),
					Symbol = ReadString(value, "Symbol")
						?.ToUpperInvariant(),
					BaseCurrency = ReadString(value, "Base")
						?.ToUpperInvariant(),
					QuoteCurrency = ReadString(value, "Quote")
						?.ToUpperInvariant(),
					AmountPrecision = ReadInt(
						GetToken(precision, "Amount")),
					PricePrecision = ReadInt(
						GetToken(precision, "Price")),
					MinimumAmount = ReadDecimal(
						GetToken(minimum, "Base")),
					MaximumAmount = ReadDecimal(
						GetToken(maximum, "Base")),
					IsActive =
						ReadBoolean(GetToken(value, "Status")) &&
						!ReadString(value, "Mode")
							.EqualsIgnoreCase("halt"),
				};
			})
			.Where(static market =>
				!market.Symbol.IsEmpty() &&
				!market.BaseCurrency.IsEmpty() &&
				!market.QuoteCurrency.IsEmpty())
			.OrderBy(static market => market.Symbol,
				StringComparer.OrdinalIgnoreCase)];

	internal static LcxTicker[] DeserializeTickers(string body)
	{
		var data = ReadDataToken(body) as JObject;
		if (data is null)
			return [];
		return [.. data.Properties()
			.Where(static property =>
				property.Value is JObject)
			.Select(static property => ParseTicker(
				(JObject)property.Value, property.Name))
			.Where(static ticker => ticker is not null)
			.OrderBy(static ticker => ticker.Symbol,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static LcxBook DeserializeBook(string body)
	{
		var data = ReadDataToken(body) as JObject ??
			throw new InvalidDataException(
				"LCX returned an invalid order book.");
		return ParseBook(data, null, true);
	}

	internal static LcxPublicTrade[] DeserializePublicTrades(
		string body,
		string pair)
		=> ParsePublicTrades(ReadDataToken(body), pair);

	internal static LcxCandle[] DeserializeCandles(string body)
		=> [.. ReadDataArray(body)
			.OfType<JObject>()
			.Select(static value =>
			{
				var resolution = ReadString(value, "timeframe");
				return new LcxCandle
				{
					Symbol = ReadString(value, "pair")
						?.ToUpperInvariant(),
					TimeFrame = FromResolution(resolution),
					OpenTime = ReadTimestamp(
						GetToken(value, "timestamp")),
					Open = ReadDecimal(
						GetToken(value, "open")),
					High = ReadDecimal(
						GetToken(value, "high")),
					Low = ReadDecimal(
						GetToken(value, "low")),
					Close = ReadDecimal(
						GetToken(value, "close")),
					Volume = ReadDecimal(
						GetToken(value, "volume")),
				};
			})
			.Where(static candle =>
				!candle.Symbol.IsEmpty() &&
				candle.TimeFrame > TimeSpan.Zero)
			.OrderBy(static candle => candle.OpenTime)];

	internal static LcxBalance[] DeserializeBalances(string body)
		=> ParseBalances(ReadDataToken(body));

	internal static LcxOrder[] DeserializeOrders(string body)
	{
		var data = ReadDataToken(body);
		IEnumerable<JObject> values = data switch
		{
			JArray array => array.OfType<JObject>(),
			JObject value => [value],
			_ => [],
		};
		return [.. values
			.Select(ParseOrder)
			.Where(static order => order is not null)];
	}

	internal static LcxUserTrade[] DeserializeUserTrades(
		string body)
	{
		var data = ReadDataToken(body);
		IEnumerable<JObject> values = data switch
		{
			JArray array => array.OfType<JObject>(),
			JObject value => [value],
			_ => [],
		};
		return [.. values
			.Select(ParseUserTrade)
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];
	}

	internal static LcxTicker ParseTicker(
		JObject value,
		string fallbackPair)
	{
		if (value is null)
			return null;
		var symbol =
			ReadString(value, "symbol") ?? fallbackPair;
		if (symbol.IsEmpty())
			return null;
		return new()
		{
			Symbol = symbol.ToUpperInvariant(),
			Time = ReadTimestamp(
				GetToken(value, "lastUpdated")),
			LastPrice = ReadDecimal(
				GetToken(value, "lastPrice")),
			Bid = ReadDecimal(GetToken(value, "bestBid")),
			Ask = ReadDecimal(GetToken(value, "bestAsk")),
			High = ReadDecimal(GetToken(value, "high")),
			Low = ReadDecimal(GetToken(value, "low")),
			Volume = ReadDecimal(GetToken(value, "volume")),
			Change = ReadDecimal(GetToken(value, "change")),
		};
	}

	internal static LcxBook ParseBook(
		JObject value,
		string pair,
		bool isSnapshot)
	{
		if (value is null)
			return null;
		return new()
		{
			Symbol = pair?.ToUpperInvariant(),
			IsSnapshot = isSnapshot,
			Bids = ParseQuotes(
				GetToken(value, "buy"),
				Sides.Buy,
				isSnapshot),
			Asks = ParseQuotes(
				GetToken(value, "sell"),
				Sides.Sell,
				isSnapshot),
		};
	}

	internal static LcxPublicTrade[] ParsePublicTrades(
		JToken token,
		string pair)
	{
		if (token is not JArray values)
			return [];
		return [.. values
			.OfType<JArray>()
			.Select((value, index) =>
			{
				if (value.Count < 4)
					return null;
				var time = ReadTimestamp(value[3]);
				var price = ReadDecimal(value[0]);
				var volume = ReadDecimal(value[1]);
				return price > 0 && volume > 0
					? new LcxPublicTrade
					{
						Id = $"{new DateTimeOffset(time)
							.ToUnixTimeMilliseconds()}:" +
							$"{price.ToString(
								CultureInfo.InvariantCulture)}:" +
							$"{volume.ToString(
								CultureInfo.InvariantCulture)}:" +
							index,
						Symbol = pair?.ToUpperInvariant(),
						Time = time,
						Price = price,
						Volume = volume,
						Side = value[2]?.ToString()
							.ToLcxSide() ?? Sides.Buy,
					}
					: null;
			})
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];
	}

	internal static LcxBalance[] ParseBalances(JToken token)
	{
		if (token is JObject wrapper &&
			GetToken(wrapper, "data") is JArray nested)
			token = nested;
		if (token is not JArray values)
			return [];
		return [.. values
			.OfType<JObject>()
			.Select(static value =>
			{
				var balance = GetObject(value, "balance") ?? value;
				var currency =
					ReadString(value, "coin") ??
					ReadString(value, "Coin");
				return currency.IsEmpty()
					? null
					: new LcxBalance
					{
						Currency = currency.ToUpperInvariant(),
						Name = ReadString(value, "fullName"),
						Available = ReadDecimal(
							GetToken(balance, "freeBalance")),
						Blocked = ReadDecimal(
							GetToken(
								balance, "occupiedBalance")),
						Total = ReadDecimal(
							GetToken(balance, "totalBalance")),
					};
			})
			.Where(static balance => balance is not null)
			.OrderBy(static balance => balance.Currency,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static LcxOrder ParseOrder(JObject value)
	{
		if (value is null)
			return null;
		var id = ReadString(value, "Id");
		var symbol = ReadString(value, "Pair");
		if (id.IsEmpty() || symbol.IsEmpty())
			return null;
		return new()
		{
			Id = id,
			ClientOrderId = ReadString(
				value, "ClientOrderId"),
			Symbol = symbol.ToUpperInvariant(),
			Side = ReadString(value, "Side").ToLcxSide(),
			OrderType = ReadString(
				value, "OrderType").ToLcxOrderType(),
			State = ReadString(
				value, "Status").ToLcxOrderState(),
			CreatedAt = ReadTimestamp(
				GetToken(value, "CreatedAt")),
			UpdatedAt = ReadTimestamp(
				GetToken(value, "UpdatedAt")),
			Price = ReadDecimal(GetToken(value, "Price")),
			Amount = ReadDecimal(GetToken(value, "Amount")),
			Filled = ReadDecimal(GetToken(value, "Filled")),
			Fee = ReadDecimal(GetToken(value, "Fee")),
		};
	}

	internal static LcxUserTrade ParseUserTrade(
		JObject value)
	{
		if (value is null)
			return null;
		var id =
			ReadString(value, "Id") ??
			ReadString(value, "tradeId");
		var orderId =
			ReadString(value, "OrderId") ??
			ReadString(value, "orderId");
		var symbol =
			ReadString(value, "Pair") ??
			ReadString(value, "pair");
		if (id.IsEmpty() || orderId.IsEmpty() ||
			symbol.IsEmpty())
			return null;
		return new()
		{
			Id = id,
			OrderId = orderId,
			Symbol = symbol.ToUpperInvariant(),
			Side = ReadString(value, "Side").ToLcxSide(),
			Time = ReadTimestamp(
				GetToken(value, "CreatedAt") ??
				GetToken(value, "createdAt")),
			Price = ReadDecimal(GetToken(value, "Price")),
			Volume = ReadDecimal(
				GetToken(value, "Amount")),
			Fee = ReadDecimal(GetToken(value, "Fee")),
			FeeCurrency =
				ReadString(value, "FeeCoin") ??
				ReadString(value, "feeCoin"),
		};
	}

	private async ValueTask<string> SendAsync(
		HttpMethod method,
		Uri endpoint,
		string path,
		IReadOnlyDictionary<string, string> query,
		JObject body,
		bool isPrivate,
		bool retryable,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"LCX API key and secret are required for private " +
					"operations.");
		var target = new Uri(endpoint, path.TrimStart('/'));
		if (query is { Count: > 0 })
			target = new UriBuilder(target)
			{
				Query = query.Select(static item =>
					Uri.EscapeDataString(item.Key) + "=" +
					Uri.EscapeDataString(item.Value ?? string.Empty))
					.Join("&"),
			}.Uri;
		var bodyText = body?.ToString(Formatting.None);
		var attempts = retryable ? _maximumReadAttempts : 1;
		Exception lastError = null;
		for (var attempt = 0; attempt < attempts; attempt++)
		{
			try
			{
				await WaitRateLimitAsync(
					isPrivate, cancellationToken);
				using var request = new HttpRequestMessage(
					method, target);
				request.Headers.TryAddWithoutValidation(
					"API-VERSION", _apiVersion);
				if (bodyText is not null)
					request.Content = new StringContent(
						bodyText,
						Encoding.UTF8,
						"application/json");
				if (isPrivate)
				{
					request.Headers.TryAddWithoutValidation(
						"x-access-key", _key);
					request.Headers.TryAddWithoutValidation(
						"x-access-sign",
						GenerateSignature(
							method.Method,
							path,
							bodyText ?? "{}",
							_secret));
					request.Headers.TryAddWithoutValidation(
						"x-access-timestamp",
						DateTimeOffset.UtcNow
							.ToUnixTimeMilliseconds()
							.ToString(
								CultureInfo.InvariantCulture));
				}
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var responseBody =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (responseBody.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"LCX response exceeds the size limit.");
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
			"LCX API request failed.");
	}

	private async ValueTask WaitRateLimitAsync(
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(
				isPrivate ? 700 : 45);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static Dictionary<string, string> CreateHistoryQuery(
		string pair,
		DateTime? from,
		DateTime? to)
	{
		var query = new Dictionary<string, string>
		{
			["offset"] = "1",
		};
		if (!pair.IsEmpty())
			query["pair"] = pair;
		if (from is DateTime fromValue)
			query["fromDate"] = new DateTimeOffset(
				fromValue.ToUniversalTime())
				.ToUnixTimeMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		if (to is DateTime toValue)
			query["toDate"] = new DateTimeOffset(
				toValue.ToUniversalTime())
				.ToUnixTimeMilliseconds()
				.ToString(CultureInfo.InvariantCulture);
		return query;
	}

	internal static string ToResolution(TimeSpan timeFrame)
	{
		if (timeFrame == TimeSpan.FromDays(1))
			return "1D";
		if (timeFrame == TimeSpan.FromDays(7))
			return "1W";
		if (timeFrame == TimeSpan.FromDays(30))
			return "1M";
		var minutes = timeFrame.TotalMinutes;
		if (minutes <= 0 || minutes != Math.Truncate(minutes))
			throw new NotSupportedException(
				$"LCX does not support the {timeFrame} candle " +
					"time frame.");
		return minutes.ToString(CultureInfo.InvariantCulture);
	}

	internal static TimeSpan FromResolution(string value)
		=> value?.Trim().ToUpperInvariant() switch
		{
			"1D" => TimeSpan.FromDays(1),
			"1W" => TimeSpan.FromDays(7),
			"1M" => TimeSpan.FromDays(30),
			_ when int.TryParse(
				value,
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out var minutes) && minutes > 0 =>
					TimeSpan.FromMinutes(minutes),
			_ => default,
		};

	private static LcxQuote[] ParseQuotes(
		JToken token,
		Sides side,
		bool isSnapshot)
	{
		if (token is not JArray values)
			return [];
		return [.. values
			.OfType<JArray>()
			.Where(static value => value.Count >= 2)
			.Select(value => new LcxQuote
			{
				Price = ReadDecimal(value[0]),
				Volume = ReadDecimal(value[1]),
				Side = value.Count > 2
					? value[2]?.ToString().ToLcxSide() ?? side
					: side,
			})
			.Where(quote =>
				quote.Price > 0 &&
				(!isSnapshot || quote.Volume > 0))
			.OrderBy(quote => side == Sides.Buy
				? -quote.Price
				: quote.Price)];
	}

	private static JObject ParseRoot(string body)
	{
		try
		{
			var root = JObject.Parse(
				body.ThrowIfEmpty(nameof(body)));
			if (ReadString(root, "status")
					.EqualsIgnoreCase("error") ||
				ReadString(root, "status")
					.EqualsIgnoreCase("fail"))
				throw new InvalidDataException(
					"LCX request failed: " +
						(ReadString(root, "message") ??
							ReadString(root, "error")));
			return root;
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"LCX returned malformed JSON.", error);
		}
	}

	private static JToken ReadDataToken(string body)
		=> GetToken(ParseRoot(body), "data");

	private static JArray ReadDataArray(string body)
		=> ReadDataToken(body) as JArray ?? [];

	internal static JToken GetToken(
		JObject value,
		string name)
		=> value?.Properties().FirstOrDefault(property =>
			property.Name.Equals(
				name,
				StringComparison.OrdinalIgnoreCase))?.Value;

	private static JObject GetObject(
		JObject value,
		string name)
		=> GetToken(value, name) as JObject;

	private static string ReadString(
		JObject value,
		string name)
	{
		var token = GetToken(value, name);
		return token is null ||
			token.Type is JTokenType.Null or JTokenType.Undefined
				? null
				: token.ToString();
	}

	internal static decimal ReadDecimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static int ReadInt(JToken value)
		=> int.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var result)
				? result
				: 0;

	private static bool ReadBoolean(JToken value)
		=> bool.TryParse(value?.ToString(), out var result) &&
			result;

	internal static DateTime ReadTimestamp(JToken value)
	{
		if (long.TryParse(
			value?.ToString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var timestamp) &&
			timestamp > 0)
			return timestamp.FromLcxTimestamp();
		if (DateTime.TryParse(
			value?.ToString(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var time))
			return time;
		return DateTime.UtcNow;
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
				"LCX endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}

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
				ReadString(root, "message") ??
				ReadString(root, "error") ??
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
			$"LCX HTTP {(int)statusCode} ({statusCode}): " +
				details,
			null,
			statusCode);
	}
}
