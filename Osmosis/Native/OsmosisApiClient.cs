namespace StockSharp.Osmosis.Native;

sealed class OsmosisApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;

	private sealed class RequestLane : IDisposable
	{
		public RequestLane(TimeSpan interval) => Interval = interval;

		public SemaphoreSlim Gate { get; } = new(1, 1);
		public TimeSpan Interval { get; }
		public DateTime NextRequestTime { get; set; }

		public void Dispose() => Gate.Dispose();
	}

	private readonly HttpClient _sqsClient;
	private readonly HttpClient _lcdClient;
	private readonly HttpClient _rpcClient;
	private readonly HttpClient _assetClient;
	private readonly Uri _assetListUri;
	private readonly RequestLane _sqsLane =
		new(TimeSpan.FromMilliseconds(150));
	private readonly RequestLane _lcdLane =
		new(TimeSpan.FromMilliseconds(200));
	private readonly RequestLane _rpcLane =
		new(TimeSpan.FromMilliseconds(200));
	private readonly RequestLane _assetLane =
		new(TimeSpan.FromMilliseconds(250));
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};
	private bool _isDisposed;

	public OsmosisApiClient(string sqsEndpoint, string lcdEndpoint,
		string rpcEndpoint, string assetListEndpoint)
	{
		_sqsClient = CreateClient(sqsEndpoint, "SQS");
		_lcdClient = CreateClient(lcdEndpoint, "LCD");
		_rpcClient = CreateClient(rpcEndpoint, "RPC");
		_assetListUri = NormalizeAbsoluteEndpoint(assetListEndpoint,
			"asset-list");
		_assetClient = new HttpClient
		{
			Timeout = TimeSpan.FromSeconds(45),
		};
		_assetClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Osmosis/1.0");
	}

	public override string Name => "Osmosis_HTTP";

	public ValueTask<OsmosisHealthResponse> GetHealthAsync(
		CancellationToken cancellationToken)
		=> GetAsync<OsmosisHealthResponse>(_sqsClient, _sqsLane,
			"healthcheck", cancellationToken);

	public ValueTask<OsmosisAssetList> GetAssetListAsync(
		CancellationToken cancellationToken)
		=> SendAsync<OsmosisAssetList>(_assetClient, _assetLane,
			HttpMethod.Get, _assetListUri.ToString(), null, true,
			cancellationToken);

	public async ValueTask<OsmosisQuote> GetExactInputQuoteAsync(
		string inputDenomination, string outputDenomination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		inputDenomination = inputDenomination.NormalizeDenomination();
		outputDenomination = outputDenomination.NormalizeDenomination();
		var path = "router/quote?tokenIn=" + Escape(
			amount.ToString(CultureInfo.InvariantCulture) + inputDenomination) +
			"&tokenOutDenom=" + Escape(outputDenomination) +
			"&singleRoute=true";
		var response = await GetAsync<OsmosisSqsQuote>(_sqsClient, _sqsLane,
			path, cancellationToken);
		var route = RequireSingleRoute(response?.Routes);
		if (response.Input is null ||
			!response.Input.Denomination.Equals(inputDenomination,
				StringComparison.Ordinal) ||
			response.Input.Amount.ParseAmount("quote input", true) != amount)
			throw new InvalidDataException(
				"Osmosis SQS returned a mismatched exact-input amount.");
		var output = response.Output.ParseAmount("quote output", true);
		ValidateExactInputRoute(route, outputDenomination);
		return new()
		{
			Kind = OsmosisSwapKinds.ExactInput,
			InputAmount = amount,
			OutputAmount = output,
			Pools = route.Pools,
		};
	}

	public async ValueTask<OsmosisQuote> GetExactOutputQuoteAsync(
		string inputDenomination, string outputDenomination, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		inputDenomination = inputDenomination.NormalizeDenomination();
		outputDenomination = outputDenomination.NormalizeDenomination();
		var path = "router/quote?tokenOut=" + Escape(
			amount.ToString(CultureInfo.InvariantCulture) + outputDenomination) +
			"&tokenInDenom=" + Escape(inputDenomination) +
			"&singleRoute=true";
		var response = await GetAsync<OsmosisSqsExactOutputQuote>(_sqsClient,
			_sqsLane, path, cancellationToken);
		var route = RequireSingleRoute(response?.Routes);
		if (response.Output is null ||
			!response.Output.Denomination.Equals(outputDenomination,
				StringComparison.Ordinal) ||
			response.Output.Amount.ParseAmount("quote output", true) != amount)
			throw new InvalidDataException(
				"Osmosis SQS returned a mismatched exact-output amount.");
		var input = response.Input.ParseAmount("quote input", true);
		ValidateExactOutputRoute(route, inputDenomination);
		return new()
		{
			Kind = OsmosisSwapKinds.ExactOutput,
			InputAmount = input,
			OutputAmount = amount,
			Pools = route.Pools,
		};
	}

	public ValueTask<OsmosisRpcResponse<OsmosisStatusResult>> GetStatusAsync(
		CancellationToken cancellationToken)
		=> GetAsync<OsmosisRpcResponse<OsmosisStatusResult>>(_rpcClient,
			_rpcLane, "status", cancellationToken);

	public async ValueTask<DateTime> GetBlockTimeAsync(long height,
		CancellationToken cancellationToken)
	{
		if (height <= 0)
			throw new ArgumentOutOfRangeException(nameof(height));
		var response = await GetAsync<OsmosisRpcResponse<OsmosisBlockResult>>(
			_rpcClient, _rpcLane, "block?height=" + height.ToString(
				CultureInfo.InvariantCulture), cancellationToken);
		if (response.Error is not null)
			throw new InvalidDataException(
				$"Osmosis RPC block query failed ({response.Error.Code}): " +
				response.Error.Message);
		var header = response.Result?.Block?.Header ?? throw new
			InvalidDataException("Osmosis RPC returned no block header.");
		if (header.Height.ParseUnsigned("block height") != (ulong)height)
			throw new InvalidDataException(
				"Osmosis RPC returned a different block height.");
		return header.Time.ParseUtcTime("block time");
	}

	public ValueTask<OsmosisBalancesResponse> GetBalancesAsync(string address,
		CancellationToken cancellationToken)
		=> GetAsync<OsmosisBalancesResponse>(_lcdClient, _lcdLane,
			"cosmos/bank/v1beta1/balances/" + Escape(
				address.NormalizeOsmosisAddress()) + "?pagination.limit=1000",
			cancellationToken);

	public ValueTask<OsmosisAccountResponse> GetAccountAsync(string address,
		CancellationToken cancellationToken)
		=> GetAsync<OsmosisAccountResponse>(_lcdClient, _lcdLane,
			"cosmos/auth/v1beta1/accounts/" + Escape(
				address.NormalizeOsmosisAddress()), cancellationToken);

	public ValueTask<OsmosisBaseFeeResponse> GetBaseFeeAsync(
		CancellationToken cancellationToken)
		=> GetAsync<OsmosisBaseFeeResponse>(_lcdClient, _lcdLane,
			"osmosis/txfees/v1beta1/cur_eip_base_fee", cancellationToken);

	public ValueTask<OsmosisSimulateResponse> SimulateAsync(
		OsmosisSimulateRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendJsonAsync<OsmosisSimulateRequest, OsmosisSimulateResponse>(
			_lcdClient, _lcdLane, HttpMethod.Post,
			"cosmos/tx/v1beta1/simulate", request, false,
			cancellationToken);
	}

	public ValueTask<OsmosisTransactionResponse> BroadcastAsync(
		OsmosisBroadcastRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendJsonAsync<OsmosisBroadcastRequest,
			OsmosisTransactionResponse>(_lcdClient, _lcdLane, HttpMethod.Post,
			"cosmos/tx/v1beta1/txs", request, false, cancellationToken);
	}

	public async ValueTask<OsmosisTransactionResponse> TryGetTransactionAsync(
		string hash, CancellationToken cancellationToken)
	{
		try
		{
			return await GetAsync<OsmosisTransactionResponse>(_lcdClient,
				_lcdLane, "cosmos/tx/v1beta1/txs/" + Escape(
					hash.NormalizeTransactionHash()), cancellationToken);
		}
		catch (OsmosisApiException error)
			when (error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	private static OsmosisSqsRoute RequireSingleRoute(
		OsmosisSqsRoute[] routes)
	{
		if (routes is not { Length: 1 } ||
			routes[0]?.Pools is not { Length: > 0 })
			throw new InvalidDataException(
				"Osmosis SQS did not return one executable route.");
		foreach (var pool in routes[0].Pools)
			if (pool is null || pool.Id == 0)
				throw new InvalidDataException(
					"Osmosis SQS returned an invalid pool route.");
		return routes[0];
	}

	private static void ValidateExactInputRoute(OsmosisSqsRoute route,
		string outputDenomination)
	{
		var last = route.Pools[^1];
		if (!last.OutputDenomination.NormalizeDenomination().Equals(
			outputDenomination, StringComparison.Ordinal))
			throw new InvalidDataException(
				"Osmosis SQS exact-input route has an unexpected output.");
		foreach (var pool in route.Pools)
			_ = pool.OutputDenomination.NormalizeDenomination();
	}

	private static void ValidateExactOutputRoute(OsmosisSqsRoute route,
		string inputDenomination)
	{
		var first = route.Pools[0];
		if (!first.InputDenomination.NormalizeDenomination().Equals(
			inputDenomination, StringComparison.Ordinal))
			throw new InvalidDataException(
				"Osmosis SQS exact-output route has an unexpected input.");
		foreach (var pool in route.Pools)
			_ = pool.InputDenomination.NormalizeDenomination();
	}

	private ValueTask<TResponse> GetAsync<TResponse>(HttpClient client,
		RequestLane lane, string path, CancellationToken cancellationToken)
		=> SendAsync<TResponse>(client, lane, HttpMethod.Get, path, null, true,
			cancellationToken);

	private ValueTask<TResponse> SendJsonAsync<TRequest, TResponse>(
		HttpClient client, RequestLane lane, HttpMethod method, string path,
		TRequest request, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(client, lane, method, path,
			JsonConvert.SerializeObject(request, _serializerSettings),
			isRetryAllowed, cancellationToken);

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpClient client,
		RequestLane lane, HttpMethod method, string path, string json,
		bool isRetryAllowed, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitForLaneAsync(lane, cancellationToken);
			using var request = new HttpRequestMessage(method, path);
			if (json is not null)
				request.Content = new StringContent(json, Encoding.UTF8,
					"application/json");
			using var response = await client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (response.Content.Headers.ContentLength is long length &&
				length > _maximumResponseLength)
				throw new OsmosisApiException(response.StatusCode, null,
					"Osmosis response exceeds the safety limit.");
			var body = await response.Content.ReadAsStringAsync(cancellationToken);
			if (body.Length > _maximumResponseLength)
				throw new OsmosisApiException(response.StatusCode, null,
					"Osmosis response exceeds the safety limit.");
			if (!response.IsSuccessStatusCode)
			{
				if (isRetryAllowed && attempt < 2 &&
					(response.StatusCode == HttpStatusCode.TooManyRequests ||
						(int)response.StatusCode >= 500))
				{
					await Task.Delay(TimeSpan.FromSeconds(attempt + 1),
						cancellationToken);
					continue;
				}
				throw CreateApiException(response.StatusCode, body);
			}
			try
			{
				var result = JsonConvert.DeserializeObject<TResponse>(body,
					_serializerSettings);
				return result is null
					? throw new OsmosisApiException(response.StatusCode, null,
						"Osmosis returned an empty JSON response.")
					: result;
			}
			catch (JsonException error)
			{
				throw new OsmosisApiException(
					"Osmosis returned malformed JSON.", error);
			}
		}
	}

	private static async ValueTask WaitForLaneAsync(RequestLane lane,
		CancellationToken cancellationToken)
	{
		await lane.Gate.WaitAsync(cancellationToken);
		try
		{
			var delay = lane.NextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			lane.NextRequestTime = DateTime.UtcNow + lane.Interval;
		}
		finally
		{
			lane.Gate.Release();
		}
	}

	private OsmosisApiException CreateApiException(HttpStatusCode status,
		string body)
	{
		OsmosisErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<OsmosisErrorResponse>(body,
				_serializerSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message ?? error?.Error;
		if (detail.IsEmpty())
			detail = body.Length <= 512 ? body : body[..512];
		return new(status, error?.Code,
			$"Osmosis HTTP {(int)status} ({status}): {detail}");
	}

	private static HttpClient CreateClient(string endpoint, string name)
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri(NormalizeAbsoluteEndpoint(endpoint, name)
				.ToString().TrimEnd('/') + "/", UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Osmosis/1.0");
		return client;
	}

	private static Uri NormalizeAbsoluteEndpoint(string value, string name)
	{
		value = value.ThrowIfEmpty(name).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https") || !uri.UserInfo.IsEmpty() ||
			(uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback))
			throw new ArgumentException(
				$"Osmosis {name} endpoint must use HTTPS, except for a local node.",
				name);
		return uri;
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_sqsClient.Dispose();
		_lcdClient.Dispose();
		_rpcClient.Dispose();
		_assetClient.Dispose();
		_sqsLane.Dispose();
		_lcdLane.Dispose();
		_rpcLane.Dispose();
		_assetLane.Dispose();
		base.DisposeManaged();
	}
}
