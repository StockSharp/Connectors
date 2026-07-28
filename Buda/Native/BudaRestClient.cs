namespace StockSharp.Buda.Native;

sealed class BudaRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly BudaAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private long _lastNonce;

	public BudaRestClient(
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
			"StockSharp-Buda-Connector/1.0");
	}

	public override string Name => "Buda_REST";

	public bool IsCredentialsAvailable
		=> _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<BudaMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> DeserializeMarkets(await SendAsync(
			HttpMethod.Get,
			"markets",
			[],
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<BudaTicker> GetTickerAsync(
		string marketId,
		CancellationToken cancellationToken)
		=> DeserializeTicker(await SendAsync(
			HttpMethod.Get,
			$"markets/{EscapePath(marketId)}/ticker",
			[],
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<BudaOrderBook> GetOrderBookAsync(
		string marketId,
		CancellationToken cancellationToken)
		=> DeserializeOrderBook(await SendAsync(
			HttpMethod.Get,
			$"markets/{EscapePath(marketId)}/order_book",
			[],
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<BudaTrade[]> GetTradesAsync(
		string marketId,
		DateTime? from,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeTrades(await SendAsync(
			HttpMethod.Get,
			$"markets/{EscapePath(marketId)}/trades",
			Query(
				("timestamp", from is DateTime timestamp
					? new DateTimeOffset(timestamp.ToUniversalTime())
						.ToUnixTimeMilliseconds()
						.ToString(CultureInfo.InvariantCulture)
					: null),
				("limit", limit.Max(1).Min(100).ToString(
					CultureInfo.InvariantCulture))),
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<BudaAccount> GetAccountAsync(
		CancellationToken cancellationToken)
	{
		var root = ParseRoot(await SendAsync(
			HttpMethod.Get,
			"me",
			[],
			null,
			true,
			true,
			cancellationToken));
		var value = root["user"] as JObject ??
			root["account"] as JObject ??
			throw new InvalidDataException(
				"Buda.com account response contains no user.");
		return new()
		{
			Id = value.Value<string>("id"),
			PubSubKey = value.Value<string>("pubsub_key"),
		};
	}

	public async ValueTask<BudaBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> DeserializeBalances(await SendAsync(
			HttpMethod.Get,
			"balances",
			[],
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<BudaOrder[]> GetOrdersAsync(
		string marketId,
		string state,
		int limit,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			$"markets/{EscapePath(marketId)}/orders",
			Query(
				("state", state),
				("per", limit.Max(1).Min(100).ToString(
					CultureInfo.InvariantCulture)),
				("page", "1")),
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<BudaOrder> GetOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> DeserializeOrder(await SendAsync(
			HttpMethod.Get,
			$"orders/{EscapePath(orderId)}",
			[],
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<BudaOrder> PlaceOrderAsync(
		BudaPlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		var body = new JObject
		{
			["type"] = order.Side.ToBuda(),
			["price_type"] = order.OrderType == OrderTypes.Market
				? "market"
				: "limit",
			["amount"] = order.Amount,
		};
		if (!order.ClientId.IsEmpty())
			body["client_id"] = order.ClientId;
		if (order.OrderType == OrderTypes.Limit)
			body["limit"] = new JObject
			{
				["price"] = order.Price,
				["type"] = order.TimeInForce.ToBudaOrderType(
					order.PostOnly),
			};
		return DeserializeOrder(await SendAsync(
			HttpMethod.Post,
			$"markets/{EscapePath(order.MarketId)}/orders",
			[],
			body,
			true,
			false,
			cancellationToken));
	}

	public async ValueTask<BudaOrder> CancelOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> DeserializeOrder(await SendAsync(
			HttpMethod.Put,
			$"orders/{EscapePath(orderId)}",
			[],
			new JObject
			{
				["state"] = "canceling",
			},
			true,
			false,
			cancellationToken));

	public async ValueTask<BudaOrder[]> CancelAllOrdersAsync(
		string marketId,
		Sides? side,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Delete,
			"orders",
			Query(
				("market", marketId),
				("type", side?.ToBuda())),
			null,
			true,
			false,
			cancellationToken));

	internal static BudaMarket[] DeserializeMarkets(string body)
	{
		var root = ParseRoot(body);
		return [.. (root["markets"] as JArray ?? [])
			.OfType<JObject>()
			.Select(value => new BudaMarket
			{
				Id = value.Value<string>("id")?.ToLowerInvariant(),
				Name = value.Value<string>("name"),
				BaseCurrency = value.Value<string>("base_currency")
					?.ToUpperInvariant(),
				QuoteCurrency = value.Value<string>("quote_currency")
					?.ToUpperInvariant(),
				MinimumOrderAmount = ReadAmount(
					value["minimum_order_amount"]),
			})
			.Where(static market =>
				!market.Id.IsEmpty() &&
				!market.BaseCurrency.IsEmpty() &&
				!market.QuoteCurrency.IsEmpty())
			.OrderBy(static market => market.SecurityCode,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static BudaTicker DeserializeTicker(string body)
	{
		var value = ParseRoot(body)["ticker"] as JObject ??
			throw new InvalidDataException(
				"Buda.com ticker response contains no ticker.");
		return new()
		{
			MarketId = value.Value<string>("market_id"),
			LastPrice = ReadNullableAmount(value["last_price"]),
			BidPrice = ReadNullableAmount(value["max_bid"]),
			AskPrice = ReadNullableAmount(value["min_ask"]),
			Volume = ReadNullableAmount(value["volume"]),
			PriceVariation24h =
				value.Value<decimal?>("price_variation_24h"),
		};
	}

	internal static BudaOrderBook DeserializeOrderBook(string body)
	{
		var value = ParseRoot(body)["order_book"] as JObject ??
			throw new InvalidDataException(
				"Buda.com order-book response contains no book.");
		return ParseOrderBook(value);
	}

	internal static BudaTrade[] DeserializeTrades(string body)
	{
		var value = ParseRoot(body)["trades"] as JObject ??
			throw new InvalidDataException(
				"Buda.com trades response contains no trades.");
		var marketId = value.Value<string>("market_id");
		return [.. (value["entries"] as JArray ?? [])
			.OfType<JArray>()
			.Select(row => ParseTrade(row, marketId))
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];
	}

	internal static BudaBalance[] DeserializeBalances(string body)
	{
		var root = ParseRoot(body);
		var values = root["balances"] as JArray;
		if (values is null && root["balance"] is JObject balance)
			values = new(balance);
		return [.. (values ?? [])
			.OfType<JObject>()
			.Select(ParseBalance)
			.Where(static item => item is not null)
			.OrderBy(static item => item.Currency,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static BudaOrder[] DeserializeOrders(string body)
	{
		var root = ParseRoot(body);
		var values = root["orders"] as JArray;
		if (values is null && root["order"] is JObject order)
			values = new(order);
		return [.. (values ?? [])
			.OfType<JObject>()
			.Select(ParseOrder)
			.Where(static item => item is not null)];
	}

	internal static BudaOrder DeserializeOrder(string body)
		=> DeserializeOrders(body).FirstOrDefault();

	internal static BudaOrderBook ParseOrderBook(JObject value)
		=> new()
		{
			Bids = ParseQuotes(value?["bids"], false),
			Asks = ParseQuotes(value?["asks"], true),
		};

	internal static BudaTrade ParseTrade(
		JArray row,
		string marketId)
	{
		if (row is not { Count: >= 4 })
			return null;
		var timestamp = ReadLong(row[0]);
		var volume = ReadDecimal(row[1]);
		var price = ReadDecimal(row[2]);
		var side = row[3].Value<string>().ToSide();
		var id = row.Count > 4
			? row[4]?.Value<string>()
			: null;
		if (id.IsEmpty())
			id = string.Join(
				"-",
				timestamp.ToString(CultureInfo.InvariantCulture),
				price.ToWire(),
				volume.ToWire(),
				side);
		return new()
		{
			Id = id,
			MarketId = marketId,
			Time = timestamp.FromBudaTimestamp(),
			Volume = volume,
			Price = price,
			Side = side,
		};
	}

	internal static BudaBalance ParseBalance(JObject value)
	{
		if (value is null)
			return null;
		return new()
		{
			Currency = value.Value<string>("id")
				?.ToUpperInvariant(),
			Amount = ReadAmount(value["amount"]),
			Available = ReadAmount(value["available_amount"]),
			Frozen = ReadAmount(value["frozen_amount"]),
			PendingWithdrawal = ReadAmount(
				value["pending_withdraw_amount"] ??
				value["pending_withdrawal_amount"]),
		};
	}

	internal static BudaOrder ParseOrder(JObject value)
	{
		if (value is null)
			return null;
		var orderType = value.Value<string>("price_type")
			.ToOrderType();
		var specificType = value.Value<string>("order_type");
		return new()
		{
			Id = value["id"]?.Value<string>(),
			ClientId = value.Value<string>("client_id"),
			MarketId = value.Value<string>("market_id"),
			Side = value.Value<string>("type").ToSide(),
			OrderType = orderType,
			TimeInForce = specificType.ToTimeInForce(),
			PostOnly = specificType.EqualsIgnoreCase("post_only"),
			State = value.Value<string>("state").ToOrderState(),
			CreatedAt = ReadDate(value["created_at"]),
			Price = ReadAmount(value["limit"]),
			OriginalAmount = ReadAmount(
				value["original_amount"]),
			RemainingAmount = ReadAmount(value["amount"]),
			TradedAmount = ReadAmount(value["traded_amount"]),
			PaidFee = ReadAmount(value["paid_fee"]),
			FeeCurrency = value.Value<string>("fee_currency"),
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
				"Buda.com API key and secret are required for " +
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
						"Buda.com response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
					return responseBody;
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
				error is HttpRequestException or TaskCanceledException)
			{
				lastError = error;
				await Task.Delay(
					TimeSpan.FromMilliseconds(
						250 * (1 << attempt)),
					cancellationToken);
			}
		}
		throw lastError ?? new InvalidOperationException(
			"Buda.com API request failed.");
	}

	private void AddAuthentication(
		HttpRequestMessage request,
		string body)
	{
		var nonce = NextNonce().ToString(
			CultureInfo.InvariantCulture);
		request.Headers.TryAddWithoutValidation(
			"X-SBTC-APIKEY", _authenticator.Key);
		request.Headers.TryAddWithoutValidation(
			"X-SBTC-NONCE", nonce);
		request.Headers.TryAddWithoutValidation(
			"X-SBTC-SIGNATURE",
			_authenticator.Sign(
				request.Method.Method,
				request.RequestUri.PathAndQuery,
				body,
				nonce));
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() *
				1000;
			var next = Math.Max(now, current + 1);
			if (Interlocked.CompareExchange(
				ref _lastNonce, next, current) == current)
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(100);
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
			if (root["error"] is JToken error)
				throw new InvalidDataException(
					$"Buda.com request failed: {error}");
			return root;
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Buda.com returned malformed JSON.", error);
		}
	}

	private static BudaQuote[] ParseQuotes(
		JToken values,
		bool isAsk)
	{
		var quotes = (values as JArray ?? [])
			.OfType<JArray>()
			.Where(static row => row.Count >= 2)
			.Select(static row => new BudaQuote
			{
				Price = ReadDecimal(row[0]),
				Volume = ReadDecimal(row[1]),
			})
			.Where(static quote =>
				quote.Price > 0 && quote.Volume > 0);
		return [.. (isAsk
			? quotes.OrderBy(static quote => quote.Price)
			: quotes.OrderByDescending(
				static quote => quote.Price))];
	}

	private static decimal ReadAmount(JToken value)
		=> ReadNullableAmount(value) ?? 0;

	private static decimal? ReadNullableAmount(JToken value)
	{
		if (value is JArray array && array.Count > 0)
			value = array[0];
		if (value is null ||
			value.Type is JTokenType.Null or JTokenType.Undefined)
			return null;
		return ReadDecimal(value);
	}

	private static decimal ReadDecimal(JToken value)
		=> decimal.TryParse(
			value?.ToString(),
			NumberStyles.Float,
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

	private static DateTime? ReadDate(JToken value)
		=> DateTime.TryParse(
			value?.Value<string>(),
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal |
				DateTimeStyles.AdjustToUniversal,
			out var result)
				? result
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
			details = root["error"]?.ToString() ??
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
			$"Buda.com HTTP {(int)statusCode} " +
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
				"Buda.com endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}
}
