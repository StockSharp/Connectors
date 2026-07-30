namespace StockSharp.CoinSpot.Native;

sealed class CoinSpotApiException(
	HttpStatusCode statusCode,
	string message)
	: InvalidOperationException(
		$"CoinSpot API error {(int)statusCode} ({statusCode}): {message}")
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class CoinSpotRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _publicEndpoint;
	private readonly Uri _tradingEndpoint;
	private readonly Uri _readOnlyEndpoint;
	private readonly HttpClient _http;
	private readonly CoinSpotAuthenticator _authenticator;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private long _lastNonce;

	public CoinSpotRestClient(
		string publicEndpoint,
		string tradingEndpoint,
		string readOnlyEndpoint,
		SecureString key,
		SecureString secret)
	{
		_publicEndpoint = ValidateEndpoint(
			publicEndpoint, nameof(publicEndpoint));
		_tradingEndpoint = ValidateEndpoint(
			tradingEndpoint, nameof(tradingEndpoint));
		_readOnlyEndpoint = ValidateEndpoint(
			readOnlyEndpoint, nameof(readOnlyEndpoint));
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
			"StockSharp-CoinSpot-Connector/1.0");
	}

	public override string Name => "CoinSpot_REST";

	public bool IsCredentialsAvailable
		=> _authenticator.IsAvailable;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<CoinSpotMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> DeserializeMarkets(await SendPublicAsync(
			"latest", cancellationToken));

	public async ValueTask<CoinSpotTicker> GetTickerAsync(
		string nativeSymbol,
		CancellationToken cancellationToken)
	{
		var market = new CoinSpotMarket(
			nativeSymbol, null, null, null);
		var response = await SendPublicAsync(
			"latest/" + market.ToCoinSpotPath(),
			cancellationToken);
		var markets = DeserializeMarkets(response);
		return markets.FirstOrDefault(item =>
			item.NativeSymbol.EqualsIgnoreCase(
				market.NativeSymbol))?.Ticker ??
			markets.FirstOrDefault()?.Ticker ??
			throw new InvalidDataException(
				$"CoinSpot returned no ticker for '{nativeSymbol}'.");
	}

	public async ValueTask<CoinSpotDepth> GetDepthAsync(
		string nativeSymbol,
		int depth,
		CancellationToken cancellationToken)
	{
		var market = new CoinSpotMarket(
			nativeSymbol, null, null, null);
		var result = DeserializeOrderBook(await SendPublicAsync(
			"orders/open/" + market.ToCoinSpotPath(),
			cancellationToken));
		return new()
		{
			Market = result.Market.IsEmpty()
				? market.SecurityCode
				: result.Market,
			Bids = result.Bids.Take(NormalizeDepth(depth)).ToArray(),
			Asks = result.Asks.Take(NormalizeDepth(depth)).ToArray(),
			Time = result.Time,
		};
	}

	public async ValueTask<CoinSpotTrade[]> GetPublicTradesAsync(
		string nativeSymbol,
		CancellationToken cancellationToken)
	{
		var market = new CoinSpotMarket(
			nativeSymbol, null, null, null);
		var trades = DeserializePublicTrades(await SendPublicAsync(
			"orders/completed/" + market.ToCoinSpotPath(),
			cancellationToken));
		return [.. trades.Select(trade =>
			trade.Market.IsEmpty()
				? new CoinSpotTrade
				{
					Id = trade.Id,
					Market = market.SecurityCode,
					Price = trade.Price,
					Volume = trade.Volume,
					Time = trade.Time,
					Side = trade.Side,
				}
				: trade)];
	}

	public async ValueTask<CoinSpotBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> DeserializeBalances(await SendPrivateAsync(
			_readOnlyEndpoint,
			"my/balances",
			null,
			true,
			cancellationToken));

	public async ValueTask<CoinSpotOrder[]> GetOpenOrdersAsync(
		string coin,
		string market,
		CancellationToken cancellationToken)
	{
		var body = CreateMarketBody(coin, market);
		var limit = DeserializeOrders(await SendPrivateAsync(
			_readOnlyEndpoint,
			"my/orders/limit/open",
			body,
			true,
			cancellationToken), false);
		var marketOrders = DeserializeOrders(await SendPrivateAsync(
			_readOnlyEndpoint,
			"my/orders/market/open",
			body,
			true,
			cancellationToken), false);
		return [.. limit.Concat(marketOrders)
			.GroupBy(static order => order.Id, StringComparer.Ordinal)
			.Select(static group => group.First())];
	}

	public async ValueTask<CoinSpotOrder[]> GetHistoryOrdersAsync(
		string coin,
		string market,
		DateTime? from,
		DateTime? to,
		int limit,
		CancellationToken cancellationToken)
	{
		var body = CreateMarketBody(coin, market);
		if (from is DateTime start)
			body["startdate"] = start.ToUniversalTime()
				.ToString("O", CultureInfo.InvariantCulture);
		if (to is DateTime end)
			body["enddate"] = end.ToUniversalTime()
				.ToString("O", CultureInfo.InvariantCulture);
		body["limit"] = limit.Max(1).Min(500);
		return DeserializeOrders(await SendPrivateAsync(
			_readOnlyEndpoint,
			"my/orders/completed",
			body,
			true,
			cancellationToken), true);
	}

	public async ValueTask<CoinSpotOrder> GetOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
	{
		orderId = orderId.ThrowIfEmpty(nameof(orderId));
		var orders = (await GetOpenOrdersAsync(
			null, null, cancellationToken))
			.Concat(await GetHistoryOrdersAsync(
				null, null, null, null, 500, cancellationToken));
		return orders.FirstOrDefault(order =>
			order.Id.EqualsIgnoreCase(orderId));
	}

	public async ValueTask<CoinSpotPlaceOrderResult> PlaceOrderAsync(
		CoinSpotPlaceOrderRequest order,
		CancellationToken cancellationToken)
	{
		if (order is null)
			throw new ArgumentNullException(nameof(order));
		var body = new JObject
		{
			["cointype"] = order.Coin.ThrowIfEmpty(nameof(order.Coin))
				.ToUpperInvariant(),
			["amount"] = order.Amount.ToWire(),
			["markettype"] = order.Market.ThrowIfEmpty(
				nameof(order.Market)).ToUpperInvariant(),
		};
		if (order.OrderType == OrderTypes.Limit)
			body["rate"] = order.Price.ToWire();
		else
			body["amounttype"] = "coin";
		var path = "my/" +
			(order.Side == Sides.Buy ? "buy" : "sell") +
			(order.OrderType == OrderTypes.Market ? "/now" : string.Empty);
		return DeserializePlaceOrder(await SendPrivateAsync(
			_tradingEndpoint,
			path,
			body,
			false,
			cancellationToken));
	}

	public async ValueTask CancelOrderAsync(
		Sides side,
		string orderId,
		CancellationToken cancellationToken)
	{
		_ = await SendPrivateAsync(
			_tradingEndpoint,
			$"my/{(side == Sides.Buy ? "buy" : "sell")}/cancel",
			new JObject
			{
				["id"] = orderId.ThrowIfEmpty(nameof(orderId)),
			},
			false,
			cancellationToken);
	}

	public async ValueTask CancelAllAsync(
		Sides side,
		string coin,
		CancellationToken cancellationToken)
	{
		var body = new JObject();
		if (!coin.IsEmpty())
			body["cointype"] = coin.ToUpperInvariant();
		_ = await SendPrivateAsync(
			_tradingEndpoint,
			$"my/{(side == Sides.Buy ? "buy" : "sell")}/cancel/all",
			body,
			false,
			cancellationToken);
	}

	internal static int NormalizeDepth(int depth)
		=> depth.Max(1).Min(200);

	internal static CoinSpotMarket[] DeserializeMarkets(string body)
	{
		var root = ParseRoot(body);
		var prices = root["prices"] as JObject ??
			(root["data"]?["prices"] as JObject);
		if (prices is null)
			throw new InvalidDataException(
				"CoinSpot latest-prices response contains no prices.");
		return [.. prices.Properties()
			.Select(property =>
			{
				var value = property.Value;
				return new CoinSpotMarket(
					property.Name,
					value.Value<decimal?>("bid"),
					value.Value<decimal?>("ask"),
					value.Value<decimal?>("last"));
			})
			.OrderBy(static market => market.SecurityCode,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static CoinSpotDepth DeserializeOrderBook(string body)
	{
		var root = ParseRoot(body);
		var bids = ParseQuotes(root["buyorders"]);
		var asks = ParseQuotes(root["sellorders"]);
		var market = root["market"]?.Value<string>() ??
			(root["buyorders"] as JArray)?.FirstOrDefault()?["market"]
				?.Value<string>() ??
			(root["sellorders"] as JArray)?.FirstOrDefault()?["market"]
				?.Value<string>();
		return new()
		{
			Market = NormalizeMarket(market),
			Bids = [.. bids.OrderByDescending(
				static quote => quote.Price)],
			Asks = [.. asks.OrderBy(static quote => quote.Price)],
			Time = ReadDate(root, "timestamp", "time"),
		};
	}

	internal static CoinSpotTrade[] DeserializePublicTrades(string body)
	{
		var root = ParseRoot(body);
		var trades = new List<CoinSpotTrade>();
		AddTrades(trades, root["buyorders"], Sides.Buy);
		AddTrades(trades, root["sellorders"], Sides.Sell);
		return [.. trades.OrderBy(static trade => trade.Time)
			.ThenBy(static trade => trade.Id,
				StringComparer.Ordinal)];
	}

	internal static CoinSpotBalance[] DeserializeBalances(string body)
	{
		var root = ParseRoot(body);
		if (root["balances"] is not JArray balances)
			return [];
		var result = new List<CoinSpotBalance>();

		foreach (var item in balances.OfType<JObject>())
		{
			foreach (var property in item.Properties())
			{
				if (property.Value is not JObject value)
					continue;
				result.Add(new()
				{
					Currency = property.Name.ToUpperInvariant(),
					Balance = value.Value<decimal?>("balance") ?? 0,
					Available = value.Value<decimal?>("available"),
					AudBalance =
						value.Value<decimal?>("audbalance") ?? 0,
					Rate = value.Value<decimal?>("rate") ?? 0,
				});
			}
		}

		return [.. result.OrderBy(
			static balance => balance.Currency,
			StringComparer.OrdinalIgnoreCase)];
	}

	internal static CoinSpotOrder[] DeserializeOrders(
		string body,
		bool isHistory)
	{
		var root = ParseRoot(body);
		var result = new List<CoinSpotOrder>();
		AddOrders(result, root["buyorders"], Sides.Buy, isHistory);
		AddOrders(result, root["sellorders"], Sides.Sell, isHistory);
		return [.. result];
	}

	internal static CoinSpotPlaceOrderResult DeserializePlaceOrder(
		string body)
	{
		var root = ParseRoot(body);
		return new()
		{
			Id = root.Value<string>("id"),
			Coin = root.Value<string>("coin")?.ToUpperInvariant(),
			Market = NormalizeMarket(root.Value<string>("market")),
			Amount = root.Value<decimal?>("amount") ?? 0,
			Rate = root.Value<decimal?>("rate") ?? 0,
		};
	}

	private async ValueTask<string> SendPublicAsync(
		string path,
		CancellationToken cancellationToken)
		=> await SendAsync(
			HttpMethod.Get,
			new Uri(_publicEndpoint, path.TrimStart('/')),
			null,
			null,
			true,
			cancellationToken);

	private async ValueTask<string> SendPrivateAsync(
		Uri endpoint,
		string path,
		JObject values,
		bool retryable,
		CancellationToken cancellationToken)
	{
		if (!IsCredentialsAvailable)
			throw new InvalidOperationException(
				"CoinSpot API key and secret are required for " +
					"private operations.");
		var payload = new JObject
		{
			["nonce"] = NextNonce(),
		};
		foreach (var property in values?.Properties() ?? [])
			payload[property.Name] = property.Value;
		var body = payload.ToString(Formatting.None);
		return await SendAsync(
			HttpMethod.Post,
			new Uri(endpoint, path.TrimStart('/')),
			body,
			request =>
			{
				request.Headers.TryAddWithoutValidation(
					"key", _authenticator.Key);
				request.Headers.TryAddWithoutValidation(
					"sign", _authenticator.Sign(body));
			},
			retryable,
			cancellationToken);
	}

	private async ValueTask<string> SendAsync(
		HttpMethod method,
		Uri target,
		string body,
		Action<HttpRequestMessage> prepare,
		bool retryable,
		CancellationToken cancellationToken)
	{
		var attempts = retryable ? _maximumReadAttempts : 1;
		Exception lastError = null;
		for (var attempt = 0; attempt < attempts; attempt++)
		{
			try
			{
				await WaitRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(method, target);
				if (body is not null)
					request.Content = new StringContent(
						body, Encoding.UTF8, "application/json");
				prepare?.Invoke(request);
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var responseBody =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (responseBody.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"CoinSpot response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
				{
					_ = ParseRoot(responseBody);
					return responseBody;
				}
				var error = CreateApiError(
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
			"CoinSpot API request failed.");
	}

	private long NextNonce()
	{
		while (true)
		{
			var current = Interlocked.Read(ref _lastNonce);
			var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(65);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static JObject ParseRoot(string body)
	{
		try
		{
			var root = JObject.Parse(
				body.ThrowIfEmpty(nameof(body)));
			var status = root.Value<string>("status");
			if (!status.IsEmpty() &&
				!status.EqualsIgnoreCase("ok") &&
				!status.EqualsIgnoreCase("success"))
				throw new CoinSpotApiException(
					HttpStatusCode.OK,
					root.Value<string>("message") ?? status);
			return root;
		}
		catch (CoinSpotApiException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinSpot returned malformed JSON.", error);
		}
	}

	private static IEnumerable<CoinSpotQuote> ParseQuotes(
		JToken values)
	{
		foreach (var value in values as JArray ?? [])
		{
			var price = value.Value<decimal?>("rate") ?? 0;
			var volume = value.Value<decimal?>("amount") ?? 0;
			if (price > 0 && volume > 0)
				yield return new()
				{
					Price = price,
					Volume = volume,
				};
		}
	}

	private static void AddTrades(
		ICollection<CoinSpotTrade> target,
		JToken values,
		Sides side)
	{
		foreach (var value in values as JArray ?? [])
		{
			var time = ReadDate(
				value, "solddate", "created", "date") ??
				DateTime.UnixEpoch;
			var price = value.Value<decimal?>("rate") ?? 0;
			var volume = value.Value<decimal?>("amount") ?? 0;
			var market = NormalizeMarket(
				value.Value<string>("market"));
			var id = value.Value<string>("id");
			if (id.IsEmpty())
				id = string.Join(
					"-",
					new DateTimeOffset(time).ToUnixTimeMilliseconds(),
					side,
					price.ToWire(),
					volume.ToWire());
			target.Add(new()
			{
				Id = id,
				Market = market,
				Price = price,
				Volume = volume,
				Time = time,
				Side = side,
			});
		}
	}

	private static void AddOrders(
		ICollection<CoinSpotOrder> target,
		JToken values,
		Sides side,
		bool isHistory)
	{
		foreach (var value in values as JArray ?? [])
		{
			var completed = ReadDate(
				value, "solddate", "completed", "updated");
			target.Add(new()
			{
				Id = value.Value<string>("id"),
				Coin = value.Value<string>("coin")
					?.ToUpperInvariant(),
				Market = NormalizeMarket(
					value.Value<string>("market")),
				Amount = value.Value<decimal?>("amount") ?? 0,
				Rate = value.Value<decimal?>("rate") ?? 0,
				Total = value.Value<decimal?>("total"),
				CreatedAt = ReadDate(value, "created", "createddate"),
				CompletedAt = completed,
				Side = side,
				State = isHistory || completed is not null
					? OrderStates.Done
					: OrderStates.Active,
				OrderType = value.Value<string>("ordertype")
					.EqualsIgnoreCase("market")
						? OrderTypes.Market
						: OrderTypes.Limit,
			});
		}
	}

	private static DateTime? ReadDate(
		JToken value,
		params string[] names)
	{
		foreach (var name in names)
		{
			var token = value?[name];
			if (token is null ||
				token.Type is JTokenType.Null or JTokenType.Undefined)
				continue;
			if (token.Type is JTokenType.Integer or JTokenType.Float)
			{
				var timestamp = token.Value<long>();
				return DateTimeOffset.FromUnixTimeMilliseconds(
					timestamp < 100_000_000_000
						? timestamp * 1000
						: timestamp).UtcDateTime;
			}
			if (DateTime.TryParse(
				token.Value<string>(),
				CultureInfo.InvariantCulture,
				DateTimeStyles.AssumeUniversal |
					DateTimeStyles.AdjustToUniversal,
				out var result))
				return result;
		}
		return null;
	}

	private static string NormalizeMarket(string market)
	{
		if (market.IsEmpty())
			return null;
		try
		{
			var (baseCurrency, quoteCurrency) =
				market.ToCoinSpotCurrencies();
			return CoinSpotExtensions.CreateSecurityCode(
				baseCurrency, quoteCurrency);
		}
		catch (FormatException)
		{
			return market.Trim().ToUpperInvariant();
		}
	}

	private static JObject CreateMarketBody(
		string coin,
		string market)
	{
		var body = new JObject();
		if (!coin.IsEmpty())
			body["cointype"] = coin.ToUpperInvariant();
		if (!market.IsEmpty())
			body["markettype"] = market.ToUpperInvariant();
		return body;
	}

	private static CoinSpotApiException CreateApiError(
		HttpStatusCode statusCode,
		string body,
		string reasonPhrase)
	{
		string details = null;
		try
		{
			details = JObject.Parse(body)
				.Value<string>("message");
		}
		catch (JsonException)
		{
		}
		details = details.IsEmpty()
			? reasonPhrase.IsEmpty() ? body : reasonPhrase
			: details;
		if (details?.Length > 512)
			details = details[..512];
		return new(statusCode, details);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static Uri ValidateEndpoint(
		string value,
		string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!value.EndsWith('/'))
			value += "/";
		if (!Uri.TryCreate(
			value,
			UriKind.Absolute,
			out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("https"))
			throw new ArgumentException(
				"CoinSpot endpoint must be an absolute HTTPS URI.",
				name);
		return endpoint;
	}
}
