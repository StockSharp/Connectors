namespace StockSharp.CoinSwitch.Native;

sealed class CoinSwitchApiException(
	HttpStatusCode statusCode,
	string message)
	: InvalidOperationException(
		$"CoinSwitch API error {(int)statusCode} ({statusCode}): {message}")
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class CoinSwitchRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _restEndpoint;
	private readonly Uri _hftEndpoint;
	private readonly HttpClient _http;
	private readonly string _key;
	private readonly CoinSwitchSigner _signer;
	private readonly string _spotExchange;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private DateTime _nextRequestTime;

	public CoinSwitchRestClient(
		string restEndpoint,
		string hftEndpoint,
		SecureString key,
		SecureString secret,
		string spotExchange)
	{
		_restEndpoint = ValidateEndpoint(
			restEndpoint, nameof(restEndpoint));
		_hftEndpoint = ValidateEndpoint(
			hftEndpoint, nameof(hftEndpoint));
		_key = key.IsEmpty() ? null : key.UnSecure().Trim();
		_signer = secret.IsEmpty() ? null : new(secret);
		_spotExchange = spotExchange.ThrowIfEmpty(
			nameof(spotExchange)).Trim().ToLowerInvariant();
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
			"StockSharp-CoinSwitch-Connector/1.0");
	}

	public override string Name => "CoinSwitch_REST";

	public bool IsCredentialsAvailable
		=> !_key.IsEmpty() && _signer is not null;

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_signer?.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<long> GetServerTimeAsync(
		CancellationToken cancellationToken)
		=> SendAsync<long>(
			HttpMethod.Get,
			"/trade/api/v2/time",
			[],
			null,
			false,
			false,
			true,
			cancellationToken);

	public async ValueTask<string[]> GetSpotSymbolsAsync(
		CancellationToken cancellationToken)
		=> DeserializeSpotSymbols(
			await SendRawAsync(
				HttpMethod.Get,
				"/trade/api/v2/coins",
				Values(("exchange", _spotExchange)),
				null,
				false,
				true,
				true,
				cancellationToken),
			_spotExchange);

	public async ValueTask<Dictionary<string, CoinSwitchSpotTradeInfo>>
		GetSpotTradeInfoAsync(
			string symbol,
			CancellationToken cancellationToken)
		=> DeserializeSpotTradeInfo(
			await SendRawAsync(
				HttpMethod.Get,
				"/trade/api/v2/tradeInfo",
				Values(
					("exchange", _spotExchange),
					("symbol", symbol.IsEmpty()
						? null
						: NormalizeSpotSymbol(symbol))),
				null,
				false,
				true,
				true,
				cancellationToken),
			_spotExchange);

	public async ValueTask<CoinSwitchTicker> GetSpotTickerAsync(
		string symbol,
		CancellationToken cancellationToken)
	{
		symbol = NormalizeSpotSymbol(symbol);
		return DeserializeSpotTicker(
			await SendRawAsync(
				HttpMethod.Get,
				"/trade/api/v2/24hr/ticker",
				Values(
					("exchange", _spotExchange),
					("symbol", symbol)),
				null,
				false,
				true,
				true,
				cancellationToken),
			symbol);
	}

	public ValueTask<CoinSwitchDepth> GetSpotDepthAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchDepth>(
			HttpMethod.Get,
			"/trade/api/v2/depth",
			Values(
				("exchange", _spotExchange),
				("symbol", NormalizeSpotSymbol(symbol)),
				("limit", NormalizeDepth(depth))),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchTrade[]> GetSpotTradesAsync(
		string symbol,
		int count,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchTrade[]>(
			HttpMethod.Get,
			"/trade/api/v2/trades",
			Values(
				("exchange", _spotExchange),
				("symbol", NormalizeSpotSymbol(symbol)),
				("limit", NormalizeTradeLimit(count))),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchCandle[]> GetSpotCandlesAsync(
		string symbol,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchCandle[]>(
			HttpMethod.Get,
			"/trade/api/v2/candles",
			Values(
				("exchange", _spotExchange),
				("symbol", NormalizeSpotSymbol(symbol)),
				("interval", timeFrame.ToCoinSwitchInterval()),
				("start_time", from.ToCoinSwitchMilliseconds()),
				("end_time", to.ToCoinSwitchMilliseconds())),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchSpotBalance[]> GetSpotBalancesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchSpotBalance[]>(
			HttpMethod.Get,
			"/trade/api/v2/user/portfolio",
			[],
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchSpotOrder> PlaceSpotOrderAsync(
		CoinSwitchSpotOrderRequest order,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchSpotOrder>(
			HttpMethod.Post,
			"/trade/api/v2/order",
			[],
			order ?? throw new ArgumentNullException(nameof(order)),
			false,
			true,
			false,
			cancellationToken);

	public ValueTask<CoinSwitchSpotOrder> CancelSpotOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchSpotOrder>(
			HttpMethod.Delete,
			"/trade/api/v2/order",
			[],
			new
			{
				order_id = orderId.ThrowIfEmpty(nameof(orderId)).Trim(),
			},
			false,
			true,
			false,
			cancellationToken);

	public ValueTask<CoinSwitchSpotOrder> GetSpotOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchSpotOrder>(
			HttpMethod.Get,
			"/trade/api/v2/order",
			Values(("order_id",
				orderId.ThrowIfEmpty(nameof(orderId)).Trim())),
			null,
			false,
			true,
			true,
			cancellationToken);

	public async ValueTask<CoinSwitchSpotOrder[]> GetSpotOrdersAsync(
		bool? open,
		string symbol,
		int count,
		DateTime? from,
		DateTime? to,
		CancellationToken cancellationToken)
		=> DeserializeList<CoinSwitchSpotOrder>(
			await SendRawAsync(
				HttpMethod.Get,
				"/trade/api/v2/orders",
				Values(
					("open", open?.ToString().ToLowerInvariant()),
					("count", NormalizeOrderLimit(count)),
					("from_time", from?.ToCoinSwitchMilliseconds()),
					("to_time", to?.ToCoinSwitchMilliseconds()),
					("symbols", symbol.IsEmpty()
						? null
						: NormalizeSpotSymbol(symbol)),
					("exchanges", _spotExchange)),
				null,
				false,
				true,
				true,
				cancellationToken));

	public async ValueTask<CoinSwitchFuturesInstrument[]>
		GetFuturesInstrumentsAsync(
			CancellationToken cancellationToken)
		=> DeserializeFuturesInstruments(
			await SendRawAsync(
				HttpMethod.Get,
				"/trade/api/v2/futures/instrument_info",
				Values(("exchange", "EXCHANGE_2")),
				null,
				false,
				true,
				true,
				cancellationToken));

	public async ValueTask<CoinSwitchFuturesTicker>
		GetFuturesTickerAsync(
			string symbol,
			CancellationToken cancellationToken)
		=> DeserializeFuturesTicker(
			await SendRawAsync(
				HttpMethod.Get,
				"/trade/api/v2/futures/ticker",
				Values(
					("exchange", "EXCHANGE_2"),
					("symbol", NormalizeFuturesSymbol(symbol))),
				null,
				false,
				true,
				true,
				cancellationToken),
			"EXCHANGE_2");

	public ValueTask<CoinSwitchDepth> GetFuturesDepthAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchDepth>(
			HttpMethod.Get,
			"/trade/api/v2/futures/order_book",
			Values(
				("exchange", "EXCHANGE_2"),
				("symbol", NormalizeFuturesSymbol(symbol)),
				("limit", NormalizeDepth(depth)),
				("l2Orderbook", true)),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchTrade[]> GetFuturesTradesAsync(
		string symbol,
		int count,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchTrade[]>(
			HttpMethod.Get,
			"/trade/api/v2/futures/trades",
			Values(
				("exchange", "EXCHANGE_2"),
				("symbol", NormalizeFuturesSymbol(symbol)),
				("limit", NormalizeTradeLimit(count))),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchCandle[]> GetFuturesCandlesAsync(
		string symbol,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchCandle[]>(
			HttpMethod.Get,
			"/trade/api/v2/futures/klines",
			Values(
				("exchange", "EXCHANGE_2"),
				("symbol", NormalizeFuturesSymbol(symbol)),
				("interval", timeFrame.ToCoinSwitchInterval()),
				("start_time", from.ToCoinSwitchMilliseconds()),
				("end_time", to.ToCoinSwitchMilliseconds())),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchFuturesBalances>
		GetFuturesBalancesAsync(
			CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchFuturesBalances>(
			HttpMethod.Get,
			"/trade/api/v2/futures/wallet_balance",
			Values(("exchange", "EXCHANGE_2")),
			null,
			false,
			true,
			true,
			cancellationToken);

	public ValueTask<CoinSwitchFuturesOrder> PlaceFuturesOrderAsync(
		CoinSwitchFuturesOrderRequest order,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchFuturesOrder>(
			HttpMethod.Post,
			"/trade/api/v2/futures/order",
			[],
			order ?? throw new ArgumentNullException(nameof(order)),
			false,
			true,
			false,
			cancellationToken);

	public ValueTask<CoinSwitchFuturesOrder> CancelFuturesOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<CoinSwitchFuturesOrder>(
			HttpMethod.Delete,
			"/trade/api/v2/futures/order",
			[],
			new
			{
				exchange = "EXCHANGE_2",
				order_id = orderId.ThrowIfEmpty(nameof(orderId)).Trim(),
			},
			false,
			true,
			false,
			cancellationToken);

	public async ValueTask<CoinSwitchFuturesOrder> GetFuturesOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			HttpMethod.Get,
			"/trade/api/v2/futures/order",
			Values(
				("exchange", "EXCHANGE_2"),
				("order_id",
					orderId.ThrowIfEmpty(nameof(orderId)).Trim())),
			null,
			false,
			true,
			true,
			cancellationToken);
		var data = DeserializeToken(raw);
		return (data["order"] ?? data)
			.ToObject<CoinSwitchFuturesOrder>(
				JsonSerializer.Create(CreateJsonSettings()));
	}

	public async ValueTask<CoinSwitchFuturesOrder[]>
		GetFuturesOrdersAsync(
			bool open,
			string symbol,
			int count,
			DateTime? from,
			DateTime? to,
			CancellationToken cancellationToken)
		=> DeserializeList<CoinSwitchFuturesOrder>(
			await SendRawAsync(
				HttpMethod.Post,
				open
					? "/trade/api/v2/futures/orders/open"
					: "/trade/api/v2/futures/orders/closed",
				[],
				new CoinSwitchFuturesOrderQuery
				{
					Exchange = "EXCHANGE_2",
					Symbol = symbol.IsEmpty()
						? null
						: NormalizeFuturesSymbol(symbol),
					Limit = NormalizeOrderLimit(count),
					FromTime = from?.ToCoinSwitchMilliseconds(),
					ToTime = to?.ToCoinSwitchMilliseconds(),
				},
				false,
				true,
				true,
				cancellationToken));

	public async ValueTask<CoinSwitchHftInstrument[]>
		GetHftInstrumentsAsync(
			CancellationToken cancellationToken)
		=> DeserializeHftInstruments(
			await SendRawAsync(
				HttpMethod.Get,
				"/v5/market/instruments-info",
				Values(("category", "option")),
				null,
				true,
				true,
				true,
				cancellationToken));

	public async ValueTask<CoinSwitchHftTicker[]> GetHftTickersAsync(
		string symbol,
		CancellationToken cancellationToken)
		=> DeserializeHftList<CoinSwitchHftTicker>(
			await SendRawAsync(
				HttpMethod.Get,
				"/v5/market/tickers",
				Values(
					("category", "option"),
					("symbol", symbol.IsEmpty()
						? null
						: symbol.Trim().ToUpperInvariant())),
				null,
				true,
				true,
				true,
				cancellationToken));

	public ValueTask<CoinSwitchHftOrderBook> GetHftDepthAsync(
		string symbol,
		int depth,
		CancellationToken cancellationToken)
		=> SendHftResultAsync<CoinSwitchHftOrderBook>(
			HttpMethod.Get,
			"/v5/market/orderbook",
			Values(
				("category", "option"),
				("symbol", NormalizeOptionSymbol(symbol)),
				("limit", NormalizeDepth(depth))),
			null,
			true,
			cancellationToken);

	public async ValueTask<CoinSwitchHftTrade[]> GetHftTradesAsync(
		string symbol,
		int count,
		CancellationToken cancellationToken)
		=> DeserializeHftList<CoinSwitchHftTrade>(
			await SendRawAsync(
				HttpMethod.Get,
				"/v5/market/recent-trade",
				Values(
					("category", "option"),
					("symbol", NormalizeOptionSymbol(symbol)),
					("limit", NormalizeTradeLimit(count))),
				null,
				true,
				true,
				true,
				cancellationToken));

	public async ValueTask<CoinSwitchHftCandle[]> GetHftCandlesAsync(
		string symbol,
		TimeSpan timeFrame,
		DateTime from,
		DateTime to,
		int count,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			HttpMethod.Get,
			"/v5/market/kline",
			Values(
				("category", "option"),
				("symbol", NormalizeOptionSymbol(symbol)),
				("interval", timeFrame.ToCoinSwitchInterval()),
				("start", from.ToCoinSwitchMilliseconds()),
				("end", to.ToCoinSwitchMilliseconds()),
				("limit", NormalizeCandleLimit(count))),
			null,
			true,
			true,
			true,
			cancellationToken);
		return DeserializeHftCandles(raw);
	}

	public ValueTask<CoinSwitchHftOrderResult> PlaceHftOrderAsync(
		CoinSwitchHftOrderRequest order,
		CancellationToken cancellationToken)
		=> SendHftResultAsync<CoinSwitchHftOrderResult>(
			HttpMethod.Post,
			"/v5/order/create",
			[],
			order ?? throw new ArgumentNullException(nameof(order)),
			false,
			cancellationToken);

	public ValueTask<CoinSwitchHftOrderResult> CancelHftOrderAsync(
		string symbol,
		string orderId,
		CancellationToken cancellationToken)
		=> SendHftResultAsync<CoinSwitchHftOrderResult>(
			HttpMethod.Post,
			"/v5/order/cancel",
			[],
			new
			{
				category = "option",
				symbol = NormalizeOptionSymbol(symbol),
				orderId = orderId.ThrowIfEmpty(nameof(orderId)).Trim(),
			},
			false,
			cancellationToken);

	public async ValueTask<CoinSwitchHftOrder[]> GetHftOrdersAsync(
		string symbol,
		string orderId,
		int count,
		CancellationToken cancellationToken)
		=> DeserializeHftList<CoinSwitchHftOrder>(
			await SendRawAsync(
				HttpMethod.Get,
				"/v5/order/realtime",
				Values(
					("category", "option"),
					("symbol", symbol.IsEmpty()
						? null
						: NormalizeOptionSymbol(symbol)),
					("orderId", orderId),
					("limit", NormalizeOrderLimit(count))),
				null,
				true,
				true,
				true,
				cancellationToken));

	public async ValueTask<CoinSwitchHftWallet[]> GetHftWalletsAsync(
		CancellationToken cancellationToken)
		=> DeserializeHftList<CoinSwitchHftWallet>(
			await SendRawAsync(
				HttpMethod.Get,
				"/v5/account/wallet-balance",
				Values(("accountType", "UNIFIED")),
				null,
				true,
				true,
				true,
				cancellationToken));

	internal static TData Deserialize<TData>(string body)
	{
		var token = DeserializeToken(body);
		try
		{
			return token.Type == JTokenType.Null
				? default
				: token.ToObject<TData>(
					JsonSerializer.Create(CreateJsonSettings()));
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinSwitch returned malformed response data.",
				error);
		}
	}

	internal static string[] DeserializeSpotSymbols(
		string body,
		string exchange)
	{
		var data = DeserializeToken(body) as JObject ??
			throw new InvalidDataException(
				"CoinSwitch spot symbol response contains no exchange map.");
		var value = FindProperty(data, exchange);
		return value?.ToObject<string[]>() ?? [];
	}

	internal static Dictionary<string, CoinSwitchSpotTradeInfo>
		DeserializeSpotTradeInfo(
			string body,
			string exchange)
	{
		var data = DeserializeToken(body) as JObject ??
			throw new InvalidDataException(
				"CoinSwitch spot trade-info response contains no data.");
		var exchangeData = FindProperty(data, exchange) as JObject;
		if (exchangeData is null &&
			data.Properties().Any(static property =>
				property.Name.Contains('/')))
			exchangeData = data;
		if (exchangeData is null)
			return new(StringComparer.OrdinalIgnoreCase);
		return exchangeData.Properties().ToDictionary(
			static property => property.Name,
			static property =>
				property.Value.ToObject<CoinSwitchSpotTradeInfo>(
					JsonSerializer.Create(CreateJsonSettings())),
			StringComparer.OrdinalIgnoreCase);
	}

	internal static CoinSwitchTicker DeserializeSpotTicker(
		string body,
		string symbol)
	{
		var data = DeserializeToken(body);
		if (data is JObject map)
		{
			var entry = FindProperty(map, symbol);
			if (entry is not null)
				data = entry;
		}
		return data.ToObject<CoinSwitchTicker>(
			JsonSerializer.Create(CreateJsonSettings())) ??
			throw new InvalidDataException(
				$"CoinSwitch returned no ticker for '{symbol}'.");
	}

	internal static CoinSwitchFuturesInstrument[]
		DeserializeFuturesInstruments(string body)
	{
		var data = DeserializeToken(body) as JObject ??
			throw new InvalidDataException(
				"CoinSwitch futures instrument response has no map.");
		var serializer = JsonSerializer.Create(CreateJsonSettings());
		return [.. data.Properties().Select(property =>
		{
			var instrument =
				property.Value.ToObject<CoinSwitchFuturesInstrument>(
					serializer) ??
				throw new InvalidDataException(
					$"CoinSwitch returned an empty '{property.Name}' instrument.");
			instrument.NativeSymbol =
				property.Name.Trim().ToUpperInvariant();
			return instrument;
		})];
	}

	internal static CoinSwitchFuturesTicker DeserializeFuturesTicker(
		string body,
		string exchange)
	{
		var data = DeserializeToken(body);
		if (data is JObject map)
		{
			var exchangeData = FindProperty(map, exchange);
			if (exchangeData is not null)
				data = exchangeData;
			if (data is JObject symbolMap &&
				symbolMap["symbol"] is null &&
				symbolMap.Properties().FirstOrDefault()?.Value is
					JObject first)
				data = first;
		}
		return data.ToObject<CoinSwitchFuturesTicker>(
			JsonSerializer.Create(CreateJsonSettings())) ??
			throw new InvalidDataException(
				$"CoinSwitch returned no ticker for '{exchange}'.");
	}

	internal static CoinSwitchHftInstrument[]
		DeserializeHftInstruments(string body)
		=> DeserializeHftList<CoinSwitchHftInstrument>(body);

	internal static int NormalizeDepth(int value)
		=> value.Max(1).Min(200);

	internal static int NormalizeTradeLimit(int value)
		=> value.Max(1).Min(1000);

	internal static int NormalizeCandleLimit(int value)
		=> value.Max(1).Min(1000);

	private async ValueTask<TData> SendAsync<TData>(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isHft,
		bool requireCredentials,
		bool isRetryable,
		CancellationToken cancellationToken)
		=> Deserialize<TData>(await SendRawAsync(
			method,
			path,
			query,
			body,
			isHft,
			requireCredentials,
			isRetryable,
			cancellationToken));

	private async ValueTask<TData> SendHftResultAsync<TData>(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isRetryable,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			method,
			path,
			query,
			body,
			true,
			true,
			isRetryable,
			cancellationToken);
		var envelope = DeserializeHftEnvelope(raw);
		if (envelope.Result is null ||
			envelope.Result.Type == JTokenType.Null)
			return default;
		return envelope.Result.ToObject<TData>(
			JsonSerializer.Create(CreateJsonSettings()));
	}

	private async ValueTask<string> SendRawAsync(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isHft,
		bool requireCredentials,
		bool isRetryable,
		CancellationToken cancellationToken)
	{
		if (requireCredentials && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"CoinSwitch API key and Ed25519 secret are required.");

		var target = CreateUri(
			isHft ? _hftEndpoint : _restEndpoint,
			path,
			query);
		var bodyText = body is null
			? null
			: JsonConvert.SerializeObject(
				body, CreateJsonSettings());
		var attempts = isRetryable ? _maximumReadAttempts : 1;
		Exception lastError = null;

		for (var attempt = 1; attempt <= attempts; attempt++)
		{
			try
			{
				await WaitRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(method, target);
				if (bodyText is not null)
					request.Content = new StringContent(
						bodyText,
						Encoding.UTF8,
						"application/json");
				if (IsCredentialsAvailable)
				AddAuthentication(request, method, target);
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				if (response.Content.Headers.ContentLength >
					_maximumPayloadLength)
					throw new InvalidDataException(
						"CoinSwitch response exceeds the size limit.");
				var responseBody =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (responseBody.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"CoinSwitch response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
					return responseBody;

				var error = CreateApiError(
					response.StatusCode,
					responseBody,
					response.ReasonPhrase);
				if (attempt >= attempts ||
					!IsTransient(response.StatusCode))
					throw error;
				lastError = error;
				await Task.Delay(
					response.Headers.RetryAfter?.Delta ??
						GetRetryDelay(attempt),
					cancellationToken);
			}
			catch (Exception error) when (
				attempt < attempts &&
				!cancellationToken.IsCancellationRequested &&
				error is HttpRequestException or TaskCanceledException)
			{
				lastError = error;
				await Task.Delay(
					GetRetryDelay(attempt), cancellationToken);
			}
		}

		throw lastError ?? new InvalidOperationException(
			"CoinSwitch API request failed.");
	}

	private void AddAuthentication(
		HttpRequestMessage request,
		HttpMethod method,
		Uri target)
	{
		var epoch = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		request.Headers.TryAddWithoutValidation(
			"X-AUTH-APIKEY", _key);
		request.Headers.TryAddWithoutValidation(
			"X-AUTH-EPOCH",
			epoch.ToString(CultureInfo.InvariantCulture));
		request.Headers.TryAddWithoutValidation(
			"X-AUTH-SIGNATURE",
			_signer.Sign(method.Method, target.PathAndQuery, epoch));
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

	private static JToken DeserializeToken(string body)
	{
		CoinSwitchEnvelope envelope;
		try
		{
			envelope = JsonConvert.DeserializeObject<CoinSwitchEnvelope>(
				body.ThrowIfEmpty(nameof(body)),
				CreateJsonSettings());
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinSwitch returned malformed JSON.", error);
		}
		if (envelope is null)
			throw new InvalidDataException(
				"CoinSwitch returned an empty response.");
		if (envelope.Error is not null &&
			envelope.Error.Type != JTokenType.Null)
			throw new CoinSwitchApiException(
				HttpStatusCode.OK,
				envelope.Message ??
					envelope.Error.ToString(Formatting.None));
		if (envelope.Data is null)
			throw new InvalidDataException(
				envelope.Message.IsEmpty()
					? "CoinSwitch response contains no data."
					: envelope.Message);
		return envelope.Data;
	}

	private static CoinSwitchHftEnvelope DeserializeHftEnvelope(
		string body)
	{
		CoinSwitchHftEnvelope envelope;
		try
		{
			envelope =
				JsonConvert.DeserializeObject<CoinSwitchHftEnvelope>(
					body.ThrowIfEmpty(nameof(body)),
					CreateJsonSettings());
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"CoinSwitch HFT returned malformed JSON.", error);
		}
		if (envelope is null)
			throw new InvalidDataException(
				"CoinSwitch HFT returned an empty response.");
		if (envelope.ReturnCode != 0)
			throw new CoinSwitchApiException(
				HttpStatusCode.OK,
				$"{envelope.ReturnCode}: {envelope.ReturnMessage}");
		if (envelope.Result is null)
			throw new InvalidDataException(
				"CoinSwitch HFT response contains no result.");
		return envelope;
	}

	private static TData[] DeserializeList<TData>(string body)
	{
		var data = DeserializeToken(body);
		var serializer = JsonSerializer.Create(CreateJsonSettings());
		if (data is JArray array)
			return array.ToObject<TData[]>(serializer) ?? [];
		var orders = data["orders"] ?? data["list"];
		if (orders is JArray list)
			return list.ToObject<TData[]>(serializer) ?? [];
		if (data.Type == JTokenType.Object)
		{
			var single = data.ToObject<TData>(serializer);
			return single is null ? [] : [single];
		}
		return [];
	}

	private static TData[] DeserializeHftList<TData>(string body)
	{
		var envelope = DeserializeHftEnvelope(body);
		var serializer = JsonSerializer.Create(CreateJsonSettings());
		if (envelope.Result is JArray array)
			return array.ToObject<TData[]>(serializer) ?? [];
		var result = envelope.Result.ToObject<CoinSwitchHftList<TData>>(
			serializer);
		return result?.Values ?? [];
	}

	private static CoinSwitchHftCandle[] DeserializeHftCandles(
		string body)
	{
		var envelope = DeserializeHftEnvelope(body);
		var list = envelope.Result["list"] as JArray ?? [];
		var result = new List<CoinSwitchHftCandle>(list.Count);
		foreach (var token in list.OfType<JArray>())
		{
			if (token.Count < 7)
				continue;
			result.Add(new()
			{
				OpenTime = token[0].Value<long>(),
				Open = token[1].Value<decimal>(),
				High = token[2].Value<decimal>(),
				Low = token[3].Value<decimal>(),
				Close = token[4].Value<decimal>(),
				Volume = token[5].Value<decimal>(),
				Turnover = token[6].Value<decimal>(),
			});
		}
		return [.. result];
	}

	private static JToken FindProperty(JObject value, string name)
		=> value.Properties().FirstOrDefault(property =>
			property.Name.EqualsIgnoreCase(name))?.Value;

	private static Uri CreateUri(
		Uri endpoint,
		string path,
		IEnumerable<(string Name, object Value)> query)
	{
		var target = new Uri(
			endpoint,
			path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
		var queryString = (query ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.OrderBy(static value => value.Name, StringComparer.Ordinal)
			.Select(static value =>
				Uri.EscapeDataString(value.Name) + "=" +
				Uri.EscapeDataString(Convert.ToString(
					value.Value,
					CultureInfo.InvariantCulture)))
			.Join("&");
		if (queryString.IsEmpty())
			return target;
		return new UriBuilder(target)
		{
			Query = queryString,
		}.Uri;
	}

	private static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			DateParseHandling = DateParseHandling.None,
			FloatParseHandling = FloatParseHandling.Decimal,
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None,
			Culture = CultureInfo.InvariantCulture,
		};

	private static CoinSwitchApiException CreateApiError(
		HttpStatusCode statusCode,
		string body,
		string reasonPhrase)
	{
		string details = null;
		try
		{
			var token = JToken.Parse(body);
			details = (string)(token["message"] ??
				token["retMsg"] ??
				token["error"]);
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

	private static TimeSpan GetRetryDelay(int attempt)
		=> TimeSpan.FromMilliseconds(
			Math.Min(5000, 250 * (1 << attempt)));

	private static int NormalizeOrderLimit(int value)
		=> value.Max(1).Min(500);

	private static string NormalizeSpotSymbol(string value)
		=> value.ToCoinSwitchNativeSymbol(CoinSwitchProductTypes.Spot);

	private static string NormalizeFuturesSymbol(string value)
		=> value.ToCoinSwitchNativeSymbol(CoinSwitchProductTypes.Futures);

	private static string NormalizeOptionSymbol(string value)
		=> value.ToCoinSwitchNativeSymbol(CoinSwitchProductTypes.Options);

	private static (string Name, object Value)[] Values(
		params (string Name, object Value)[] values)
		=> [.. (values ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)];

	private static Uri ValidateEndpoint(string value, string parameterName)
	{
		value = value.ThrowIfEmpty(parameterName).Trim();
		if (!value.EndsWith('/'))
			value += "/";
		if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("https"))
			throw new ArgumentException(
				"CoinSwitch REST endpoint must be an absolute HTTPS URI.",
				parameterName);
		return endpoint;
	}
}
