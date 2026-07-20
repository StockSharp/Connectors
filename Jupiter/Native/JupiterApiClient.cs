namespace StockSharp.Jupiter.Native;

sealed class JupiterApiClient : BaseLogReceiver
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

	private readonly HttpClient _dataClient;
	private readonly HttpClient _perpetualClient;
	private readonly RequestLane _dataLane;
	private readonly RequestLane _perpetualLane =
		new(TimeSpan.FromMilliseconds(250));
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};
	private bool _isDisposed;

	public JupiterApiClient(string apiEndpoint, string perpetualEndpoint,
		SecureString apiKey)
	{
		var key = apiKey.IsEmpty() ? null : apiKey.UnSecure().Trim();
		apiEndpoint = NormalizeEndpoint(apiEndpoint.IsEmpty()
			? "https://api.jup.ag"
			: apiEndpoint);
		perpetualEndpoint = NormalizeEndpoint(perpetualEndpoint.IsEmpty()
			? "https://perps-api.jup.ag/v2"
			: perpetualEndpoint);

		_dataClient = CreateClient(apiEndpoint, key);
		_perpetualClient = CreateClient(perpetualEndpoint, key);
		_dataLane = new(key.IsEmpty()
			? TimeSpan.FromSeconds(2.05)
			: TimeSpan.FromSeconds(1.05));
	}

	public override string Name => "Jupiter_REST";

	public ValueTask<JupiterToken[]> GetTokensAsync(
		IEnumerable<string> mints, CancellationToken cancellationToken)
	{
		var values = (mints ?? []).Select(static mint =>
			mint.NormalizePublicKey()).Distinct(StringComparer.Ordinal).ToArray();
		if (values.Length is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(mints),
				"Jupiter token lookup requires between one and 100 mints.");
		var path = "tokens/v2/search?query=" + Escape(string.Join(",", values)) +
			"&limit=" + values.Length.ToString(CultureInfo.InvariantCulture);
		return GetAsync<JupiterToken[]>(_dataClient, _dataLane, path,
			cancellationToken);
	}

	public ValueTask<JupiterSwapOrder> GetSwapOrderAsync(
		JupiterSwapOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var path = new StringBuilder("swap/v2/order?inputMint=")
			.Append(Escape(request.InputMint.NormalizePublicKey()))
			.Append("&outputMint=")
			.Append(Escape(request.OutputMint.NormalizePublicKey()))
			.Append("&amount=").Append(Escape(request.Amount))
			.Append("&swapMode=").Append(request.SwapMode ==
				JupiterSwapModes.ExactInput ? "ExactIn" : "ExactOut");
		if (!request.Taker.IsEmpty())
			path.Append("&taker=").Append(Escape(
				request.Taker.NormalizePublicKey()));
		if (request.SlippageBasisPoints is int slippage)
			path.Append("&slippageBps=").Append(slippage.ToString(
				CultureInfo.InvariantCulture));
		return GetAsync<JupiterSwapOrder>(_dataClient, _dataLane,
			path.ToString(), cancellationToken);
	}

	public ValueTask<JupiterSpotExecuteResponse> ExecuteSwapAsync(
		JupiterSpotExecuteRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterSpotExecuteRequest,
			JupiterSpotExecuteResponse>(_dataClient, _dataLane,
			HttpMethod.Post, "swap/v2/execute", request, false,
			cancellationToken);

	public ValueTask<JupiterHoldingsResponse> GetHoldingsAsync(
		string walletAddress, CancellationToken cancellationToken)
		=> GetAsync<JupiterHoldingsResponse>(_dataClient, _dataLane,
			"ultra/v1/holdings/" + Escape(
				walletAddress.NormalizePublicKey()), cancellationToken);

	public ValueTask<JupiterSpotTradePage> GetSpotTradesAsync(
		string walletAddress, string assetMint, DateTime? from, DateTime? to,
		int limit, string offset, CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 30)
			throw new ArgumentOutOfRangeException(nameof(limit));
		var path = new StringBuilder(
			"_datapi/v1/txs/users?addresses=")
			.Append(Escape(walletAddress.NormalizePublicKey()))
			.Append("&includeCapitalSide=true&limit=")
			.Append(limit.ToString(CultureInfo.InvariantCulture));
		if (!assetMint.IsEmpty())
			path.Append("&assetId=").Append(Escape(
				assetMint.NormalizePublicKey()));
		if (from is DateTime fromTime)
			path.Append("&fromTs=").Append(fromTime.ToUnixSeconds().ToString(
					CultureInfo.InvariantCulture));
		if (to is DateTime toTime)
			path.Append("&toTs=").Append(toTime.ToUnixSeconds().ToString(
					CultureInfo.InvariantCulture));
		if (!offset.IsEmpty())
			path.Append("&offset=").Append(Escape(offset));
		return GetAsync<JupiterSpotTradePage>(_dataClient, _dataLane,
			path.ToString(), cancellationToken);
	}

	public ValueTask<JupiterPerpetualMarketStats> GetPerpetualStatsAsync(
		string mint, CancellationToken cancellationToken)
		=> GetAsync<JupiterPerpetualMarketStats>(_perpetualClient,
			_perpetualLane, "market-stats?mint=" + Escape(
				mint.NormalizePublicKey()), cancellationToken);

	public ValueTask<JupiterPerpetualPositionPage> GetPositionsAsync(
		string walletAddress, CancellationToken cancellationToken)
		=> GetAsync<JupiterPerpetualPositionPage>(_perpetualClient,
			_perpetualLane, "positions?walletAddress=" + Escape(
				walletAddress.NormalizePublicKey()), cancellationToken);

	public ValueTask<JupiterPerpetualLimitOrderPage> GetLimitOrdersAsync(
		string walletAddress, CancellationToken cancellationToken)
		=> GetAsync<JupiterPerpetualLimitOrderPage>(_perpetualClient,
			_perpetualLane, "orders/limit?walletAddress=" + Escape(
				walletAddress.NormalizePublicKey()), cancellationToken);

	public ValueTask<JupiterPerpetualTradePage> GetPerpetualTradesAsync(
		string walletAddress, int start, int end, DateTime? from,
		DateTime? to, CancellationToken cancellationToken)
	{
		if (start < 0 || end <= start || end - start > 1000)
			throw new ArgumentOutOfRangeException(nameof(end));
		var path = new StringBuilder("trades?walletAddress=")
			.Append(Escape(walletAddress.NormalizePublicKey()))
			.Append("&start=").Append(start.ToString(CultureInfo.InvariantCulture))
			.Append("&end=").Append(end.ToString(CultureInfo.InvariantCulture));
		if (from is DateTime fromTime)
			path.Append("&createdAtAfter=").Append(
				fromTime.ToUnixSeconds().ToString(
					CultureInfo.InvariantCulture));
		if (to is DateTime toTime)
			path.Append("&createdAtBefore=").Append(
				toTime.ToUnixSeconds().ToString(
					CultureInfo.InvariantCulture));
		return GetAsync<JupiterPerpetualTradePage>(_perpetualClient,
			_perpetualLane, path.ToString(), cancellationToken);
	}

	public ValueTask<JupiterPerpetualIncreaseResponse>
		CreatePositionAsync(JupiterPerpetualIncreaseRequest request,
			CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualIncreaseRequest,
			JupiterPerpetualIncreaseResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Post, "positions/increase", request,
			true, cancellationToken);

	public ValueTask<JupiterPerpetualDecreaseResponse>
		DecreasePositionAsync(JupiterPerpetualDecreaseRequest request,
			CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualDecreaseRequest,
			JupiterPerpetualDecreaseResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Post, "positions/decrease", request,
			true, cancellationToken);

	public ValueTask<JupiterPerpetualLimitResponse> CreateLimitOrderAsync(
		JupiterPerpetualLimitRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualLimitRequest,
			JupiterPerpetualLimitResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Post, "orders/limit", request, true,
			cancellationToken);

	public ValueTask<JupiterPerpetualLimitResponse> UpdateLimitOrderAsync(
		JupiterPerpetualUpdateRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualUpdateRequest,
			JupiterPerpetualLimitResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Patch, "orders/limit", request, true,
			cancellationToken);

	public ValueTask<JupiterPerpetualCancelResponse> CancelLimitOrderAsync(
		JupiterPerpetualCancelRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualCancelRequest,
			JupiterPerpetualCancelResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Delete, "orders/limit", request, true,
			cancellationToken);

	public ValueTask<JupiterPerpetualTriggerResponse> CreateTriggerAsync(
		JupiterPerpetualTriggerRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualTriggerRequest,
			JupiterPerpetualTriggerResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Post, "tpsl", request, true,
			cancellationToken);

	public ValueTask<JupiterPerpetualTriggerResponse> UpdateTriggerAsync(
		JupiterPerpetualUpdateRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualUpdateRequest,
			JupiterPerpetualTriggerResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Patch, "tpsl", request, true,
			cancellationToken);

	public ValueTask<JupiterPerpetualCancelResponse> CancelTriggerAsync(
		JupiterPerpetualCancelRequest request,
		CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualCancelRequest,
			JupiterPerpetualCancelResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Delete, "tpsl", request, true,
			cancellationToken);

	public ValueTask<JupiterPerpetualExecuteResponse>
		ExecutePerpetualTransactionAsync(
			JupiterPerpetualExecuteRequest request,
			CancellationToken cancellationToken)
		=> SendJsonAsync<JupiterPerpetualExecuteRequest,
			JupiterPerpetualExecuteResponse>(_perpetualClient,
			_perpetualLane, HttpMethod.Post, "transaction/execute", request,
			false, cancellationToken);

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
				throw new JupiterApiException(
					"Jupiter response exceeds the configured safety limit.");
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (body.Length > _maximumResponseLength)
				throw new JupiterApiException(
					"Jupiter response exceeds the configured safety limit.");
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
					? throw new JupiterApiException(
						"Jupiter returned an empty JSON response.")
					: result;
			}
			catch (JsonException error)
			{
				throw new JupiterApiException(
					"Jupiter returned malformed JSON.", error);
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

	private JupiterApiException CreateApiException(HttpStatusCode status,
		string body)
	{
		JupiterErrorResponse error = null;
		try
		{
			error = JsonConvert.DeserializeObject<JupiterErrorResponse>(body,
				_serializerSettings);
		}
		catch (JsonException)
		{
		}
		var message = error?.ErrorMessage ?? error?.Error ?? error?.Message;
		if (message.IsEmpty())
			message = body.Length <= 512 ? body : body[..512];
		return new JupiterApiException(
			$"Jupiter HTTP {(int)status} ({status}): {message}");
	}

	private static HttpClient CreateClient(string endpoint, string apiKey)
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri(endpoint, UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Jupiter/1.0");
		client.DefaultRequestHeaders.TryAddWithoutValidation(
			"x-client-platform", "stocksharp.connector");
		if (!apiKey.IsEmpty())
			client.DefaultRequestHeaders.TryAddWithoutValidation(
				"x-api-key", apiKey);
		return client;
	}

	private static string NormalizeEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps || !uri.UserInfo.IsEmpty())
			throw new ArgumentException(
				"Jupiter endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return value.TrimEnd('/') + "/";
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_dataClient.Dispose();
		_perpetualClient.Dispose();
		_dataLane.Dispose();
		_perpetualLane.Dispose();
		base.DisposeManaged();
	}
}
