namespace StockSharp.DydxChain.Native;

sealed class DydxChainApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly HttpClient _indexer;
	private readonly HttpClient _validator;
	private readonly SemaphoreSlim _indexerRateSync = new(1, 1);
	private readonly SemaphoreSlim _validatorRateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextIndexerRequestTime;
	private DateTime _nextValidatorRequestTime;
	private long _rpcId;

	public DydxChainApiClient(string indexerEndpoint,
		string validatorEndpoint)
	{
		_indexer = CreateClient(indexerEndpoint, "Indexer");
		_validator = CreateClient(validatorEndpoint, "validator RPC");
	}

	public override string Name => "dYdX_HTTP";

	public async ValueTask<DydxChainMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> (await GetAsync<DydxChainMarketsResponse>(
			"v4/perpetualMarkets", cancellationToken)).Markets ?? [];

	public ValueTask<DydxChainOrderbookResponse> GetOrderbookAsync(
		string ticker, CancellationToken cancellationToken)
		=> GetAsync<DydxChainOrderbookResponse>(
			"v4/orderbooks/perpetualMarket/" + Escape(
				ticker.NormalizeTicker()), cancellationToken);

	public async ValueTask<DydxChainTrade[]> GetTradesAsync(string ticker,
		int limit, CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = "v4/trades/perpetualMarket/" + Escape(
			ticker.NormalizeTicker()) + "?limit=" + limit.ToString(
				CultureInfo.InvariantCulture);
		return (await GetAsync<DydxChainTradesResponse>(path,
			cancellationToken)).Trades ?? [];
	}

	public async ValueTask<DydxChainCandle[]> GetCandlesAsync(string ticker,
		DydxChainCandleResolutions resolution, DateTime? from, DateTime? to,
		int limit, CancellationToken cancellationToken)
	{
		ValidateLimit(limit);
		var path = new StringBuilder("v4/candles/perpetualMarkets/")
			.Append(Escape(ticker.NormalizeTicker()))
			.Append("?resolution=").Append(Escape(resolution.ToWire()))
			.Append("&limit=").Append(limit.ToString(
				CultureInfo.InvariantCulture));
		AppendIso(path, "fromISO", from);
		AppendIso(path, "toISO", to);
		return (await GetAsync<DydxChainCandlesResponse>(path.ToString(),
			cancellationToken)).Candles ?? [];
	}

	public ValueTask<DydxChainTimeResponse> GetTimeAsync(
		CancellationToken cancellationToken)
		=> GetAsync<DydxChainTimeResponse>("v4/time", cancellationToken);

	public ValueTask<DydxChainHeightResponse> GetHeightAsync(
		CancellationToken cancellationToken)
		=> GetAsync<DydxChainHeightResponse>("v4/height", cancellationToken);

	public ValueTask<DydxChainSubaccountSnapshot> GetSubaccountAsync(
		string address, int subaccountNumber,
		CancellationToken cancellationToken)
	{
		if (subaccountNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(subaccountNumber));
		return GetAsync<DydxChainSubaccountSnapshot>(
			"v4/addresses/" + Escape(address.NormalizeAddress()) +
			"/subaccountNumber/" + subaccountNumber.ToString(
				CultureInfo.InvariantCulture), cancellationToken);
	}

	public async ValueTask<DydxChainPerpetualPosition[]>
		GetPerpetualPositionsAsync(string address, int subaccountNumber,
		int limit, CancellationToken cancellationToken)
	{
		var path = AccountQuery("v4/perpetualPositions", address,
			subaccountNumber, limit);
		return (await GetAsync<DydxChainPerpetualPositionsResponse>(path,
			cancellationToken)).Positions ?? [];
	}

	public async ValueTask<DydxChainAssetPosition[]> GetAssetPositionsAsync(
		string address, int subaccountNumber, int limit,
		CancellationToken cancellationToken)
	{
		var path = AccountQuery("v4/assetPositions", address,
			subaccountNumber, limit);
		return (await GetAsync<DydxChainAssetPositionsResponse>(path,
			cancellationToken)).Positions ?? [];
	}

	public ValueTask<DydxChainOrder[]> GetOrdersAsync(string address,
		int subaccountNumber, int limit, CancellationToken cancellationToken)
		=> GetAsync<DydxChainOrder[]>(AccountQuery("v4/orders", address,
			subaccountNumber, limit) + "&returnLatestOrders=true",
			cancellationToken);

	public async ValueTask<DydxChainOrder> TryGetOrderAsync(string orderId,
		CancellationToken cancellationToken)
	{
		orderId = orderId.ThrowIfEmpty(nameof(orderId)).Trim();
		try
		{
			return await GetAsync<DydxChainOrder>("v4/orders/" +
				Escape(orderId), cancellationToken);
		}
		catch (HttpRequestException error)
			when (error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public async ValueTask<DydxChainFill[]> GetFillsAsync(string address,
		int subaccountNumber, int limit, CancellationToken cancellationToken)
	{
		var path = AccountQuery("v4/fills", address, subaccountNumber, limit);
		return (await GetAsync<DydxChainFillsResponse>(path,
			cancellationToken)).Fills ?? [];
	}

	public async ValueTask<DydxChainStatusResult> GetValidatorStatusAsync(
		CancellationToken cancellationToken)
	{
		var response = await RpcAsync<DydxChainStatusParameters,
			DydxChainStatusResult>(DydxChainRpcMethods.Status, new(),
			cancellationToken);
		return response;
	}

	public async ValueTask<DydxChainAccountInfo> GetAccountInfoAsync(
		string address, CancellationToken cancellationToken)
	{
		var query = DydxChainSigner.CreateAccountQuery(address);
		var response = await RpcAsync<DydxChainAbciQueryParameters,
			DydxChainAbciQueryResult>(DydxChainRpcMethods.AbciQuery, new()
			{
				Path = "/cosmos.auth.v1beta1.Query/Account",
				Data = Convert.ToHexString(query).ToLowerInvariant(),
			}, cancellationToken);
		var result = response.Response ?? throw new InvalidDataException(
			"dYdX validator returned no ABCI account response.");
		if (result.Code != 0)
			throw new InvalidOperationException(
				$"dYdX account query failed ({result.Codespace}:{result.Code}): " +
				result.Log);
		byte[] value;
		try
		{
			value = Convert.FromBase64String(result.Value.ThrowIfEmpty(
				"account response value"));
		}
		catch (FormatException error)
		{
			throw new InvalidDataException(
				"dYdX validator returned invalid account protobuf base64.",
				error);
		}
		return DydxChainSigner.ParseAccountQueryResponse(value);
	}

	public async ValueTask<DydxChainBroadcastResult> BroadcastAsync(
		byte[] transaction, CancellationToken cancellationToken)
	{
		if (transaction is not { Length: > 0 })
			throw new ArgumentException(
				"A signed dYdX transaction is required.",
				nameof(transaction));
		return await RpcAsync<DydxChainBroadcastParameters,
			DydxChainBroadcastResult>(
			DydxChainRpcMethods.BroadcastTransactionSync, new()
			{
				Transaction = Convert.ToBase64String(transaction),
			}, cancellationToken);
	}

	private ValueTask<T> GetAsync<T>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<T>(_indexer, _indexerRateSync, true, HttpMethod.Get,
			path, null, cancellationToken);

	private async ValueTask<TResult> RpcAsync<TParameters, TResult>(
		DydxChainRpcMethods method, TParameters parameters,
		CancellationToken cancellationToken)
		where TParameters : class
		where TResult : class
	{
		var request = new DydxChainRpcRequest<TParameters>
		{
			Id = Interlocked.Increment(ref _rpcId),
			Method = method,
			Parameters = parameters,
		};
		var json = JsonConvert.SerializeObject(request, _jsonSettings);
		var response = await SendAsync<DydxChainRpcResponse<TResult>>(
			_validator, _validatorRateSync, false, HttpMethod.Post, string.Empty,
			json, cancellationToken);
		if (response.Error is not null)
			throw new InvalidOperationException(
				$"dYdX validator RPC failed ({response.Error.Code}): " +
				(response.Error.Data.IsEmpty()
					? response.Error.Message
					: response.Error.Message + " - " + response.Error.Data));
		return response.Result ?? throw new InvalidDataException(
			"dYdX validator RPC returned no result.");
	}

	private async ValueTask<T> SendAsync<T>(HttpClient client,
		SemaphoreSlim rateSync, bool isIndexer, HttpMethod method, string path,
		string json, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(rateSync, isIndexer, cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			using var response = await client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
				HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateHttpException(response.StatusCode, body);
			try
			{
				return JsonConvert.DeserializeObject<T>(body, _jsonSettings) ??
					throw new InvalidDataException(
						"dYdX returned an empty JSON response.");
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"dYdX returned malformed JSON.", error);
			}
		}
	}

	private async ValueTask WaitRateLimitAsync(SemaphoreSlim sync,
		bool isIndexer, CancellationToken cancellationToken)
	{
		await sync.WaitAsync(cancellationToken);
		try
		{
			var next = isIndexer
				? _nextIndexerRequestTime
				: _nextValidatorRequestTime;
			var delay = next - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			if (isIndexer)
				_nextIndexerRequestTime = DateTime.UtcNow.AddMilliseconds(50);
			else
				_nextValidatorRequestTime = DateTime.UtcNow.AddMilliseconds(100);
		}
		finally
		{
			sync.Release();
		}
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"dYdX response exceeds the 16 MiB safety limit.");
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
					"dYdX response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}

	private HttpRequestException CreateHttpException(HttpStatusCode status,
		string body)
	{
		DydxChainErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<DydxChainErrorResponse>(body,
				_jsonSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message ?? error?.Error;
		if (detail.IsEmpty())
			detail = body.IsEmpty() ? "request failed" :
				body[..body.Length.Min(1024)];
		return new HttpRequestException(
			$"dYdX HTTP {(int)status} ({status}): {detail}", null, status);
	}

	private static HttpClient CreateClient(string endpoint, string name)
	{
		endpoint = endpoint.ThrowIfEmpty(name).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = "https://" + endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https") || !uri.UserInfo.IsEmpty() ||
			(uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback))
			throw new ArgumentException(
				$"dYdX {name} endpoint must use HTTPS, except for a local node.",
				name);
		var client = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			BaseAddress = new Uri(uri.ToString().TrimEnd('/') + "/"),
			Timeout = TimeSpan.FromSeconds(30),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-dYdX-Connector/1.0");
		client.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		return client;
	}

	private static string AccountQuery(string path, string address,
		int subaccountNumber, int limit)
	{
		if (subaccountNumber < 0)
			throw new ArgumentOutOfRangeException(nameof(subaccountNumber));
		ValidateLimit(limit);
		return path + "?address=" + Escape(address.NormalizeAddress()) +
			"&subaccountNumber=" + subaccountNumber.ToString(
				CultureInfo.InvariantCulture) + "&limit=" + limit.ToString(
				CultureInfo.InvariantCulture);
	}

	private static void AppendIso(StringBuilder path, string name,
		DateTime? value)
	{
		if (value is DateTime time)
			path.Append('&').Append(name).Append('=').Append(Escape(
				time.EnsureUtc().ToString("O", CultureInfo.InvariantCulture)));
	}

	private static void ValidateLimit(int limit)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit), limit,
				"dYdX request limit must be between 1 and 1000.");
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).Trim());

	protected override void DisposeManaged()
	{
		_indexer.Dispose();
		_validator.Dispose();
		_indexerRateSync.Dispose();
		_validatorRateSync.Dispose();
		base.DisposeManaged();
	}
}
