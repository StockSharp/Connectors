namespace StockSharp.Injective.Native;

sealed class InjectiveRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 32 * 1024 * 1024;
	private readonly HttpClient _indexer;
	private readonly HttpClient _chain;
	private readonly SemaphoreSlim _indexerGate = new(1, 1);
	private readonly SemaphoreSlim _chainGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextIndexerRequest;
	private DateTime _nextChainRequest;

	public InjectiveRestClient(string indexerEndpoint, string chainEndpoint)
	{
		_indexer = CreateClient(indexerEndpoint, "indexer");
		_chain = CreateClient(chainEndpoint, "chain LCD");
	}

	public override string Name => "Injective_HTTP";

	public async ValueTask<InjectiveSpotMarket[]> GetSpotMarketsAsync(
		CancellationToken cancellationToken)
		=> (await GetIndexerAsync<InjectiveSpotMarketsEnvelope>(
			"api/exchange/spot/v1/markets", cancellationToken)).Markets ?? [];

	public async ValueTask<InjectiveDerivativeMarket[]>
		GetDerivativeMarketsAsync(CancellationToken cancellationToken)
		=> (await GetIndexerAsync<InjectiveDerivativeMarketsEnvelope>(
			"api/exchange/derivative/v1/markets", cancellationToken)).Markets ?? [];

	public ValueTask<InjectiveOrderBookEnvelope> GetOrderBookAsync(
		InjectiveMarket market, int depth, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (depth is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(depth));
		return GetIndexerAsync<InjectiveOrderBookEnvelope>(
			$"api/exchange/{market.Kind.ToPath()}/v2/orderbook/" +
			Escape(market.MarketId) + "?depth=" + depth.ToString(
				CultureInfo.InvariantCulture), cancellationToken);
	}

	public async ValueTask<InjectiveTrade[]> GetTradesAsync(
		InjectiveMarket market, string subaccountId, DateTime? from, DateTime? to,
		int limit, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		return await GetTradesAsync(market.Kind, market.MarketId, subaccountId,
			from, to, limit, cancellationToken);
	}

	public ValueTask<InjectiveTrade[]> GetAccountTradesAsync(
		InjectiveMarketKinds kind, string subaccountId, DateTime? from,
		DateTime? to, int limit, CancellationToken cancellationToken)
		=> GetTradesAsync(kind, null,
			subaccountId.ThrowIfEmpty(nameof(subaccountId)), from, to, limit,
			cancellationToken);

	private async ValueTask<InjectiveTrade[]> GetTradesAsync(
		InjectiveMarketKinds kind, string marketId, string subaccountId,
		DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder($"api/exchange/{kind.ToPath()}" +
			"/v2/trades?limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		if (!marketId.IsEmpty())
			path.Append("&marketIds=").Append(Escape(marketId));
		if (!subaccountId.IsEmpty())
			path.Append("&subaccountIds=").Append(Escape(subaccountId));
		AppendMilliseconds(path, "startTime", from);
		AppendMilliseconds(path, "endTime", to);
		return (await GetIndexerAsync<InjectiveTradesEnvelope>(path.ToString(),
			cancellationToken)).Trades ?? [];
	}

	public ValueTask<InjectiveMarketSummary[]> GetMarketSummariesAsync(
		InjectiveMarketKinds kind, CancellationToken cancellationToken)
		=> GetIndexerAsync<InjectiveMarketSummary[]>(
			$"api/chart/v1/{kind.ToPath()}/market_summary_all?resolution=24h",
			cancellationToken);

	public ValueTask<InjectiveChartHistory> GetCandlesAsync(
		InjectiveMarket market, TimeSpan timeFrame, DateTime? from, DateTime to,
		int count, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		ValidateLimit(count);
		var path = new StringBuilder($"api/chart/v1/{market.Kind.ToPath()}" +
			"/history?marketId=").Append(Escape(market.MarketId))
			.Append("&resolution=").Append(Escape(timeFrame.ToChartResolution()))
			.Append("&to=").Append(to.ToInjectiveSeconds().ToString(
				CultureInfo.InvariantCulture));
		if (from is DateTime start)
			path.Append("&from=").Append(start.ToInjectiveSeconds().ToString(
				CultureInfo.InvariantCulture));
		else
			path.Append("&countback=").Append(count.ToString(
				CultureInfo.InvariantCulture));
		return GetIndexerAsync<InjectiveChartHistory>(path.ToString(),
			cancellationToken);
	}

	public ValueTask<InjectivePortfolioEnvelope> GetPortfolioAsync(
		string address, CancellationToken cancellationToken)
		=> GetIndexerAsync<InjectivePortfolioEnvelope>(
			"api/exchange/portfolio/v2/portfolio/" +
			Escape(address.ThrowIfEmpty(nameof(address))), cancellationToken);

	public async ValueTask<InjectivePosition[]> GetPositionsAsync(
		string subaccountId, string marketId, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder(
			"api/exchange/derivative/v2/positions?subaccountId=")
			.Append(Escape(subaccountId.ThrowIfEmpty(nameof(subaccountId))))
			.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		if (!marketId.IsEmpty())
			path.Append("&marketIds=").Append(Escape(marketId));
		return (await GetIndexerAsync<InjectivePositionsEnvelope>(path.ToString(),
			cancellationToken)).Positions ?? [];
	}

	public async ValueTask<InjectiveOrder[]> GetOrdersAsync(
		InjectiveMarketKinds kind, string subaccountId, string marketId,
		bool isHistory, DateTime? from, DateTime? to, int limit,
		CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder($"api/exchange/{kind.ToPath()}/v1/")
			.Append(isHistory ? "ordersHistory" : "orders")
			.Append("?subaccountId=")
			.Append(Escape(subaccountId.ThrowIfEmpty(nameof(subaccountId))))
			.Append("&limit=").Append(limit.ToString(CultureInfo.InvariantCulture));
		if (!marketId.IsEmpty())
			path.Append(isHistory ? "&marketIds=" : "&marketId=")
				.Append(Escape(marketId));
		if (!isHistory)
			path.Append("&includeInactive=true");
		AppendMilliseconds(path, "startTime", from);
		AppendMilliseconds(path, "endTime", to);
		return (await GetIndexerAsync<InjectiveOrdersEnvelope>(path.ToString(),
			cancellationToken)).Orders ?? [];
	}

	public ValueTask<InjectiveAccountEnvelope> GetAccountAsync(string address,
		CancellationToken cancellationToken)
		=> GetChainAsync<InjectiveAccountEnvelope>(
			"cosmos/auth/v1beta1/accounts/" +
			Escape(address.ThrowIfEmpty(nameof(address))), cancellationToken);

	public ValueTask<InjectiveLatestBlockEnvelope> GetLatestBlockAsync(
		CancellationToken cancellationToken)
		=> GetChainAsync<InjectiveLatestBlockEnvelope>(
			"cosmos/base/tendermint/v1beta1/blocks/latest", cancellationToken);

	public ValueTask<InjectiveBroadcastEnvelope> BroadcastAsync(
		byte[] transaction, CancellationToken cancellationToken)
	{
		if (transaction is not { Length: > 0 })
			throw new ArgumentException(
				"A signed Injective transaction is required.",
				nameof(transaction));
		return SendWithBodyAsync<InjectiveBroadcastEnvelope,
			InjectiveBroadcastRequest>(_chain, _chainGate, false,
			HttpMethod.Post, "cosmos/tx/v1beta1/txs", new InjectiveBroadcastRequest
			{
				TransactionBytes = Convert.ToBase64String(transaction),
				Mode = "BROADCAST_MODE_SYNC",
			}, cancellationToken);
	}

	public async ValueTask<InjectiveTransactionResponse> TryGetTransactionAsync(
		string hash, CancellationToken cancellationToken)
	{
		try
		{
			return (await GetChainAsync<InjectiveBroadcastEnvelope>(
				"cosmos/tx/v1beta1/txs/" +
				Escape(hash.ThrowIfEmpty(nameof(hash))), cancellationToken))
				.TransactionResponse;
		}
		catch (HttpRequestException error)
			when (error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	private ValueTask<T> GetIndexerAsync<T>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<T>(_indexer, _indexerGate, true, HttpMethod.Get, path, null,
			cancellationToken);

	private ValueTask<T> GetChainAsync<T>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<T>(_chain, _chainGate, false, HttpMethod.Get, path, null,
			cancellationToken);

	private ValueTask<TResponse> SendWithBodyAsync<TResponse, TRequest>(
		HttpClient client, SemaphoreSlim gate, bool isIndexer, HttpMethod method,
		string path, TRequest body, CancellationToken cancellationToken)
		where TRequest : class
	{
		ArgumentNullException.ThrowIfNull(body);
		return SendAsync<TResponse>(client, gate, isIndexer, method, path,
			JsonConvert.SerializeObject(body, _settings), cancellationToken);
	}

	private async ValueTask<T> SendAsync<T>(HttpClient client,
		SemaphoreSlim gate, bool isIndexer, HttpMethod method, string path,
		string body, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(gate, isIndexer, cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (body is not null)
				request.Content = new StringContent(body, Encoding.UTF8,
					"application/json");
			using var response = await client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var text = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
				HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
			{
				var error = TryDeserialize<InjectiveApiError>(text);
				throw new HttpRequestException(
					$"Injective {method} {path} failed with HTTP " +
					$"{(int)response.StatusCode}: " +
					(error?.Message ?? error?.Details ?? text).Trim(), null,
					response.StatusCode);
			}
			return Deserialize<T>(text);
		}
	}

	private async ValueTask WaitRateLimitAsync(SemaphoreSlim gate,
		bool isIndexer, CancellationToken cancellationToken)
	{
		await gate.WaitAsync(cancellationToken);
		try
		{
			var now = DateTime.UtcNow;
			var next = isIndexer ? _nextIndexerRequest : _nextChainRequest;
			if (next > now)
				await Task.Delay(next - now, cancellationToken);
			if (isIndexer)
				_nextIndexerRequest = DateTime.UtcNow.AddMilliseconds(20);
			else
				_nextChainRequest = DateTime.UtcNow.AddMilliseconds(40);
		}
		finally
		{
			gate.Release();
		}
	}

	private async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Injective response exceeds the connector size limit.");
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
					"Injective response exceeds the connector size limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private T Deserialize<T>(string text)
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(text, _settings) ??
				throw new InvalidDataException(
					"Injective returned an empty JSON value.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Injective returned malformed JSON.", error);
		}
	}

	private T TryDeserialize<T>(string text) where T : class
	{
		try
		{
			return JsonConvert.DeserializeObject<T>(text, _settings);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static HttpClient CreateClient(string endpoint, string name)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(endpoint + "/", UriKind.Absolute, out var address) ||
			address.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				$"Injective {name} endpoint is invalid.", nameof(endpoint));
		return new()
		{
			BaseAddress = address,
			Timeout = TimeSpan.FromSeconds(30),
			DefaultRequestHeaders =
			{
				{ "Accept", "application/json" },
				{ "User-Agent", "StockSharp-Injective/1.0" },
			},
		};
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value);

	private static void AppendMilliseconds(StringBuilder builder, string name,
		DateTime? value)
	{
		if (value is DateTime time)
			builder.Append('&').Append(name).Append('=').Append(
				time.ToInjectiveMilliseconds().ToString(CultureInfo.InvariantCulture));
	}

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
	}

	protected override void DisposeManaged()
	{
		_indexer.Dispose();
		_chain.Dispose();
		_indexerGate.Dispose();
		_chainGate.Dispose();
		base.DisposeManaged();
	}
}

static class InjectiveMarketKindExtensions
{
	public static string ToPath(this InjectiveMarketKinds kind)
		=> kind switch
		{
			InjectiveMarketKinds.Spot => "spot",
			InjectiveMarketKinds.Derivative => "derivative",
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
		};
}
