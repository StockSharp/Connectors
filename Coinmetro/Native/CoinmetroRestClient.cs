namespace StockSharp.Coinmetro.Native;

sealed class CoinmetroRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 3;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;
	private string _token;

	public CoinmetroRestClient(
		string endpoint,
		SecureString token)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_token = token.IsEmpty() ? null : token.UnSecure().Trim();
		if (_token?.StartsWith(
			"Bearer ",
			StringComparison.OrdinalIgnoreCase) == true)
			_token = _token[7..].Trim();
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
			"StockSharp-Coinmetro-Connector/1.0");
	}

	public override string Name => "Coinmetro_REST";

	public bool IsCredentialsAvailable => !_token.IsEmpty();

	public string AccessToken => _token;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<string> GetDemoTokenAsync(
		CancellationToken cancellationToken)
	{
		var root = ParseObject(await SendAsync(
			HttpMethod.Get,
			"demo/temp",
			null,
			false,
			true,
			cancellationToken));
		_token = root.Value<string>("token")
			.ThrowIfEmpty("token");
		return _token;
	}

	public async ValueTask<CoinmetroAsset[]> GetAssetsAsync(
		CancellationToken cancellationToken)
		=> DeserializeAssets(await SendAsync(
			HttpMethod.Get,
			"assets",
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<CoinmetroMarketSpec[]>
		GetMarketSpecsAsync(
			CancellationToken cancellationToken)
		=> DeserializeMarketSpecs(await SendAsync(
			HttpMethod.Get,
			"markets",
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<CoinmetroTicker[]> GetTickersAsync(
		CancellationToken cancellationToken)
		=> DeserializeTickers(await SendAsync(
			HttpMethod.Get,
			"exchange/prices",
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<CoinmetroBook> GetBookAsync(
		string pair,
		CancellationToken cancellationToken)
		=> DeserializeBook(await SendAsync(
			HttpMethod.Get,
			$"exchange/book/{EscapePath(pair)}",
			null,
			false,
			true,
			cancellationToken));

	public async ValueTask<CoinmetroTrade[]> GetTradesAsync(
		string pair,
		DateTime? from,
		CancellationToken cancellationToken)
	{
		var path = $"exchange/ticks/{EscapePath(pair)}";
		if (from is DateTime timestamp)
			path += "/" +
				new DateTimeOffset(timestamp.ToUniversalTime())
					.ToUnixTimeMilliseconds()
					.ToString(CultureInfo.InvariantCulture);
		return DeserializeTrades(await SendAsync(
			HttpMethod.Get,
			path,
			null,
			false,
			true,
			cancellationToken));
	}

	public async ValueTask<CoinmetroCandle[]> GetCandlesAsync(
		string pair,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
	{
		var milliseconds = timeFrame.TotalMilliseconds.To<long>();
		var fromValue = new DateTimeOffset(
			from.ToUniversalTime()).ToUnixTimeMilliseconds();
		var toValue = new DateTimeOffset(
			to.ToUniversalTime()).ToUnixTimeMilliseconds();
		return DeserializeCandles(await SendAsync(
			HttpMethod.Get,
			$"exchange/candles/{EscapePath(pair)}/" +
				$"{milliseconds}/{fromValue}/{toValue}",
			null,
			false,
			true,
			cancellationToken));
	}

	public async ValueTask<CoinmetroWallet[]> GetWalletsAsync(
		CancellationToken cancellationToken)
		=> DeserializeWallets(await SendAsync(
			HttpMethod.Get,
			"users/wallets",
			null,
			true,
			true,
			cancellationToken));

	public async ValueTask<CoinmetroOrder[]> GetActiveOrdersAsync(
		IEnumerable<CoinmetroMarket> markets,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			"exchange/orders/active",
			null,
			true,
			true,
			cancellationToken), markets);

	public async ValueTask<CoinmetroOrder> GetOrderAsync(
		string orderId,
		IEnumerable<CoinmetroMarket> markets,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Get,
			$"exchange/orders/status/{EscapePath(orderId)}",
			null,
			true,
			true,
			cancellationToken), markets).FirstOrDefault();

	public async ValueTask<CoinmetroFill[]> GetFillsAsync(
		DateTime? from,
		CancellationToken cancellationToken)
	{
		var path = "exchange/fills";
		if (from is DateTime timestamp)
			path += "/" +
				new DateTimeOffset(timestamp.ToUniversalTime())
					.ToUnixTimeMilliseconds()
					.ToString(CultureInfo.InvariantCulture);
		return DeserializeFills(await SendAsync(
			HttpMethod.Get,
			path,
			null,
			true,
			true,
			cancellationToken));
	}

	public async ValueTask<CoinmetroOrder> PlaceOrderAsync(
		CoinmetroMarket market,
		Sides side,
		OrderTypes orderType,
		decimal volume,
		decimal price,
		TimeInForce? timeInForce,
		DateTime? tillDate,
		IEnumerable<CoinmetroMarket> markets,
		CancellationToken cancellationToken)
	{
		var values = CreateOrderForm(
			market,
			side,
			orderType,
			volume,
			price,
			timeInForce,
			tillDate);
		return DeserializeOrders(await SendAsync(
			HttpMethod.Post,
			"exchange/orders/create",
			values,
			true,
			false,
			cancellationToken), markets).FirstOrDefault();
	}

	public async ValueTask<CoinmetroOrder> CancelOrderAsync(
		string orderId,
		IEnumerable<CoinmetroMarket> markets,
		CancellationToken cancellationToken)
		=> DeserializeOrders(await SendAsync(
			HttpMethod.Put,
			$"exchange/orders/cancel/{EscapePath(orderId)}",
			new Dictionary<string, string>(),
			true,
			false,
			cancellationToken), markets).FirstOrDefault();

	internal static CoinmetroAsset[] DeserializeAssets(string body)
		=> [.. ParseArray(body)
			.OfType<JObject>()
			.Select(static value => new CoinmetroAsset
			{
				Symbol = value.Value<string>("symbol")
					?.ToUpperInvariant(),
				Name = value.Value<string>("name"),
				Digits = value.Value<int?>("digits") ?? 0,
				BookDigits =
					value.Value<int?>("bookDigits") ??
					value.Value<int?>("digits") ?? 0,
				MinimumQuantity =
					ReadDecimal(value["minQty"]),
			})
			.Where(static asset => !asset.Symbol.IsEmpty())
			.OrderBy(static asset => asset.Symbol,
				StringComparer.OrdinalIgnoreCase)];

	internal static CoinmetroMarketSpec[] DeserializeMarketSpecs(
		string body)
		=> [.. ParseArray(body)
			.OfType<JObject>()
			.Select(static value => new CoinmetroMarketSpec
			{
				Pair = value.Value<string>("pair")
					?.ToUpperInvariant(),
				Precision = value.Value<int?>("precision") ?? 0,
				IsMarginSupported =
					ReadBoolean(value["margin"]),
			})
			.Where(static market => !market.Pair.IsEmpty())
			.OrderBy(static market => market.Pair,
				StringComparer.OrdinalIgnoreCase)];

	internal static CoinmetroTicker[] DeserializeTickers(
		string body)
	{
		var root = ParseObject(body);
		return [.. (root["latestPrices"] as JArray ?? [])
			.OfType<JObject>()
			.Select(ParseTicker)
			.Where(static ticker => ticker is not null)
			.OrderBy(static ticker => ticker.Pair,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static CoinmetroMarket[] CreateMarkets(
		IEnumerable<CoinmetroAsset> assets,
		IEnumerable<CoinmetroMarketSpec> marketSpecs,
		IEnumerable<CoinmetroTicker> tickers)
	{
		var assetMap = (assets ?? [])
			.Where(static asset => !asset.Symbol.IsEmpty())
			.GroupBy(
				static asset => asset.Symbol,
				StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				static group => group.Key,
				static group => group.First(),
				StringComparer.OrdinalIgnoreCase);
		var tickerMap = (tickers ?? [])
			.Where(static ticker => !ticker.Pair.IsEmpty())
			.GroupBy(
				static ticker => ticker.Pair,
				StringComparer.OrdinalIgnoreCase)
			.ToDictionary(
				static group => group.Key,
				static group => group.First(),
				StringComparer.OrdinalIgnoreCase);
		var result = new List<CoinmetroMarket>();

		foreach (var spec in marketSpecs ?? [])
		{
			tickerMap.TryGetValue(spec.Pair, out var ticker);
			var baseCurrency = ticker?.BaseCurrency;
			var quoteCurrency = ticker?.QuoteCurrency;
			if (baseCurrency.IsEmpty() || quoteCurrency.IsEmpty())
				ResolvePair(
					spec.Pair,
					assetMap.Keys,
					out baseCurrency,
					out quoteCurrency);
			if (baseCurrency.IsEmpty() ||
				quoteCurrency.IsEmpty())
				continue;
			assetMap.TryGetValue(baseCurrency, out var baseAsset);
			result.Add(new()
			{
				Pair = spec.Pair,
				BaseCurrency = baseCurrency.ToUpperInvariant(),
				QuoteCurrency = quoteCurrency.ToUpperInvariant(),
				PricePrecision = spec.Precision,
				AmountPrecision = baseAsset?.Digits ?? 0,
				BookAmountPrecision =
					baseAsset?.BookDigits ??
					baseAsset?.Digits ?? 0,
				MinimumAmount =
					baseAsset?.MinimumQuantity ?? 0,
				IsMarginSupported = spec.IsMarginSupported,
			});
		}

		return [.. result.OrderBy(
			static market => market.SecurityCode,
			StringComparer.OrdinalIgnoreCase)];
	}

	internal static CoinmetroBook DeserializeBook(string body)
	{
		var root = ParseObject(body);
		var value = root["book"] as JObject ?? root;
		return ParseBook(value);
	}

	internal static CoinmetroTrade[] DeserializeTrades(string body)
	{
		var root = ParseObject(body);
		return [.. (root["tickHistory"] as JArray ?? [])
			.OfType<JObject>()
			.Select(ParseTrade)
			.Where(static trade => trade is not null)
			.OrderBy(static trade => trade.Time)];
	}

	internal static CoinmetroCandle[] DeserializeCandles(
		string body)
	{
		var root = ParseObject(body);
		return [.. (root["candleHistory"] as JArray ?? [])
			.OfType<JObject>()
			.Select(static value => new CoinmetroCandle
			{
				Pair = value.Value<string>("pair")
					?.ToUpperInvariant(),
				TimeFrame = TimeSpan.FromMilliseconds(
					ReadLong(value["timeframe"])),
				OpenTime = ReadTimestamp(value["timestamp"]),
				Open = ReadDecimal(value["o"]),
				High = ReadDecimal(value["h"]),
				Low = ReadDecimal(value["l"]),
				Close = ReadDecimal(value["c"]),
				Volume = ReadDecimal(value["v"]),
			})
			.Where(static candle =>
				!candle.Pair.IsEmpty() &&
				candle.TimeFrame > TimeSpan.Zero)
			.OrderBy(static candle => candle.OpenTime)];
	}

	internal static CoinmetroWallet[] DeserializeWallets(
		string body)
	{
		var root = ParseObject(body);
		return [.. (root["list"] as JArray ?? [])
			.OfType<JObject>()
			.Select(ParseWallet)
			.Where(static wallet => wallet is not null)
			.OrderBy(static wallet => wallet.Currency,
				StringComparer.OrdinalIgnoreCase)];
	}

	internal static CoinmetroOrder[] DeserializeOrders(
		string body,
		IEnumerable<CoinmetroMarket> markets)
	{
		var token = ParseToken(body);
		IEnumerable<JObject> values = token switch
		{
			JArray array => array.OfType<JObject>(),
			JObject root when root["orderStatus"] is JObject order =>
				[order],
			JObject root => [root],
			_ => [],
		};
		var knownMarkets = (markets ?? []).ToArray();
		return [.. values
			.Select(value => ParseOrder(value, knownMarkets))
			.Where(static order => order is not null)];
	}

	internal static CoinmetroFill[] DeserializeFills(string body)
		=> [.. ParseArray(body)
			.OfType<JObject>()
			.Select(static value => ParseFill(value))
			.Where(static fill => fill is not null)
			.OrderBy(static fill => fill.Time)];

	internal static Dictionary<string, string> CreateOrderForm(
		CoinmetroMarket market,
		Sides side,
		OrderTypes orderType,
		decimal volume,
		decimal price,
		TimeInForce? timeInForce,
		DateTime? tillDate)
	{
		if (market is null)
			throw new ArgumentNullException(nameof(market));
		var values = new Dictionary<string, string>(
			StringComparer.Ordinal)
		{
			["orderType"] = orderType == OrderTypes.Market
				? "market"
				: "limit",
		};
		if (side == Sides.Buy)
		{
			values["buyingCurrency"] = market.BaseCurrency;
			values["sellingCurrency"] = market.QuoteCurrency;
			values["buyingQty"] = volume.ToWire();
			if (orderType == OrderTypes.Limit)
				values["sellingQty"] =
					(volume * price).ToWire();
		}
		else
		{
			values["buyingCurrency"] = market.QuoteCurrency;
			values["sellingCurrency"] = market.BaseCurrency;
			values["sellingQty"] = volume.ToWire();
			if (orderType == OrderTypes.Limit)
				values["buyingQty"] =
					(volume * price).ToWire();
		}
		values["timeInForce"] =
			timeInForce.ToCoinmetro(tillDate).ToString(
				CultureInfo.InvariantCulture);
		if (tillDate is DateTime expiry)
			values["expirationTime"] =
				new DateTimeOffset(expiry.ToUniversalTime())
					.ToUnixTimeMilliseconds()
					.ToString(CultureInfo.InvariantCulture);
		return values;
	}

	internal static CoinmetroTicker ParseTicker(JObject value)
	{
		if (value?.Value<string>("pair").IsEmpty() != false)
			return null;
		return new()
		{
			Pair = value.Value<string>("pair").ToUpperInvariant(),
			BaseCurrency = value.Value<string>("base")
				?.ToUpperInvariant(),
			QuoteCurrency = value.Value<string>("quote")
				?.ToUpperInvariant(),
			Time = ReadTimestamp(value["timestamp"]),
			Sequence = ReadLong(
				value["seqNum"] ?? value["seqNumber"]),
			Price = ReadDecimal(value["price"]),
			Volume = ReadDecimal(value["qty"]),
			Ask = ReadNullableDecimal(value["ask"]),
			Bid = ReadNullableDecimal(value["bid"]),
		};
	}

	internal static CoinmetroBook ParseBook(JObject value)
	{
		if (value is null)
			return null;
		return new()
		{
			Pair = value.Value<string>("pair")?.ToUpperInvariant(),
			Sequence = ReadLong(value["seqNumber"]),
			Checksum = ReadInt(value["checksum"]),
			Asks = ParseQuotes(value["ask"], true),
			Bids = ParseQuotes(value["bid"], false),
		};
	}

	internal static CoinmetroBookUpdate ParseBookUpdate(
		JObject value)
	{
		if (value is null)
			return null;
		return new()
		{
			Pair = value.Value<string>("pair")?.ToUpperInvariant(),
			Sequence = ReadLong(value["seqNumber"]),
			Checksum = ReadInt(value["checksum"]),
			Asks = ParseQuotes(value["ask"], true, false),
			Bids = ParseQuotes(value["bid"], false, false),
		};
	}

	internal static CoinmetroTrade ParseTrade(JObject value)
	{
		if (value?.Value<string>("pair").IsEmpty() != false)
			return null;
		var price = ReadDecimal(value["price"]);
		var volume = ReadDecimal(value["qty"]);
		if (price <= 0 || volume <= 0)
			return null;
		var sequence = ReadLong(
			value["seqNum"] ?? value["seqNumber"]);
		var sideText = value.Value<string>("side");
		return new()
		{
			Id = sequence > 0
				? sequence.ToString(CultureInfo.InvariantCulture)
				: $"{ReadLong(value["timestamp"])}:" +
					$"{price.ToWire()}:{volume.ToWire()}",
			Pair = value.Value<string>("pair").ToUpperInvariant(),
			Time = ReadTimestamp(value["timestamp"]),
			Price = price,
			Volume = volume,
			Side = sideText.IsEmpty()
				? null
				: sideText.EqualsIgnoreCase("buy")
					? Sides.Buy
					: Sides.Sell,
		};
	}

	internal static CoinmetroWallet ParseWallet(JObject value)
	{
		if (value?.Value<string>("currency").IsEmpty() != false)
			return null;
		return new()
		{
			Id =
				value.Value<string>("id") ??
				value.Value<string>("walletId") ??
				value.Value<string>("_id"),
			Currency = value.Value<string>("currency")
				.ToUpperInvariant(),
			Label = value.Value<string>("label"),
			Total = ReadDecimal(
				value["balance"] ?? value["total"]),
			Reserved = ReadDecimal(value["reserved"]),
		};
	}

	internal static CoinmetroOrder ParseOrder(
		JObject value,
		IEnumerable<CoinmetroMarket> markets)
	{
		if (value is null)
			return null;
		var id =
			value.Value<string>("orderID") ??
			value.Value<string>("orderId");
		var buyingCurrency =
			value.Value<string>("buyingCurrency")
				?.ToUpperInvariant();
		var sellingCurrency =
			value.Value<string>("sellingCurrency")
				?.ToUpperInvariant();
		if (id.IsEmpty() ||
			buyingCurrency.IsEmpty() ||
			sellingCurrency.IsEmpty())
			return null;
		var market = (markets ?? []).FirstOrDefault(item =>
			item.BaseCurrency.EqualsIgnoreCase(buyingCurrency) &&
				item.QuoteCurrency.EqualsIgnoreCase(sellingCurrency) ||
			item.BaseCurrency.EqualsIgnoreCase(sellingCurrency) &&
				item.QuoteCurrency.EqualsIgnoreCase(buyingCurrency));
		if (market is null)
			return null;
		var isBuy = market.BaseCurrency.EqualsIgnoreCase(
			buyingCurrency);
		var buyingQuantity = ReadDecimal(value["buyingQty"]);
		var sellingQuantity = ReadDecimal(value["sellingQty"]);
		var boughtQuantity = ReadDecimal(value["boughtQty"]);
		var soldQuantity = ReadDecimal(value["soldQty"]);
		var original = isBuy
			? buyingQuantity
			: sellingQuantity;
		var executed = isBuy
			? boughtQuantity
			: soldQuantity;
		var fills = (value["fills"] as JArray ?? [])
			.OfType<JObject>()
			.Select(fill => ParseFill(
				fill, id, market.Pair))
			.Where(static fill => fill is not null)
			.ToArray();
		var completion = ReadNullableTimestamp(
			value["completionTime"] ?? value["closedTime"]);
		var price = original > 0
			? isBuy
				? sellingQuantity / original
				: buyingQuantity / original
			: fills.Length > 0
				? fills[^1].Price
				: 0;
		return new()
		{
			Id = id,
			Pair = market.Pair,
			BuyingCurrency = buyingCurrency,
			SellingCurrency = sellingCurrency,
			Side = isBuy ? Sides.Buy : Sides.Sell,
			OrderType = value.Value<string>("orderType")
				.ToOrderType(),
			TimeInForce =
				ReadInt(value["timeInForce"]).ToTimeInForce(),
			State = completion is null
				? OrderStates.Active
				: OrderStates.Done,
			CreatedAt = ReadTimestamp(value["creationTime"]),
			CompletedAt = completion,
			Price = price,
			OriginalAmount = original,
			RemainingAmount = Math.Max(0, original - executed),
			Fees = ReadDecimal(value["fees"]),
			Fills = fills,
		};
	}

	private static CoinmetroFill ParseFill(
		JObject value,
		string orderId = null,
		string pair = null)
	{
		if (value is null)
			return null;
		var sequence = ReadLong(value["seqNumber"]);
		var price = ReadDecimal(value["price"]);
		var volume = ReadDecimal(value["qty"]);
		pair ??= value.Value<string>("pair");
		orderId ??=
			value.Value<string>("orderID") ??
			value.Value<string>("orderId");
		if (pair.IsEmpty() || price <= 0 || volume <= 0)
			return null;
		return new()
		{
			Id = sequence > 0
				? sequence.ToString(CultureInfo.InvariantCulture)
				: value.Value<string>("_id"),
			OrderId = orderId,
			Pair = pair.ToUpperInvariant(),
			Time = ReadTimestamp(value["timestamp"]),
			Price = price,
			Volume = volume,
			Side = value.Value<string>("side")
				.EqualsIgnoreCase("buy")
					? Sides.Buy
					: Sides.Sell,
		};
	}

	private async ValueTask<string> SendAsync(
		HttpMethod method,
		string path,
		IReadOnlyDictionary<string, string> form,
		bool isPrivate,
		bool retryable,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Coinmetro bearer token is required for private " +
					"operations.");
		var target = new Uri(
			_endpoint,
			path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
		var attempts = retryable ? _maximumReadAttempts : 1;
		Exception lastError = null;
		for (var attempt = 0; attempt < attempts; attempt++)
		{
			try
			{
				await WaitRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(
					method, target);
				if (form is not null)
					request.Content =
						new FormUrlEncodedContent(form);
				if (isPrivate)
					request.Headers.Authorization =
						new AuthenticationHeaderValue(
							"Bearer", _token);
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				var responseBody =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (responseBody.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"Coinmetro response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
				{
					_ = ParseToken(responseBody);
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
			"Coinmetro API request failed.");
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

	private static void ResolvePair(
		string pair,
		IEnumerable<string> symbols,
		out string baseCurrency,
		out string quoteCurrency)
	{
		baseCurrency = null;
		quoteCurrency = null;
		if (pair.IsEmpty())
			return;
		pair = pair.ToUpperInvariant();
		var known = new HashSet<string>(
			symbols ?? [],
			StringComparer.OrdinalIgnoreCase);
		foreach (var symbol in known.OrderByDescending(
			static value => value.Length))
		{
			if (!pair.StartsWith(
				symbol,
				StringComparison.OrdinalIgnoreCase))
				continue;
			var remainder = pair[symbol.Length..];
			if (!known.Contains(remainder))
				continue;
			baseCurrency = symbol;
			quoteCurrency = remainder;
			return;
		}
	}

	private static CoinmetroQuote[] ParseQuotes(
		JToken token,
		bool isAsk,
		bool sort = true)
	{
		if (token is not JObject values)
			return [];
		var quotes = values.Properties()
			.Select(static property => new CoinmetroQuote
			{
				Price = decimal.TryParse(
					property.Name,
					NumberStyles.Float,
					CultureInfo.InvariantCulture,
					out var price)
						? price
						: 0,
				Volume = ReadDecimal(property.Value),
			})
			.Where(static quote => quote.Price > 0);
		if (!sort)
			return [.. quotes];
		return [.. (isAsk
			? quotes
				.Where(static quote => quote.Volume > 0)
				.OrderBy(static quote => quote.Price)
			: quotes
				.Where(static quote => quote.Volume > 0)
				.OrderByDescending(static quote => quote.Price))];
	}

	private static JToken ParseToken(string body)
	{
		try
		{
			var token = JToken.Parse(
				body.ThrowIfEmpty(nameof(body)));
			if (token is JObject root &&
				(root.Value<string>("status")
					.EqualsIgnoreCase("fail") ||
					root["error"] is not null))
				throw new InvalidDataException(
					"Coinmetro request failed: " +
					(root.Value<string>("reason") ??
						root["error"]?.ToString() ??
						root.Value<string>("message")));
			return token;
		}
		catch (InvalidDataException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Coinmetro returned malformed JSON.", error);
		}
	}

	private static JObject ParseObject(string body)
		=> ParseToken(body) as JObject ??
			throw new InvalidDataException(
				"Coinmetro returned an unexpected JSON shape.");

	private static JArray ParseArray(string body)
		=> ParseToken(body) as JArray ??
			throw new InvalidDataException(
				"Coinmetro returned an unexpected JSON shape.");

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
			? timestamp.FromCoinmetroTimestamp()
			: null;
	}

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
				root.Value<string>("reason") ??
				root["error"]?.ToString() ??
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
			$"Coinmetro HTTP {(int)statusCode} " +
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
				"Coinmetro endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}
}
