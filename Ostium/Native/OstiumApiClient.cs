namespace StockSharp.Ostium.Native;

sealed class OstiumApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private const int _pageSize = 1000;
	private const string _pairsQuery = """
		query GetPairsAndGroups {
		  pairs(orderBy: id, orderDirection: asc, subgraphError: allow) {
		    id from to maxLeverage overnightMaxLeverage takerFeeP
		    maxOI longOI shortOI
		    group { id name maxLeverage }
		    lastUpdateTimestamp buyVolume sellVolume decayRate netVolThreshold
		    priceImpactK accRolloverLong accRolloverShort lastRolloverBlock
		    lastRolloverLongPure brokerPremium isNegativeRolloverAllowed
		    lastTradePrice spreadP
		  }
		}
		""";
	private const string _openTradesQuery = """
		query GetTraderOpenTrades($trader: String!, $skip: Int!, $first: Int!) {
		  trades(
		    where: { isOpen: true, trader: $trader }
		    skip: $skip first: $first
		    orderBy: timestamp orderDirection: desc
		  ) {
		    id tradeID trader isOpen isBuy isDayTrade index tradeType
		    collateral notional tradeNotional leverage highestLeverage
		    openPrice stopLossPrice takeProfitPrice rollover timestamp
		    pair {
		      id from to maxLeverage overnightMaxLeverage takerFeeP
		      maxOI longOI shortOI group { id name maxLeverage }
		      lastUpdateTimestamp buyVolume sellVolume decayRate netVolThreshold
		      priceImpactK accRolloverLong accRolloverShort lastRolloverBlock
		      lastRolloverLongPure brokerPremium isNegativeRolloverAllowed
		      lastTradePrice spreadP
		    }
		  }
		}
		""";
	private const string _activeLimitsQuery = """
		query GetTraderActiveLimits($trader: String!, $skip: Int!, $first: Int!) {
		  limits(
		    where: { isActive: true, trader: $trader }
		    skip: $skip first: $first
		    orderBy: updatedAt orderDirection: desc
		  ) {
		    id uniqueId orderId trader pair { id from to group { id name } }
		    isBuy limitType isActive executionStarted
		    collateral notional tradeNotional leverage
		    openPrice takeProfitPrice stopLossPrice block initiatedAt updatedAt
		  }
		}
		""";
	private const string _ordersQuery = """
		query GetTraderOrders($trader: String!, $skip: Int!, $first: Int!) {
		  orders(
		    where: { trader: $trader }
		    skip: $skip first: $first
		    orderBy: initiatedAt orderDirection: desc
		  ) {
		    id tradeID limitID trader pair { id from to group { id name } }
		    orderAction orderType isBuy isPending isCancelled cancelReason
		    collateral notional tradeNotional leverage
		    price priceAfterImpact priceImpactP
		    vaultFee devFee oracleFee rolloverFee liquidationFee
		    builder builderFee profitPercent totalProfitPercent amountSentToTrader
		    closePercent initiatedTx initiatedBlock initiatedAt
		    executedTx executedBlock executedAt
		  }
		}
		""";

	private readonly Uri _pricesEndpoint;
	private readonly Uri _ohlcEndpoint;
	private readonly Uri _subgraphEndpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;

	public OstiumApiClient(string builderEndpoint, string subgraphEndpoint)
	{
		var builder = CreateEndpoint(builderEndpoint, nameof(builderEndpoint));
		_subgraphEndpoint = CreateEndpoint(subgraphEndpoint,
			nameof(subgraphEndpoint));
		_pricesEndpoint = new(builder, "/v1/prices");
		_ohlcEndpoint = new(builder, "/v1/ohlc");
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Ostium-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Ostium_HTTP";

	public ValueTask<OstiumPricesResponse> GetPricesAsync(
		CancellationToken cancellationToken)
		=> SendAsync<OstiumPricesResponse>(_pricesEndpoint, HttpMethod.Get, null,
			cancellationToken);

	public async ValueTask<OstiumOhlcResponse> GetCandlesAsync(string pair,
		DateTime from, DateTime to, TimeSpan timeFrame, int maximumCandles,
		CancellationToken cancellationToken)
	{
		from = from.EnsureOstiumUtc();
		to = to.EnsureOstiumUtc();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));
		if (maximumCandles <= 0)
			throw new ArgumentOutOfRangeException(nameof(maximumCandles));
		var pairName = pair.ThrowIfEmpty(nameof(pair));
		var resolution = timeFrame.ToOstiumResolution();
		var cursorMilliseconds = checked((long)from.ToUnix(false));
		var toMilliseconds = checked((long)to.ToUnix(false));
		var candles = new List<OstiumCandle>(maximumCandles.Min(1000));
		var times = new HashSet<long>();
		long? lastRequestTimestamp = null;
		for (var page = 0; page < 20 && candles.Count < maximumCandles; page++)
		{
			var response = await SendAsync<OstiumOhlcResponse>(_ohlcEndpoint,
				HttpMethod.Post, new OstiumOhlcRequest
			{
				Pair = pairName,
				FromTimestampSeconds = cursorMilliseconds / 1000,
				ToTimestampSeconds = checked((long)to.ToUnix()),
				Resolution = resolution,
			}, cancellationToken);
			lastRequestTimestamp = response.LastRequestTimestamp;
			var pageCandles = (response.Data ?? [])
				.Where(static candle => candle is not null && candle.Time > 0)
				.OrderBy(static candle => candle.Time)
				.ToArray();
			foreach (var candle in pageCandles)
				if (times.Add(candle.Time))
					candles.Add(candle);
			if (pageCandles.Length == 0)
				break;
			var next = pageCandles[^1].Time;
			if (next <= cursorMilliseconds || next >= toMilliseconds)
				break;
			cursorMilliseconds = next;
		}
		return new()
		{
			Data = [.. candles.OrderBy(static candle => candle.Time)
				.Take(maximumCandles)],
			LastRequestTimestamp = lastRequestTimestamp,
		};
	}

	public async ValueTask<OstiumGraphPair[]> GetPairsAsync(
		CancellationToken cancellationToken)
	{
		var data = await QueryAsync<OstiumEmptyVariables, OstiumPairsData>(
			_pairsQuery, new(), cancellationToken);
		return data.Pairs ?? [];
	}

	public ValueTask<OstiumGraphTrade[]> GetOpenTradesAsync(string trader,
		int limit, CancellationToken cancellationToken)
		=> ReadPagesAsync<OstiumGraphTrade, OstiumTradesData>(trader, limit,
			_openTradesQuery, static data => data.Trades, cancellationToken);

	public ValueTask<OstiumGraphLimit[]> GetActiveLimitsAsync(string trader,
		int limit, CancellationToken cancellationToken)
		=> ReadPagesAsync<OstiumGraphLimit, OstiumLimitsData>(trader, limit,
			_activeLimitsQuery, static data => data.Limits, cancellationToken);

	public ValueTask<OstiumGraphOrder[]> GetOrdersAsync(string trader,
		int limit, CancellationToken cancellationToken)
		=> ReadPagesAsync<OstiumGraphOrder, OstiumOrdersData>(trader, limit,
			_ordersQuery, static data => data.Orders, cancellationToken);

	private async ValueTask<TItem[]> ReadPagesAsync<TItem, TData>(string trader,
		int limit, string query, Func<TData, TItem[]> selector,
		CancellationToken cancellationToken)
	{
		trader = trader.NormalizeAddress();
		if (limit <= 0)
			return [];
		var result = new List<TItem>(limit.Min(_pageSize));
		while (result.Count < limit)
		{
			var first = (limit - result.Count).Min(_pageSize);
			var data = await QueryAsync<OstiumTraderPageVariables, TData>(query,
				new()
				{
					Trader = trader,
					Skip = result.Count,
					First = first,
				}, cancellationToken);
			var page = selector(data) ?? [];
			result.AddRange(page.Where(static item => item is not null));
			if (page.Length < first)
				break;
		}
		return [.. result.Take(limit)];
	}

	private async ValueTask<TData> QueryAsync<TVariables, TData>(string query,
		TVariables variables, CancellationToken cancellationToken)
	{
		var response = await SendAsync<OstiumGraphQlResponse<TData>>(
			_subgraphEndpoint, HttpMethod.Post,
			new OstiumGraphQlRequest<TVariables>
			{
				Query = query,
				Variables = variables,
			}, cancellationToken);
		if (response.Errors?.Length > 0)
			throw new InvalidOperationException(
				"Ostium GraphQL: " + string.Join("; ", response.Errors
					.Where(static error => error is not null)
					.Select(static error => error.Message)
					.Where(static message => !message.IsEmpty())));
		return response.Data is not null
			? response.Data
			: throw new InvalidDataException(
				"Ostium GraphQL returned no data.");
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(Uri endpoint,
		HttpMethod method, object payload, CancellationToken cancellationToken)
	{
		var content = payload is null
			? null
			: JsonConvert.SerializeObject(payload, _settings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(method, endpoint);
			if (content is not null)
				request.Content = new StringContent(content, Encoding.UTF8,
					"application/json");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 3 && (response.StatusCode == (HttpStatusCode)429 ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
			{
				var error = TryDeserialize<OstiumErrorResponse>(body)?.Error;
				throw new InvalidOperationException("Ostium HTTP " +
					(int)response.StatusCode + ": " +
					(error.IsEmpty() ? Truncate(body) : error));
			}
			return Deserialize<TResponse>(body);
		}
	}

	private async ValueTask WaitForRequestAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequest - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(20);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private T Deserialize<T>(string body)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(body, _settings) ??
				throw new InvalidDataException(
					"Ostium returned an empty JSON response.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Ostium returned an unexpected response shape.", error);
		}
	}

	private T TryDeserialize<T>(string body)
		where T : class
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(body, _settings);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static Uri CreateEndpoint(string endpoint, string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Ostium endpoint must use HTTP or HTTPS.", parameterName);
		return uri;
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Ostium response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var block = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(block, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Ostium response exceeds the 16 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}
}
