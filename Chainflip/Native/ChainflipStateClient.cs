namespace StockSharp.Chainflip.Native;

sealed class ChainflipStateClient : BaseLogReceiver
{
	private sealed class TradeAccumulator
	{
		public ChainflipMarket Market { get; init; }
		public Sides Side { get; init; }
		public BigInteger BaseUnits { get; set; }
		public BigInteger QuoteOnlyUnits { get; set; }
	}

	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private long _requestId;
	private DateTime _nextRequest;

	public ChainflipStateClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Chainflip State Chain endpoint must be an absolute HTTP or " +
					"HTTPS URI.", nameof(endpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Chainflip-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Chainflip_State_RPC";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<ChainflipMarket[]> VerifyAndGetMarketsAsync(
		CancellationToken cancellationToken)
	{
		var chain = await SendAsync<string>("system_chain", new JArray(),
			cancellationToken);
		if (chain.IsEmpty() ||
			!chain.Contains("Chainflip", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				$"State Chain RPC reports unexpected chain '{chain}'.");
		var supported = await SendAsync<ChainflipRpcAsset[]>(
			"cf_supported_assets", new JArray(), cancellationToken);
		if (supported is not { Length: > 0 })
			throw new InvalidDataException(
				"Chainflip State Chain returned no supported assets.");
		foreach (var asset in supported)
			_ = asset.ResolveAsset();
		var pools = await SendAsync<ChainflipRpcPool[]>(
			"cf_available_pools", new JArray(), cancellationToken);
		if (pools is not { Length: > 0 })
			throw new InvalidDataException(
				"Chainflip State Chain returned no available pools.");
		var markets = pools.Select(static pool =>
		{
			if (pool?.Base is null || pool.Quote is null)
				throw new InvalidDataException(
					"Chainflip returned an incomplete pool.");
			var baseAsset = pool.Base.ResolveAsset();
			var quoteAsset = pool.Quote.ResolveAsset();
			if (baseAsset.Key.EqualsIgnoreCase(quoteAsset.Key))
				throw new InvalidDataException(
					"Chainflip returned a pool with identical assets.");
			return new ChainflipMarket
			{
				BaseAsset = baseAsset,
				QuoteAsset = quoteAsset,
				SecurityCode = baseAsset.ToSecurityCode(quoteAsset),
			};
		}).ToArray();
		if (markets.GroupBy(static market => market.SecurityCode,
			StringComparer.OrdinalIgnoreCase).Any(static group =>
				group.Count() != 1))
			throw new InvalidDataException(
				"Chainflip returned duplicate security identifiers.");
		return markets;
	}

	public async ValueTask<long> GetBestBlockNumberAsync(
		CancellationToken cancellationToken)
	{
		var header = await SendAsync<ChainflipHeader>("chain_getHeader",
			new JArray(), cancellationToken);
		if (header?.Number.IsEmpty() != false)
			throw new InvalidDataException(
				"Chainflip State Chain returned no best block.");
		var number = header.Number.ParseInteger();
		if (number < 0 || number > long.MaxValue)
			throw new InvalidDataException(
				"Chainflip best block is outside the supported range.");
		return (long)number;
	}

	public async ValueTask<BigInteger> GetMinimumDepositAmountAsync(
		ChainflipAsset asset, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(asset);
		var environment = await SendAsync<JObject>("cf_environment",
			new JArray(), cancellationToken);
		var value = environment?["ingress_egress"]?
			["minimum_deposit_amounts"]?[asset.Chain]?[asset.Symbol]?
			.Value<string>();
		var amount = value.ParseInteger();
		if (amount <= 0)
			throw new InvalidDataException(
				$"Chainflip returned no positive minimum deposit for " +
					$"'{asset.Key}'.");
		return amount;
	}

	public async ValueTask<string> GetBlockHashAsync(long blockNumber,
		CancellationToken cancellationToken)
	{
		if (blockNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(blockNumber));
		var hash = await SendAsync<string>("chain_getBlockHash",
			new JArray(blockNumber), cancellationToken);
		if (hash.IsEmpty() || !hash.StartsWith("0x",
			StringComparison.OrdinalIgnoreCase) || hash.Length != 66)
			throw new InvalidDataException(
				$"Chainflip State Chain returned no hash for block " +
					$"{blockNumber}.");
		return hash.ToLowerInvariant();
	}

	public async ValueTask<(decimal Bid, decimal Ask)> GetPricesAsync(
		ChainflipMarket market, string blockHash,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		var parameters = CreatePoolParameters(market, blockHash);
		var price = await SendAsync<ChainflipPoolPrice>(
			"cf_pool_price_v2", parameters, cancellationToken);
		if (price is null || price.Sell.IsEmpty() || price.Buy.IsEmpty())
			throw new InvalidDataException(
				$"Chainflip pool '{market.SecurityCode}' has no two-sided " +
					"price.");
		var bid = ChainflipExtensions.DecodeSqrtPrice(price.Sell,
			market.BaseAsset.Decimals, market.QuoteAsset.Decimals);
		var ask = ChainflipExtensions.DecodeSqrtPrice(price.Buy,
			market.BaseAsset.Decimals, market.QuoteAsset.Decimals);
		if (bid <= 0 || ask <= 0 || bid > ask)
			throw new InvalidDataException(
				$"Chainflip pool '{market.SecurityCode}' returned an invalid " +
					"spread.");
		return (bid, ask);
	}

	public async ValueTask<ChainflipOrderBook> GetOrderBookAsync(
		ChainflipMarket market, int depth,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (depth is < 1 or > 16384)
			throw new ArgumentOutOfRangeException(nameof(depth));
		var parameters = CreatePoolParameters(market, null);
		parameters["orders"] = depth;
		var response = await SendAsync<ChainflipRpcOrderBook>(
			"cf_pool_orderbook", parameters, cancellationToken);
		var bids = ConvertLevels(response?.Bids, market);
		var asks = ConvertLevels(response?.Asks, market);
		if (bids.Length == 0 || asks.Length == 0)
			throw new InvalidDataException(
				$"Chainflip pool '{market.SecurityCode}' has no two-sided " +
					"order book.");
		return new()
		{
			Time = DateTime.UtcNow,
			Bids = bids,
			Asks = asks,
		};
	}

	public async ValueTask<ChainflipBlockTrades> GetBlockTradesAsync(
		long blockNumber, IReadOnlyDictionary<string, ChainflipMarket> markets,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(markets);
		var hash = await GetBlockHashAsync(blockNumber, cancellationToken);
		var block = await SendAsync<ChainflipFillBlock>(
			"cf_lp_get_order_fills", new JArray(hash), cancellationToken);
		if (block is null || block.BlockNumber != blockNumber ||
			!block.BlockHash.EqualsIgnoreCase(hash))
			throw new InvalidDataException(
				$"Chainflip returned fills for a different block than " +
					$"{blockNumber}.");
		var accumulators = ParseFills(block.Fills ?? [], markets);
		var trades = new List<ChainflipTrade>();

		foreach (var accumulator in accumulators)
		{
			var prices = await GetPricesAsync(accumulator.Market, hash,
				cancellationToken);
			var price = accumulator.Side == Sides.Sell
				? prices.Bid
				: prices.Ask;
			var volume = accumulator.BaseUnits.FromBaseUnits(
				accumulator.Market.BaseAsset.Decimals);
			if (accumulator.QuoteOnlyUnits > 0)
				volume += accumulator.QuoteOnlyUnits.FromBaseUnits(
					accumulator.Market.QuoteAsset.Decimals) / price;
			if (price <= 0 || volume <= 0)
				continue;
			trades.Add(new()
			{
				Id = $"{blockNumber}:{accumulator.Market.SecurityCode}:" +
					accumulator.Side,
				Market = accumulator.Market,
				Time = block.Timestamp.ToUtcTime(),
				Side = accumulator.Side,
				Price = price,
				Volume = volume,
			});
		}

		return new()
		{
			BlockNumber = block.BlockNumber,
			BlockHash = block.BlockHash,
			Time = block.Timestamp.ToUtcTime(),
			Trades = [.. trades],
		};
	}

	private static ChainflipLevel[] ConvertLevels(
		IEnumerable<ChainflipRpcLevel> levels, ChainflipMarket market)
	{
		var result = new List<ChainflipLevel>();

		foreach (var level in levels ?? [])
		{
			if (level?.Amount.IsEmpty() != false ||
				level.SqrtPrice.IsEmpty())
				continue;
			try
			{
				var volume = level.Amount.ParseInteger().FromBaseUnits(
					market.BaseAsset.Decimals);
				var price = ChainflipExtensions.DecodeSqrtPrice(
					level.SqrtPrice, market.BaseAsset.Decimals,
					market.QuoteAsset.Decimals);
				if (volume > 0 && price > 0)
					result.Add(new()
					{
						Price = price,
						Volume = volume,
					});
			}
			catch (OverflowException)
			{
			}
		}

		return [.. result];
	}

	private static TradeAccumulator[] ParseFills(IEnumerable<JObject> fills,
		IReadOnlyDictionary<string, ChainflipMarket> markets)
	{
		var result = new Dictionary<string, TradeAccumulator>(
			StringComparer.OrdinalIgnoreCase);

		foreach (var wrapper in fills)
		{
			if (wrapper?["limit_order"] is JObject limit)
			{
				var market = ResolveMarket(limit, markets);
				if (market is null)
					continue;
				var lpSide = limit.Value<string>("side");
				var side = lpSide?.ToUpperInvariant() switch
				{
					"SELL" => Sides.Buy,
					"BUY" => Sides.Sell,
					_ => throw new InvalidDataException(
						"Chainflip limit fill has an invalid side."),
				};
				var sold = limit.Value<string>("sold").ParseInteger();
				var bought = limit.Value<string>("bought").ParseInteger();
				if (sold < 0 || bought < 0)
					throw new InvalidDataException(
						"Chainflip limit fill has a negative amount.");
				var accumulator = GetAccumulator(result, market, side);
				accumulator.BaseUnits += side == Sides.Buy ? sold : bought;
			}
			else if (wrapper?["range_order"] is JObject range)
			{
				var market = ResolveMarket(range, markets);
				if (market is null)
					continue;
				var amounts = range["bought_amounts"] as JObject ??
					throw new InvalidDataException(
						"Chainflip range fill has no bought amounts.");
				var baseUnits = amounts.Value<string>("base").ParseInteger();
				var quoteUnits = amounts.Value<string>("quote").ParseInteger();
				if (baseUnits < 0 || quoteUnits < 0 ||
					(baseUnits == 0) == (quoteUnits == 0))
					throw new InvalidDataException(
						"Chainflip range fill has invalid bought amounts.");
				if (baseUnits > 0)
					GetAccumulator(result, market, Sides.Sell).BaseUnits +=
						baseUnits;
				else
					GetAccumulator(result, market, Sides.Buy).QuoteOnlyUnits +=
						quoteUnits;
			}
		}
		return [.. result.Values];
	}

	private static ChainflipMarket ResolveMarket(JObject fill,
		IReadOnlyDictionary<string, ChainflipMarket> markets)
	{
		var baseAsset = fill["base_asset"]?.ToObject<ChainflipRpcAsset>() ??
			throw new InvalidDataException(
				"Chainflip fill has no base asset.");
		var quoteAsset = fill["quote_asset"]?.ToObject<ChainflipRpcAsset>() ??
			throw new InvalidDataException(
				"Chainflip fill has no quote asset.");
		var key = $"{baseAsset.Chain}:{baseAsset.Symbol}/" +
			$"{quoteAsset.Chain}:{quoteAsset.Symbol}";
		return markets.TryGetValue(key, out var market) ? market : null;
	}

	private static TradeAccumulator GetAccumulator(
		IDictionary<string, TradeAccumulator> values,
		ChainflipMarket market, Sides side)
	{
		var key = $"{market.Key}:{side}";
		if (!values.TryGetValue(key, out var value))
			values.Add(key, value = new()
			{
				Market = market,
				Side = side,
			});
		return value;
	}

	private static JObject CreatePoolParameters(ChainflipMarket market,
		string blockHash)
	{
		var result = new JObject
		{
			["base_asset"] = JObject.FromObject(market.BaseAsset.ToRpc()),
			["quote_asset"] = JObject.FromObject(market.QuoteAsset.ToRpc()),
		};
		if (!blockHash.IsEmpty())
			result["at"] = blockHash;
		return result;
	}

	private async ValueTask<TResult> SendAsync<TResult>(string method,
		JToken parameters, CancellationToken cancellationToken)
	{
		var requestId = Interlocked.Increment(ref _requestId);
		var payload = JsonConvert.SerializeObject(new ChainflipStateRequest
		{
			Id = requestId,
			Method = method.ThrowIfEmpty(nameof(method)),
			Parameters = parameters ?? new JArray(),
		}, _jsonSettings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Post,
				_endpoint)
			{
				Content = new StringContent(payload, Encoding.UTF8,
					"application/json"),
			};
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (attempt < 3 && (response.StatusCode ==
					(HttpStatusCode)429 || (int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(
					250 * (1 << attempt)), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"Chainflip State RPC HTTP {(int)response.StatusCode}: " +
						Truncate(body));
			ChainflipStateResponse<TResult> rpc;
			try
			{
				rpc = JsonConvert.DeserializeObject<
					ChainflipStateResponse<TResult>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Chainflip State RPC returned an unexpected response.",
					error);
			}
			if (rpc is null || rpc.Id != requestId)
				throw new InvalidDataException(
					"Chainflip State RPC returned an invalid identifier.");
			if (rpc.Error is not null)
				throw new InvalidOperationException(
					$"Chainflip State RPC {rpc.Error.Code}: " +
						(rpc.Error.Message ?? "request rejected"));
			return rpc.Result;
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Chainflip State RPC response exceeds the 32 MiB limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Chainflip State RPC response exceeds the 32 MiB limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
	}
}
