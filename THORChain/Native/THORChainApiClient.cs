namespace StockSharp.THORChain.Native;

sealed class THORChainApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;

	private sealed class RequestLane : IDisposable
	{
		public RequestLane(TimeSpan interval)
		{
			Interval = interval;
		}

		public SemaphoreSlim Gate { get; } = new(1, 1);
		public TimeSpan Interval { get; }
		public DateTime NextRequestTime { get; set; }

		public void Dispose() => Gate.Dispose();
	}

	private readonly HttpClient _midgardClient;
	private readonly HttpClient _thornodeClient;
	private readonly RequestLane _midgardLane =
		new(TimeSpan.FromMilliseconds(250));
	private readonly RequestLane _thornodeLane =
		new(TimeSpan.FromMilliseconds(250));
	private readonly RequestLane _quoteLane =
		new(TimeSpan.FromMilliseconds(1050));
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};
	private bool _isDisposed;

	public THORChainApiClient(string midgardEndpoint,
		string thornodeEndpoint, string clientId)
	{
		_midgardClient = CreateClient(midgardEndpoint, clientId);
		_thornodeClient = CreateClient(thornodeEndpoint, clientId);
	}

	public override string Name => "THORChain_REST";

	public ValueTask<THORChainPool[]> GetPoolsAsync(
		CancellationToken cancellationToken)
		=> GetAsync<THORChainPool[]>(_midgardClient, _midgardLane,
			"pools?status=available&period=24h", cancellationToken);

	public ValueTask<THORChainQuote> GetQuoteAsync(string fromAsset,
		string toAsset, BigInteger amount, string destination,
		string refundAddress, int streamingInterval, int streamingQuantity,
		int? liquidityToleranceBasisPoints,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (streamingInterval is < 0 or > 100_000)
			throw new ArgumentOutOfRangeException(nameof(streamingInterval));
		if (streamingQuantity is < 0 or > 100_000)
			throw new ArgumentOutOfRangeException(nameof(streamingQuantity));
		if (liquidityToleranceBasisPoints is < 1 or > 10_000)
			throw new ArgumentOutOfRangeException(
				nameof(liquidityToleranceBasisPoints));

		var path = new StringBuilder("thorchain/quote/swap?from_asset=")
			.Append(Escape(NormalizeAsset(fromAsset)))
			.Append("&to_asset=").Append(Escape(NormalizeAsset(toAsset)))
			.Append("&amount=").Append(amount.ToString(
				CultureInfo.InvariantCulture))
			.Append("&streaming_interval=").Append(streamingInterval.ToString(
				CultureInfo.InvariantCulture))
			.Append("&streaming_quantity=").Append(streamingQuantity.ToString(
				CultureInfo.InvariantCulture));
		if (!destination.IsEmpty())
			path.Append("&destination=").Append(Escape(destination.Trim()));
		if (!refundAddress.IsEmpty())
			path.Append("&refund_address=").Append(Escape(
				refundAddress.Trim()));
		if (liquidityToleranceBasisPoints is int tolerance)
			path.Append("&liquidity_tolerance_bps=").Append(
				tolerance.ToString(CultureInfo.InvariantCulture));

		return GetAsync<THORChainQuote>(_thornodeClient, _quoteLane,
			path.ToString(), cancellationToken);
	}

	public ValueTask<THORChainActionsPage> GetActionsAsync(string asset,
		string address, string transactionHash, int limit, int offset,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 50)
			throw new ArgumentOutOfRangeException(nameof(limit));
		if (offset < 0)
			throw new ArgumentOutOfRangeException(nameof(offset));

		var path = new StringBuilder("actions?type=swap&limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture))
			.Append("&offset=").Append(offset.ToString(
				CultureInfo.InvariantCulture));
		if (!asset.IsEmpty())
			path.Append("&asset=").Append(Escape(NormalizeAsset(asset)));
		if (!address.IsEmpty())
			path.Append("&address=").Append(Escape(address.Trim()));
		if (!transactionHash.IsEmpty())
			path.Append("&txid=").Append(Escape(
				transactionHash.NormalizeTransactionHash()));
		return GetAsync<THORChainActionsPage>(_midgardClient, _midgardLane,
			path.ToString(), cancellationToken);
	}

	public ValueTask<THORChainBalancesResponse> GetBalancesAsync(
		string walletAddress, CancellationToken cancellationToken)
		=> GetAsync<THORChainBalancesResponse>(_thornodeClient, _thornodeLane,
			"cosmos/bank/v1beta1/balances/" + Escape(
				walletAddress.NormalizeThorAddress()), cancellationToken);

	public ValueTask<THORChainAccountResponse> GetAccountAsync(
		string walletAddress, CancellationToken cancellationToken)
		=> GetAsync<THORChainAccountResponse>(_thornodeClient, _thornodeLane,
			"cosmos/auth/v1beta1/accounts/" + Escape(
				walletAddress.NormalizeThorAddress()), cancellationToken);

	public ValueTask<THORChainNodeInfoResponse> GetNodeInfoAsync(
		CancellationToken cancellationToken)
		=> GetAsync<THORChainNodeInfoResponse>(_thornodeClient,
			_thornodeLane, "cosmos/base/tendermint/v1beta1/node_info",
			cancellationToken);

	public ValueTask<THORChainNetwork> GetNetworkAsync(
		CancellationToken cancellationToken)
		=> GetAsync<THORChainNetwork>(_thornodeClient, _thornodeLane,
			"thorchain/network", cancellationToken);

	public ValueTask<THORChainTransactionResponse> BroadcastAsync(
		THORChainBroadcastRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return SendJsonAsync<THORChainBroadcastRequest,
			THORChainTransactionResponse>(_thornodeClient, _thornodeLane,
			HttpMethod.Post, "cosmos/tx/v1beta1/txs", request, false,
			cancellationToken);
	}

	public async ValueTask<THORChainTransactionResponse> TryGetTransactionAsync(
		string transactionHash, CancellationToken cancellationToken)
	{
		try
		{
			return await GetAsync<THORChainTransactionResponse>(
				_thornodeClient, _thornodeLane, "cosmos/tx/v1beta1/txs/" +
				Escape(transactionHash.NormalizeTransactionHash()),
				cancellationToken);
		}
		catch (THORChainApiException error)
			when (error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	private async ValueTask<TResponse> GetAsync<TResponse>(HttpClient client,
		RequestLane lane, string path, CancellationToken cancellationToken)
		=> await SendAsync<TResponse>(client, lane, HttpMethod.Get, path,
			null, true, cancellationToken);

	private async ValueTask<TResponse> SendJsonAsync<TRequest, TResponse>(
		HttpClient client, RequestLane lane, HttpMethod method, string path,
		TRequest request, bool isRetryAllowed,
		CancellationToken cancellationToken)
		=> await SendAsync<TResponse>(client, lane, method, path,
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
				throw new THORChainApiException(response.StatusCode, null,
					"THORChain response exceeds the safety limit.");
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (body.Length > _maximumResponseLength)
				throw new THORChainApiException(response.StatusCode, null,
					"THORChain response exceeds the safety limit.");
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
					? throw new THORChainApiException(response.StatusCode,
						null, "THORChain returned an empty JSON response.")
					: result;
			}
			catch (JsonException error)
			{
				throw new THORChainApiException(
					"THORChain returned malformed JSON.", error);
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

	private THORChainApiException CreateApiException(HttpStatusCode status,
		string body)
	{
		THORChainErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<THORChainErrorResponse>(body,
				_serializerSettings);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message ?? error?.Error;
		if (detail.IsEmpty())
			detail = body.Length <= 512 ? body : body[..512];
		return new(status, error?.Code,
			$"THORChain HTTP {(int)status} ({status}): {detail}");
	}

	private static HttpClient CreateClient(string endpoint, string clientId)
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri(NormalizeEndpoint(endpoint),
				UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-THORChain/1.0");
		if (!clientId.IsEmpty())
			client.DefaultRequestHeaders.TryAddWithoutValidation(
				"x-client-id", clientId.Trim());
		return client;
	}

	private static string NormalizeEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https") || !uri.UserInfo.IsEmpty() ||
			(uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback))
			throw new ArgumentException(
				"THORChain endpoint must use HTTPS, except for a local node.",
				nameof(value));
		return value.TrimEnd('/') + "/";
	}

	private static string NormalizeAsset(string value)
		=> value.ThrowIfEmpty(nameof(value)).Trim().ToUpperInvariant();

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_midgardClient.Dispose();
		_thornodeClient.Dispose();
		_midgardLane.Dispose();
		_thornodeLane.Dispose();
		_quoteLane.Dispose();
		base.DisposeManaged();
	}
}
