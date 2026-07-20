namespace StockSharp.GMTrade.Native;

sealed class GMTradeRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private const string _marketsQuery = """
		query Markets {
		  markets {
		    marketToken
		    pubkey
		    slot
		    meta {
		      name
		      store
		      isPure
		      isEnabled
		      indexToken {
		        pubkey
		        price { ts min max isOpen }
		        meta {
		          name
		          uiSymbol
		          uiName
		          decimals
		          precision
		          category
		          isEnabled
		          isSynthetic
		        }
		      }
		      longToken {
		        pubkey
		        price { ts min max isOpen }
		        meta { name uiSymbol uiName decimals precision category isEnabled isSynthetic }
		      }
		      shortToken {
		        pubkey
		        price { ts min max isOpen }
		        meta { name uiSymbol uiName decimals precision category isEnabled isSynthetic }
		      }
		    }
		  }
		}
		""";
	private const string _candlesQuery = """
		query Candles($indexToken: String!, $resolution: Int!, $from: Int!, $to: Int!) {
		  candles(indexToken: $indexToken, resolution: $resolution, from: $from, to: $to) {
		    indexToken
		    resolution
		    timestamp
		    open
		    high
		    low
		    close
		  }
		}
		""";
	private const string _tradesQuery = """
		query Trades($where: TradeEventWhereInput, $limit: Int!) {
		  tradeEvents(where: $where, orderBy: timestamp_DESC, limit: $limit) {
		    id
		    timestamp
		    flags
		    tradeId
		    user
		    marketToken
		    position
		    order
		    executionPrice
		    beforeSizeInTokens
		    afterSizeInTokens
		    beforeSizeInUsd
		    afterSizeInUsd
		    beforeCollateralAmount
		    afterCollateralAmount
		    pnlPnl
		  }
		}
		""";
	private const string _userQuery = """
		query User($owner: String!) {
		  user(owner: $owner) {
		    pubkey
		    positions(skipRevalidation: false) {
		      pubkey
		      isInsert
		      slot
		      store
		      kind
		      owner
		      marketToken
		      collateralToken
		      tradeId
		      increasedAt
		      updatedAtSlot
		      decreasedAt
		      sizeInTokens
		      collateralAmount
		      size
		    }
		    orders(skipRevalidation: false) {
		      pubkey
		      isInsert
		      slot
		      marketToken
		      initialCollateralToken
		      finalOutputToken
		      header { id store market owner status updatedAt updatedAtSlot }
		      params { kind side amount size acceptablePrice triggerPrice minOutput validFromTs }
		    }
		  }
		}
		""";

	private readonly Uri _keeperEndpoint;
	private readonly Uri _candleEndpoint;
	private readonly Uri _indexerEndpoint;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;

	public GMTradeRestClient(string keeperEndpoint, string candleEndpoint,
		string indexerEndpoint)
	{
		_keeperEndpoint = CreateEndpoint(keeperEndpoint,
			nameof(keeperEndpoint));
		_candleEndpoint = CreateEndpoint(candleEndpoint,
			nameof(candleEndpoint));
		_indexerEndpoint = CreateEndpoint(indexerEndpoint,
			nameof(indexerEndpoint));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-GMTrade-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "GMTRADE_GRAPHQL";

	public async ValueTask<GMTradeMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await SendAsync<GMTradeNoVariables, GMTradeMarketsData>(
			_keeperEndpoint, _marketsQuery, new(), cancellationToken)).Markets ?? [];

	public async ValueTask<GMTradeCandle[]> GetCandlesAsync(string indexToken,
		int resolution, DateTime from, DateTime to,
		CancellationToken cancellationToken)
		=> (await SendAsync<GMTradeCandlesVariables, GMTradeCandlesData>(
			_candleEndpoint, _candlesQuery, new()
			{
				IndexToken = indexToken.ThrowIfEmpty(nameof(indexToken)).Trim(),
				Resolution = resolution,
				From = checked((long)from.EnsureUtc().ToUnix()),
				To = checked((long)to.EnsureUtc().ToUnix()),
			}, cancellationToken)).Candles ?? [];

	public async ValueTask<GMTradeTrade[]> GetTradesAsync(
		GMTradeTradeFilter filter,
		int limit, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 5000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		return (await SendAsync<GMTradeTradesVariables, GMTradeTradesData>(
			_indexerEndpoint, _tradesQuery, new()
			{
				Filter = filter ?? new(),
				Limit = limit,
			}, cancellationToken)).Trades ?? [];
	}

	public async ValueTask<GMTradeUser> GetUserAsync(string owner,
		CancellationToken cancellationToken)
	{
		var data = await SendAsync<GMTradeUserVariables, GMTradeUserData>(
			_keeperEndpoint, _userQuery, new()
			{
				Owner = owner.NormalizePublicKey(nameof(owner)),
			}, cancellationToken);
		return data.User ?? new GMTradeUser
		{
			PublicKey = owner,
			Positions = [],
			Orders = [],
		};
	}

	private async ValueTask<TData> SendAsync<TVariables, TData>(Uri endpoint,
		string query, TVariables variables,
		CancellationToken cancellationToken)
		where TData : class
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			var request = new GMTradeGraphQlRequest<TVariables>
			{
				Query = query,
				Variables = variables,
			};
			using var content = new StringContent(JsonConvert.SerializeObject(
				request, _jsonSettings), Encoding.UTF8, "application/json");
			using var response = await _http.PostAsync(endpoint, content,
				cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 3 && IsTransient(response.StatusCode))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"GMTrade HTTP {(int)response.StatusCode} " +
					$"({response.StatusCode}): {Limit(body, 1024)}");

			GMTradeGraphQlResponse<TData> envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<
					GMTradeGraphQlResponse<TData>>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"GMTrade returned malformed GraphQL JSON.", error);
			}
			if (envelope is null)
				throw new InvalidDataException(
					"GMTrade returned an empty GraphQL response.");
			if (envelope.Errors is { Length: > 0 })
				throw new InvalidOperationException(
					"GMTrade GraphQL error: " + string.Join("; ",
						envelope.Errors.Select(static error => error.Message)
							.Where(static message => !message.IsEmpty())));
			return envelope.Data ?? throw new InvalidDataException(
				"GMTrade returned no GraphQL data.");
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(50);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static Uri CreateEndpoint(string endpoint, string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"GMTrade GraphQL endpoints must use HTTP or HTTPS.",
				parameterName);
		return uri;
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"GMTrade response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"GMTrade response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
