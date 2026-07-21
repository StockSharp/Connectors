namespace StockSharp.Balancer.Native;

sealed class BalancerApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private const string _poolFields = """
		id address name symbol type version protocolVersion
		poolTokens { index address name symbol decimals balance hasNestedPool isAllowed }
		dynamicData {
			totalLiquidity volume24h fees24h swapFee swapEnabled
			isPaused isInRecoveryMode
		}
		""";
	private static readonly string _poolsQuery = """
		query Pools($first: Int!, $skip: Int!, $where: GqlPoolFilter) {
			poolGetPools(
				first: $first
				skip: $skip
				orderBy: volume24h
				orderDirection: desc
				where: $where
			) {
				POOL_FIELDS
			}
		}
		""".Replace("POOL_FIELDS", _poolFields);
	private static readonly string _poolQuery = """
		query Pool($id: String!, $chain: GqlChain!) {
			poolGetPool(id: $id, chain: $chain) {
				POOL_FIELDS
			}
		}
		""".Replace("POOL_FIELDS", _poolFields);
	private const string _eventsQuery = """
		query Events($first: Int!, $skip: Int!, $where: GqlPoolEventsFilter!) {
			poolEvents(first: $first, skip: $skip, where: $where) {
				... on GqlPoolSwapEventV3 {
					id tx logIndex blockNumber timestamp poolId
					tokenIn { address amount valueUSD }
					tokenOut { address amount valueUSD }
					fee { address amount valueUSD }
				}
			}
		}
		""";
	private const string _quoteQueryTemplate = """
		query Quote(
			$chain: GqlChain!
			$tokenIn: String!
			$tokenOut: String!
			$swapType: GqlSorSwapType!
			$swapAmount: AmountHumanReadable!
			$poolIds: [String!]
		) {
			sorGetSwapPaths(
				chain: $chain
				tokenIn: $tokenIn
				tokenOut: $tokenOut
				swapType: $swapType
				swapAmount: $swapAmount
				useProtocolVersion: PROTOCOL_VERSION
				poolIds: $poolIds
			) {
				protocolVersion tokenIn tokenOut tokenInAmount tokenOutAmount
				swapAmountRaw returnAmountRaw
				paths {
					protocolVersion pools isBuffer inputAmountRaw outputAmountRaw
					tokens { address decimals }
				}
			}
		}
		""";
	private static readonly string _quoteV2Query =
		_quoteQueryTemplate.Replace("PROTOCOL_VERSION", "2");
	private static readonly string _quoteV3Query =
		_quoteQueryTemplate.Replace("PROTOCOL_VERSION", "3");

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
	private DateTime _nextRequest;

	public BalancerApiClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Balancer API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Balancer-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Balancer_GraphQL";

	public async ValueTask<BalancerPool[]> GetPoolsAsync(
		BalancerDeployment deployment, int maximum, decimal minimumTvl,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(deployment);
		if (maximum is < 0 or > 50)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		if (minimumTvl < 0)
			throw new ArgumentOutOfRangeException(nameof(minimumTvl));
		if (maximum == 0)
			return [];

		var response = await SendAsync<BalancerPoolsVariables,
			BalancerPoolsData>(_poolsQuery, new()
			{
				First = Math.Min(100, Math.Max(maximum, maximum * 3)),
				Skip = 0,
				Where = new()
				{
					Chains = [deployment.GraphChain],
					ProtocolVersions = deployment.V3Vault.IsEmpty()
						? [2]
						: [2, 3],
					MinimumTotalValueLocked = minimumTvl,
				},
			}, cancellationToken);
		return ConvertPools(response.Pools, maximum);
	}

	public async ValueTask<BalancerPool> GetPoolAsync(
		BalancerDeployment deployment, string poolId,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(deployment);
		poolId = poolId.ThrowIfEmpty(nameof(poolId)).Trim().ToLowerInvariant();
		var response = await SendAsync<BalancerPoolVariables,
			BalancerPoolData>(_poolQuery, new()
			{
				Id = poolId,
				Chain = deployment.GraphChain,
			}, cancellationToken);
		return response.Pool?.ToBalancer() ?? throw new InvalidDataException(
			$"Balancer API has no {deployment.Name} pool '{poolId}'.");
	}

	public async ValueTask<BalancerTrade[]> GetTradesAsync(
		BalancerDeployment deployment, BalancerMarket market, DateTime from,
		DateTime to, int maximum, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(deployment);
		ArgumentNullException.ThrowIfNull(market);
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		if (to < from)
			throw new ArgumentOutOfRangeException(nameof(to));
		if (maximum is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(maximum));

		var result = new List<BalancerTrade>(maximum);
		for (var skip = 0; skip < 1000 && result.Count < maximum; skip += 100)
		{
			var response = await SendAsync<BalancerEventsVariables,
				BalancerEventsData>(_eventsQuery, new()
				{
					First = Math.Min(100, maximum - result.Count),
					Skip = skip,
					Where = new()
					{
						Chains = [deployment.GraphChain],
						PoolId = market.Pool.Id,
					},
				}, cancellationToken);
			var events = response.Events ?? [];
			if (events.Length == 0)
				break;
			var oldest = DateTime.MaxValue;
			foreach (var item in events)
			{
				if (!TryConvertTrade(item, market, out var trade))
					continue;
				oldest = oldest.Min(trade.Time);
				if (trade.Time >= from && trade.Time <= to)
					result.Add(trade);
			}
			if (events.Length < 100 || oldest <= from)
				break;
		}
		return [.. result.GroupBy(static item => item.Id,
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.OrderBy(static item => item.Time)
			.TakeLast(maximum)];
	}

	public async ValueTask<BalancerQuote> GetQuoteAsync(
		BalancerDeployment deployment, BalancerMarket market,
		BalancerSwapTypes swapType, decimal amount,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(deployment);
		ArgumentNullException.ThrowIfNull(market);
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var specifiedAmount = amount.ToBaseUnits(market.BaseToken.Decimals);
		if (specifiedAmount <= 0)
			throw new InvalidOperationException(
				"Balancer quote amount rounds to zero base-token units.");
		var isExactIn = swapType == BalancerSwapTypes.ExactIn;
		var tokenIn = isExactIn ? market.BaseToken : market.QuoteToken;
		var tokenOut = isExactIn ? market.QuoteToken : market.BaseToken;
		var query = market.Pool.ProtocolVersion == 2
			? _quoteV2Query
			: _quoteV3Query;
		var response = await SendAsync<BalancerQuoteVariables,
			BalancerQuoteData>(query, new()
			{
				Chain = deployment.GraphChain,
				TokenIn = tokenIn.Address,
				TokenOut = tokenOut.Address,
				SwapType = swapType,
				SwapAmount = amount.ToString(
					"0.############################", CultureInfo.InvariantCulture),
				PoolIds = [market.Pool.Id],
			}, cancellationToken);
		var quote = response.Quote ?? throw new InvalidDataException(
			"Balancer SOR returned no quote.");
		ValidateQuote(quote, market, tokenIn, tokenOut);
		var input = (isExactIn ? quote.SwapAmountRaw : quote.ReturnAmountRaw)
			.ParseInteger();
		var output = (isExactIn ? quote.ReturnAmountRaw : quote.SwapAmountRaw)
			.ParseInteger();
		if (input <= 0 || output <= 0)
			throw new InvalidDataException(
				"Balancer SOR returned non-positive quote amounts.");
		if ((isExactIn ? input : output) != specifiedAmount)
			throw new InvalidDataException(
				"Balancer SOR changed the requested base-token amount.");
		var inputDecimal = input.FromBaseUnits(tokenIn.Decimals);
		var outputDecimal = output.FromBaseUnits(tokenOut.Decimals);
		var volume = isExactIn ? inputDecimal : outputDecimal;
		var quoteAmount = isExactIn ? outputDecimal : inputDecimal;
		if (volume <= 0 || quoteAmount <= 0)
			throw new InvalidDataException(
				"Balancer SOR returned an invalid executable price.");
		return new()
		{
			SwapType = swapType,
			InputAmount = input,
			OutputAmount = output,
			InputAmountDecimal = inputDecimal,
			OutputAmountDecimal = outputDecimal,
			Price = quoteAmount / volume,
		};
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	private static BalancerPool[] ConvertPools(BalancerGraphPool[] source,
		int maximum)
	{
		var result = new List<BalancerPool>(maximum);
		foreach (var item in source ?? [])
		{
			try
			{
				var pool = item.ToBalancer();
				if (IsSupported(pool))
					result.Add(pool);
			}
			catch (Exception error) when (error is ArgumentException or
				InvalidDataException or OverflowException)
			{
			}
			if (result.Count == maximum)
				break;
		}
		return [.. result];
	}

	public static bool IsSupported(BalancerPool pool)
		=> pool is not null && pool.ProtocolVersion is 2 or 3 &&
			pool.Type is not BalancerPoolTypes.CowAmm and
				not BalancerPoolTypes.Unknown &&
			pool.IsSwapEnabled && pool.Tokens is { Length: >= 2 and <= 8 };

	private static bool TryConvertTrade(BalancerGraphSwap source,
		BalancerMarket market, out BalancerTrade trade)
	{
		trade = null;
		if (source?.TokenIn is null || source.TokenOut is null ||
			source.TransactionHash.IsEmpty() ||
			!source.PoolId.EqualsIgnoreCase(market.Pool.Id))
			return false;
		var tokenIn = source.TokenIn.Address.NormalizeAddress();
		var tokenOut = source.TokenOut.Address.NormalizeAddress();
		Sides side;
		decimal volume;
		decimal quote;
		if (tokenIn.EqualsIgnoreCase(market.BaseToken.Address) &&
			tokenOut.EqualsIgnoreCase(market.QuoteToken.Address))
		{
			side = Sides.Sell;
			volume = BalancerExtensions.ParseDecimal(
				source.TokenIn.Amount, "swap input amount");
			quote = BalancerExtensions.ParseDecimal(
				source.TokenOut.Amount, "swap output amount");
		}
		else if (tokenIn.EqualsIgnoreCase(market.QuoteToken.Address) &&
			tokenOut.EqualsIgnoreCase(market.BaseToken.Address))
		{
			side = Sides.Buy;
			volume = BalancerExtensions.ParseDecimal(
				source.TokenOut.Amount, "swap output amount");
			quote = BalancerExtensions.ParseDecimal(
				source.TokenIn.Amount, "swap input amount");
		}
		else
			return false;
		if (volume <= 0 || quote <= 0)
			return false;
		trade = new()
		{
			Id = source.Id.IsEmpty()
				? source.TransactionHash + ":" + source.LogIndex.ToString(
					CultureInfo.InvariantCulture)
				: source.Id,
			TransactionHash = source.TransactionHash.NormalizeHash(),
			PoolId = market.Pool.Id,
			Time = new BigInteger(source.Timestamp).ToUtcTime(),
			Price = quote / volume,
			Volume = volume,
			Side = side,
			BlockNumber = source.BlockNumber,
			LogIndex = source.LogIndex,
		};
		return true;
	}

	private static void ValidateQuote(BalancerGraphQuote quote,
		BalancerMarket market, BalancerToken tokenIn, BalancerToken tokenOut)
	{
		if (quote.ProtocolVersion != market.Pool.ProtocolVersion ||
			!quote.TokenIn.NormalizeAddress().EqualsIgnoreCase(tokenIn.Address) ||
			!quote.TokenOut.NormalizeAddress().EqualsIgnoreCase(tokenOut.Address))
			throw new InvalidDataException(
				"Balancer SOR quote does not match the requested market.");
		if (quote.Paths is not { Length: > 0 })
			throw new InvalidDataException(
				"Balancer SOR found no executable direct path.");
		foreach (var path in quote.Paths)
		{
			if (path is null ||
				path.ProtocolVersion != market.Pool.ProtocolVersion ||
				path.Pools is not { Length: 1 } ||
				!path.Pools[0].EqualsIgnoreCase(market.Pool.Id) ||
				path.Tokens is not { Length: 2 } ||
				!path.Tokens[0].Address.NormalizeAddress().EqualsIgnoreCase(
					tokenIn.Address) ||
				!path.Tokens[1].Address.NormalizeAddress().EqualsIgnoreCase(
					tokenOut.Address) ||
				(path.IsBuffers ?? []).Any(static value => value))
				throw new NotSupportedException(
					"Balancer SOR selected a routed or buffer path; only the " +
					"configured direct pool is supported.");
		}
	}

	private async ValueTask<TData> SendAsync<TVariables, TData>(string query,
		TVariables variables, CancellationToken cancellationToken)
	{
		var payload = JsonConvert.SerializeObject(
			new BalancerGraphRequest<TVariables>
			{
				Query = query,
				Variables = variables,
			}, _jsonSettings);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
			{
				Content = new StringContent(payload, Encoding.UTF8,
					"application/json"),
			};
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
				throw new InvalidOperationException(
					$"Balancer API HTTP {(int)response.StatusCode}: " +
					Truncate(body));
			BalancerGraphResponse<TData> graph;
			try
			{
				graph = JsonConvert.DeserializeObject<
					BalancerGraphResponse<TData>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Balancer API returned an unexpected response shape.", error);
			}
			if (graph is null)
				throw new InvalidDataException(
					"Balancer API returned an empty response.");
			if (graph.Errors is { Length: > 0 })
				throw new InvalidOperationException("Balancer API: " +
					string.Join("; ", graph.Errors.Select(static error =>
						error?.Message ?? "request rejected")));
			return graph.Data ?? throw new InvalidDataException(
				"Balancer API returned no data.");
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
		}
		finally
		{
			_requestGate.Release();
		}
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
				"Balancer API response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var block = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(block, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"Balancer API response exceeds the 16 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}
