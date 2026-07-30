namespace StockSharp.DeepBook.Native;

sealed class DeepBookApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 8 * 1024 * 1024;
	private const int _maximumHistory = 500;

	private readonly HttpClient _httpClient;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public DeepBookApiClient(string endpoint)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_httpClient = new()
		{
			BaseAddress = new Uri(endpoint + "/", UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-DeepBook/1.0");
	}

	public override string Name => "DeepBook_Indexer";

	public async ValueTask<DeepBookStatusData> GetStatusAsync(
		CancellationToken cancellationToken)
	{
		var status = await GetAsync<DeepBookStatusData>("status",
			cancellationToken) ?? throw new InvalidDataException(
				"DeepBook indexer returned no status.");
		if (!status.Status.EqualsIgnoreCase("OK") ||
			status.LatestCheckpoint == 0 ||
			status.CurrentTimeMilliseconds <= 0)
			throw new InvalidDataException(
				"DeepBook indexer returned an unhealthy status.");
		return status;
	}

	public async ValueTask<DeepBookMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
	{
		var data = await GetAsync<DeepBookPoolData[]>("get_pools",
			cancellationToken) ?? [];
		var result = new List<DeepBookMarket>(data.Length);
		var pools = new HashSet<string>(StringComparer.Ordinal);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var item in data)
		{
			if (item is null)
				continue;
			var poolId = item.PoolId.NormalizeSuiAddress();
			var poolName = item.PoolName.NormalizePoolName();
			if (!pools.Add(poolId) || !names.Add(poolName))
				throw new InvalidDataException(
					$"DeepBook indexer returned duplicate pool '{poolName}'.");
			if (item.BaseAssetDecimals is < 0 or > 28 ||
				item.QuoteAssetDecimals is < 0 or > 28 ||
				item.MinSize == 0 || item.LotSize == 0 ||
				item.TickSize == 0)
				throw new InvalidDataException(
					$"DeepBook pool '{poolName}' has invalid precision metadata.");
			var baseToken = new DeepBookToken
			{
				CoinType = item.BaseAssetId.NormalizeCoinType(),
				Symbol = item.BaseAssetSymbol.NormalizeTokenSymbol(
					item.BaseAssetId),
				Name = item.BaseAssetName.NormalizeTokenName(
					item.BaseAssetSymbol),
				Decimals = item.BaseAssetDecimals,
			};
			var quoteToken = new DeepBookToken
			{
				CoinType = item.QuoteAssetId.NormalizeCoinType(),
				Symbol = item.QuoteAssetSymbol.NormalizeTokenSymbol(
					item.QuoteAssetId),
				Name = item.QuoteAssetName.NormalizeTokenName(
					item.QuoteAssetSymbol),
				Decimals = item.QuoteAssetDecimals,
			};
			result.Add(new()
			{
				PoolId = poolId,
				PoolName = poolName,
				BaseToken = baseToken,
				QuoteToken = quoteToken,
				SecurityCode = poolName.Replace('_', '-')
					.NormalizeSecurityCode(),
				MinSize = item.MinSize.FromBaseUnits(
					item.BaseAssetDecimals),
				LotSize = item.LotSize.FromBaseUnits(
					item.BaseAssetDecimals),
				TickSize = item.TickSize.FromBaseUnits(
					item.QuoteAssetDecimals),
			});
		}

		if (result.Count == 0)
			throw new InvalidDataException(
				"DeepBook indexer returned no pools.");
		return [.. result];
	}

	public async ValueTask<DeepBookOrderBook> GetOrderBookAsync(
		DeepBookMarket market, int depth,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (depth is < 2 or > 500 || depth % 2 != 0)
			throw new ArgumentOutOfRangeException(nameof(depth), depth,
				"DeepBook order-book depth must be an even number from 2 to 500.");
		var data = await GetAsync<DeepBookOrderBookData>(
			"orderbook/" + Uri.EscapeDataString(market.PoolName) +
			"?level=2&depth=" + depth.ToString(CultureInfo.InvariantCulture),
			cancellationToken) ?? throw new InvalidDataException(
				"DeepBook indexer returned no order book.");
		if (!long.TryParse(data.Timestamp, NumberStyles.Integer,
			CultureInfo.InvariantCulture, out var timestamp) || timestamp <= 0)
			throw new InvalidDataException(
				"DeepBook indexer returned an invalid order-book timestamp.");
		var bids = ParseLevels(data.Bids, true);
		var asks = ParseLevels(data.Asks, false);
		if (bids.Length == 0 || asks.Length == 0 ||
			bids[0].Price >= asks[0].Price)
			throw new InvalidDataException(
				"DeepBook indexer returned an invalid or crossed order book.");
		return new()
		{
			Time = FromUnixMilliseconds(timestamp),
			Bids = bids,
			Asks = asks,
		};
	}

	public async ValueTask<DeepBookTrade[]> GetTradesAsync(
		DeepBookMarket market, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		limit = limit.Max(1).Min(_maximumHistory);
		var request = "trades/" + Uri.EscapeDataString(market.PoolName) +
			"?limit=" + limit.ToString(CultureInfo.InvariantCulture);
		if (from is DateTime start)
			request += "&start_time=" + new DateTimeOffset(
				start.ToUniversalTime()).ToUnixTimeSeconds().ToString(
					CultureInfo.InvariantCulture);
		if (to is DateTime end)
			request += "&end_time=" + new DateTimeOffset(
				end.ToUniversalTime()).ToUnixTimeSeconds().ToString(
					CultureInfo.InvariantCulture);
		var data = await GetAsync<DeepBookTradeData[]>(request,
			cancellationToken) ?? [];
		var result = new List<DeepBookTrade>(data.Length);

		foreach (var item in data)
		{
			if (item is null || item.Timestamp <= 0 ||
				item.Price <= 0 || item.BaseVolume <= 0 ||
				item.QuoteVolume <= 0)
				throw new InvalidDataException(
					"DeepBook indexer returned a malformed trade.");
			var id = item.EventDigest.IsEmpty()
				? item.TradeId.IsEmpty()
					? item.Digest
					: item.TradeId
				: item.EventDigest;
			if (id.IsEmpty())
				throw new InvalidDataException(
					"DeepBook indexer returned a trade without an identifier.");
			result.Add(new()
			{
				Id = id,
				Time = FromUnixMilliseconds(item.Timestamp),
				Price = item.Price,
				BaseVolume = item.BaseVolume,
				QuoteVolume = item.QuoteVolume,
				Side = item.Type.EqualsIgnoreCase("buy") ||
					item.TakerIsBid ? Sides.Buy : Sides.Sell,
			});
		}

		return [.. result.OrderBy(static item => item.Time)
			.ThenBy(static item => item.Id, StringComparer.Ordinal)];
	}

	public async ValueTask<DeepBookCandle[]> GetCandlesAsync(
		DeepBookMarket market, TimeSpan timeFrame, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		var interval = timeFrame.ToDeepBookInterval();
		limit = limit.Max(1).Min(_maximumHistory);
		var request = "ohclv/" + Uri.EscapeDataString(market.PoolName) +
			"?interval=" + interval + "&limit=" +
			limit.ToString(CultureInfo.InvariantCulture);
		if (from is DateTime start)
			request += "&start_time=" + new DateTimeOffset(
				start.ToUniversalTime()).ToUnixTimeMilliseconds().ToString(
					CultureInfo.InvariantCulture);
		if (to is DateTime end)
			request += "&end_time=" + new DateTimeOffset(
				end.ToUniversalTime()).ToUnixTimeMilliseconds().ToString(
					CultureInfo.InvariantCulture);
		var data = await GetAsync<DeepBookCandleData>(request,
			cancellationToken);
		var result = new List<DeepBookCandle>();

		foreach (var token in data?.Candles ?? [])
		{
			if (token is not JArray { Count: >= 6 } row)
				throw new InvalidDataException(
					"DeepBook indexer returned a malformed candle.");
			var timestamp = ReadLong(row[0], "candle timestamp");
			var open = ReadDecimal(row[1], "candle open");
			var high = ReadDecimal(row[2], "candle high");
			var low = ReadDecimal(row[3], "candle low");
			var close = ReadDecimal(row[4], "candle close");
			var volume = ReadDecimal(row[5], "candle volume");
			if (timestamp <= 0 || open <= 0 || high <= 0 || low <= 0 ||
				close <= 0 || volume < 0 || high < open || high < close ||
				low > open || low > close)
				throw new InvalidDataException(
					"DeepBook indexer returned invalid candle values.");
			result.Add(new()
			{
				OpenTime = FromUnixMilliseconds(timestamp),
				Open = open,
				High = high,
				Low = low,
				Close = close,
				Volume = volume,
			});
		}

		return [.. result.OrderBy(static item => item.OpenTime)];
	}

	public async ValueTask<DeepBookQuote> GetQuoteAsync(
		DeepBookMarket market, Sides side, decimal volume, int depth,
		CancellationToken cancellationToken)
	{
		if (volume <= 0)
			throw new ArgumentOutOfRangeException(nameof(volume));
		var book = await GetOrderBookAsync(market, depth, cancellationToken);
		var levels = side == Sides.Sell ? book.Bids : book.Asks;
		var remaining = volume;
		var quoteAmount = 0m;

		foreach (var level in levels)
		{
			var filled = remaining.Min(level.Volume);
			quoteAmount += filled * level.Price;
			remaining -= filled;
			if (remaining <= 0)
				break;
		}

		if (remaining > 0)
			throw new InvalidOperationException(
				$"DeepBook order book has insufficient liquidity for {volume} " +
				$"{market.BaseToken.Symbol}.");
		var baseUnits = volume.ToBaseUnits(market.BaseToken.Decimals);
		var quoteUnits = quoteAmount.ToBaseUnitsRoundedUp(
			market.QuoteToken.Decimals);
		return new()
		{
			Side = side,
			InputAmount = side == Sides.Sell ? baseUnits : quoteUnits,
			OutputAmount = side == Sides.Sell ? quoteUnits : baseUnits,
			Price = quoteAmount / volume,
			Volume = volume,
		};
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_httpClient.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask<T> GetAsync<T>(string request,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		for (var attempt = 0; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			using var response = await _httpClient.GetAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
					HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new DeepBookApiException(response.StatusCode,
					$"DeepBook indexer request failed: {Limit(body, 1024)}");
			try
			{
				return JsonConvert.DeserializeObject<T>(body,
					_serializerSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"DeepBook indexer returned malformed JSON.", error);
			}
		}
	}

	private async ValueTask WaitForRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static DeepBookBookLevel[] ParseLevels(string[][] source,
		bool descending)
	{
		var result = new List<DeepBookBookLevel>();

		foreach (var item in source ?? [])
		{
			if (item is not { Length: >= 2 } ||
				!decimal.TryParse(item[0], NumberStyles.Float,
					CultureInfo.InvariantCulture, out var price) ||
				!decimal.TryParse(item[1], NumberStyles.Float,
					CultureInfo.InvariantCulture, out var volume) ||
				price <= 0 || volume <= 0)
				throw new InvalidDataException(
					"DeepBook indexer returned a malformed order-book level.");
			result.Add(new() { Price = price, Volume = volume });
		}

		return descending
			? [.. result.OrderByDescending(static item => item.Price)]
			: [.. result.OrderBy(static item => item.Price)];
	}

	private static long ReadLong(JToken token, string field)
		=> long.TryParse(token?.ToString(Formatting.None).Trim('"'),
			NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
			? value
			: throw new InvalidDataException(
				$"DeepBook indexer returned an invalid {field}.");

	private static decimal ReadDecimal(JToken token, string field)
		=> decimal.TryParse(token?.ToString(Formatting.None).Trim('"'),
			NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
			? value
			: throw new InvalidDataException(
				$"DeepBook indexer returned an invalid {field}.");

	private static DateTime FromUnixMilliseconds(long value)
	{
		try
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(value).UtcDateTime;
		}
		catch (ArgumentOutOfRangeException error)
		{
			throw new InvalidDataException(
				$"DeepBook indexer returned invalid timestamp '{value}'.",
				error);
		}
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"DeepBook indexer endpoint must use HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"DeepBook indexer response exceeds the safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];

		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseLength)
				throw new InvalidDataException(
					"DeepBook indexer response exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}

		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
