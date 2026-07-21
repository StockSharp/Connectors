namespace StockSharp.Anchorage.Native;

sealed class AnchorageApiException : InvalidOperationException
{
	public AnchorageApiException(HttpStatusCode statusCode,
		AnchorageErrorTypes errorType, string message)
		: base(message)
	{
		StatusCode = statusCode;
		ErrorType = errorType;
	}

	public HttpStatusCode StatusCode { get; }
	public AnchorageErrorTypes ErrorType { get; }
}

sealed class AnchorageRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 32 * 1024 * 1024;
	private static readonly TimeSpan _minimumRequestInterval =
		TimeSpan.FromMilliseconds(50);
	private readonly HttpClient _client;
	private readonly string _apiKey;
	private readonly AnchorageSigner _signer;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _lastRequestTime;
	private bool _isDisposed;

	public AnchorageRestClient(string endpoint, SecureString apiKey,
		SecureString signingKey)
	{
		endpoint = endpoint.NormalizeAnchorageEndpoint().ThrowIfEmpty(
			nameof(endpoint));
		if (apiKey.IsEmpty())
			throw new ArgumentNullException(nameof(apiKey));
		_apiKey = apiKey.UnSecure().Trim().ThrowIfEmpty(nameof(apiKey));
		if (!signingKey.IsEmpty())
			_signer = new(signingKey);
		_client = new()
		{
			BaseAddress = new(endpoint + "/", UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		_client.DefaultRequestHeaders.Accept.Add(new("application/json"));
		_client.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Anchorage/1.0");
	}

	public override string Name => "Anchorage_REST";

	public bool IsSigningAvailable => _signer is not null;

	public async ValueTask<AnchorageAssetType[]> GetAssetTypesAsync(
		CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageAssetTypesResponse>("asset-types",
			cancellationToken))?.Data ?? [];

	public async ValueTask<AnchorageTradePair[]> GetTradePairsAsync(
		CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageTradePairsResponse>("trading/pairs",
			cancellationToken))?.Data ?? [];

	public async ValueTask<AnchorageMarketDataSnapshot> GetMarketDataAsync(
		string symbol, string accountId, string subaccountId, int depth,
		CancellationToken cancellationToken)
	{
		if (depth is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(depth));
		if (!accountId.IsEmpty() && !subaccountId.IsEmpty())
			throw new ArgumentException(
				"Only one Anchorage market-data account scope may be used.");
		var path = "trading/marketdata?symbol=" + Escape(
			symbol.ThrowIfEmpty(nameof(symbol))) + "&depth=" + Format(depth);
		if (!accountId.IsEmpty())
			path += "&accountId=" + Escape(accountId);
		if (!subaccountId.IsEmpty())
			path += "&subaccountId=" + Escape(subaccountId);
		var response = await GetAsync<AnchorageMarketDataResponse>(path,
			cancellationToken);
		return response?.Data?.FirstOrDefault(item =>
			item?.Symbol.EqualsIgnoreCase(symbol) == true);
	}

	public async ValueTask<AnchorageTradingAccount[]> GetTradingAccountsAsync(
		CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageTradingAccountsResponse>("trading/accounts",
			cancellationToken))?.Data ?? [];

	public async ValueTask<AnchorageTradingBalance[]> GetTradingBalancesAsync(
		string accountId, CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageTradingBalancesResponse>(
			"trading/accounts/" + Escape(accountId.ThrowIfEmpty(
				nameof(accountId))) + "/balances", cancellationToken))?.Data ?? [];

	public ValueTask<AnchorageVault[]> GetVaultsAsync(int pageSize, int maximum,
		CancellationToken cancellationToken)
		=> GetPagesAsync<AnchorageVaultsResponse, AnchorageVault>(
			"vaults?limit=" + Format(ValidatePage(pageSize, maximum)), maximum,
			static response => response?.Data ?? [],
			static response => response?.Page?.Next, cancellationToken);

	public ValueTask<AnchorageWallet[]> GetWalletsAsync(int pageSize,
		int maximum, CancellationToken cancellationToken)
		=> GetPagesAsync<AnchorageWalletsResponse, AnchorageWallet>(
			"wallets?filterByIsArchived=false&limit=" +
				Format(ValidatePage(pageSize, maximum)), maximum,
			static response => response?.Data ?? [],
			static response => response?.Page?.Next, cancellationToken);

	public async ValueTask<AnchorageTradingOrder> PlaceImmediateOrderAsync(
		AnchorageImmediateOrderRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return (await PostAsync<AnchorageTradingOrderResponse,
			AnchorageImmediateOrderRequest>("trading/order", request, true,
			cancellationToken))?.Data;
	}

	public async ValueTask<AnchorageTradingOrder> PlaceAsyncOrderAsync(
		AnchorageAsyncOrderRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return (await PostAsync<AnchorageTradingOrderResponse,
			AnchorageAsyncOrderRequest>("trading/async-order", request, true,
			cancellationToken))?.Data;
	}

	public async ValueTask<AnchorageTradingOrder> CancelTradingOrderAsync(
		string orderId, CancellationToken cancellationToken)
	{
		var path = "trading/async-order/cancel?orderId=" + Escape(
			orderId.ThrowIfEmpty(nameof(orderId)));
		return await SendAsync<AnchorageTradingOrder>(HttpMethod.Post, path,
			[], null, true, false, cancellationToken, response =>
				JsonConvert.DeserializeObject<AnchorageTradingOrderResponse>(
					response, _settings)?.Data);
	}

	public async ValueTask<AnchorageTradingOrder> GetTradingOrderAsync(string id,
		CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageTradingOrderResponse>("trading/orders/" +
			Escape(id.ThrowIfEmpty(nameof(id))), cancellationToken))?.Data;

	public ValueTask<AnchorageTradingOrder[]> GetTradingOrdersAsync(
		DateTime? from, DateTime? to, string accountId, int maximum,
		CancellationToken cancellationToken)
	{
		ValidateMaximum(maximum);
		var path = "trading/orders?limit=" + Format(maximum.Min(100));
		if (from is DateTime start)
			path += "&startDateTime=" + Escape(start.ToAnchorageTime());
		if (to is DateTime end)
			path += "&endDateTime=" + Escape(end.ToAnchorageTime());
		if (!accountId.IsEmpty())
			path += "&accountId=" + Escape(accountId);
		return GetPagesAsync<AnchorageTradingOrdersResponse,
			AnchorageTradingOrder>(path, maximum,
			static response => response?.Data ?? [],
			static response => response?.Page?.Next, cancellationToken);
	}

	public ValueTask<AnchorageTrade[]> GetTradesAsync(DateTime? from,
		DateTime? to, string accountId, string symbol, string orderId,
		int maximum, CancellationToken cancellationToken)
	{
		ValidateMaximum(maximum);
		var path = "trading/trades?limit=" + Format(maximum.Min(100));
		if (from is DateTime start)
			path += "&startDateTime=" + Escape(start.ToAnchorageTime());
		if (to is DateTime end)
			path += "&endDateTime=" + Escape(end.ToAnchorageTime());
		if (!accountId.IsEmpty())
			path += "&accountId=" + Escape(accountId);
		if (!symbol.IsEmpty())
			path += "&tradingPair=" + Escape(symbol);
		if (!orderId.IsEmpty())
			path += "&orderId=" + Escape(orderId);
		return GetPagesAsync<AnchorageTradesResponse, AnchorageTrade>(path,
			maximum, static response => response?.Data ?? [],
			static response => response?.Page?.Next, cancellationToken);
	}

	public async ValueTask<AnchorageTransfer> CreateTransferAsync(
		AnchorageTransferRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return (await PostAsync<AnchorageTransferResponse,
			AnchorageTransferRequest>("transfers", request, true,
			cancellationToken))?.Data;
	}

	public async ValueTask<AnchorageTransfer> GetTransferAsync(string id,
		CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageTransferResponse>("transfers/" + Escape(
			id.ThrowIfEmpty(nameof(id))), cancellationToken))?.Data;

	public ValueTask<AnchorageTransfer[]> GetTransfersAsync(DateTime? from,
		DateTime? to, string vaultId, int pageSize, int maximum,
		CancellationToken cancellationToken)
	{
		var size = ValidatePage(pageSize, maximum);
		var path = "transfers?limit=" + Format(size);
		if (from is DateTime start)
			path += "&startDate=" + start.EnsureUtc().ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&endDate=" + end.EnsureUtc().ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture);
		if (!vaultId.IsEmpty())
			path += "&vaultId=" + Escape(vaultId);
		return GetPagesAsync<AnchorageTransfersResponse, AnchorageTransfer>(
			path, maximum, static response => response?.Data ?? [],
			static response => response?.Page?.Next, cancellationToken);
	}

	public async ValueTask CancelTransferAsync(string id,
		CancellationToken cancellationToken)
		=> _ = await SendAsync<AnchorageEmptyResponse>(HttpMethod.Delete,
			"transfers/" + Escape(id.ThrowIfEmpty(nameof(id))), [], null, true,
			false, cancellationToken, null);

	public async ValueTask<string> CreateWithdrawalAsync(
		AnchorageWithdrawalRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return (await PostAsync<AnchorageWithdrawalResponse,
			AnchorageWithdrawalRequest>("transactions/withdrawal", request, true,
			cancellationToken))?.Data?.Id;
	}

	public async ValueTask<string> CreateStakeAsync(
		AnchorageStakingRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return (await PostAsync<AnchorageStakingResponse,
			AnchorageStakingRequest>("transactions/stake", request, true,
			cancellationToken))?.Data?.TransactionId;
	}

	public async ValueTask<string> CreateUnstakeAsync(
		AnchorageUnstakingRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return (await PostAsync<AnchorageStakingResponse,
			AnchorageUnstakingRequest>("transactions/unstake", request, true,
			cancellationToken))?.Data?.TransactionId;
	}

	public async ValueTask<AnchorageTransaction> GetTransactionAsync(string id,
		CancellationToken cancellationToken)
		=> (await GetAsync<AnchorageTransactionResponse>("transactions/" +
			Escape(id.ThrowIfEmpty(nameof(id))), cancellationToken))?.Data;

	public ValueTask<AnchorageTransaction[]> GetTransactionsAsync(DateTime? from,
		DateTime? to, string vaultId, int pageSize, int maximum,
		CancellationToken cancellationToken)
	{
		var size = ValidatePage(pageSize, maximum);
		var path = "transactions?limit=" + Format(size);
		if (from is DateTime start)
			path += "&startDate=" + start.EnsureUtc().ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture);
		if (to is DateTime end)
			path += "&endDate=" + end.EnsureUtc().ToString("yyyy-MM-dd",
				CultureInfo.InvariantCulture);
		if (!vaultId.IsEmpty())
			path += "&vaultId=" + Escape(vaultId);
		return GetPagesAsync<AnchorageTransactionsResponse,
			AnchorageTransaction>(path, maximum,
			static response => response?.Data ?? [],
			static response => response?.Page?.Next, cancellationToken);
	}

	private async ValueTask<TItem[]> GetPagesAsync<TResponse, TItem>(
		string firstPath, int maximum, Func<TResponse, TItem[]> getItems,
		Func<TResponse, string> getNext, CancellationToken cancellationToken)
	{
		ValidateMaximum(maximum);
		ArgumentNullException.ThrowIfNull(getItems);
		ArgumentNullException.ThrowIfNull(getNext);
		var result = new List<TItem>();
		var path = firstPath.ThrowIfEmpty(nameof(firstPath));
		var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		while (result.Count < maximum && visited.Add(path))
		{
			var response = await GetAsync<TResponse>(path, cancellationToken);
			var items = getItems(response) ?? [];
			result.AddRange(items.Take(maximum - result.Count));
			var next = getNext(response);
			if (next.IsEmpty() || items.Length == 0)
				break;
			path = NormalizeNextPath(next);
		}
		return [.. result];
	}

	private ValueTask<TResponse> GetAsync<TResponse>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<TResponse>(HttpMethod.Get, path, [], null, false, true,
			cancellationToken, null);

	private ValueTask<TResponse> PostAsync<TResponse, TRequest>(string path,
		TRequest request, bool isSignatureRequired,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		var body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(request,
			_settings));
		return SendAsync<TResponse>(HttpMethod.Post, path, body,
			"application/json", isSignatureRequired, false, cancellationToken,
			null);
	}

	private async ValueTask<TResponse> SendAsync<TResponse>(HttpMethod method,
		string path, byte[] body, string contentType, bool isSignatureRequired,
		bool isRetryAllowed, CancellationToken cancellationToken,
		Func<string, TResponse> deserialize)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		if (isSignatureRequired && _signer is null)
			throw new InvalidOperationException(
				"Anchorage Ed25519 signing key is required for this operation.");
		await _gate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				await EnforceRequestIntervalAsync(cancellationToken);
				var uri = new Uri(_client.BaseAddress, path);
				using var request = new HttpRequestMessage(method, path);
				request.Headers.TryAddWithoutValidation("Api-Access-Key", _apiKey);
				if (_signer is not null)
				{
					var timestamp = checked((long)DateTime.UtcNow.ToUnix()).ToString(
							CultureInfo.InvariantCulture);
					request.Headers.TryAddWithoutValidation("Api-Timestamp",
						timestamp);
					request.Headers.TryAddWithoutValidation("Api-Signature",
						_signer.Sign(timestamp, method.Method, uri.PathAndQuery, body));
				}
				if (body.Length > 0)
					request.Content = new ByteArrayContent(body)
					{
						Headers = { ContentType = new(contentType) },
					};

				HttpResponseMessage response;
				try
				{
					response = await _client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead,
						cancellationToken);
				}
				catch (Exception error) when (isRetryAllowed && attempt < 3 &&
					!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					await DelayRetryAsync(attempt, null, cancellationToken);
					continue;
				}
				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var bytes = await ReadResponseAsync(response,
						cancellationToken);
					if (response.IsSuccessStatusCode)
					{
						if (bytes.Length == 0)
							return default;
						var json = Encoding.UTF8.GetString(bytes);
						return deserialize is null
							? JsonConvert.DeserializeObject<TResponse>(json, _settings)
							: deserialize(json);
					}
					if (isRetryAllowed && attempt < 3 &&
						IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, bytes);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async ValueTask EnforceRequestIntervalAsync(
		CancellationToken cancellationToken)
	{
		var remaining = _minimumRequestInterval -
			(DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ?? TimeSpan.FromMilliseconds(200 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromSeconds(10))
			delay = TimeSpan.FromSeconds(10);
		await Task.Delay(delay, cancellationToken);
	}

	private string NormalizeNextPath(string next)
	{
		if (!Uri.TryCreate(_client.BaseAddress, next, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps ||
			!uri.Host.Equals(_client.BaseAddress.Host,
				StringComparison.OrdinalIgnoreCase) ||
			uri.Port != _client.BaseAddress.Port)
			throw new InvalidDataException(
				"Anchorage returned an invalid pagination URL.");
		var root = _client.BaseAddress.AbsolutePath.TrimEnd('/') + "/";
		if (!uri.AbsolutePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(
				"Anchorage pagination URL is outside the configured API root.");
		return uri.AbsolutePath[root.Length..] + uri.Query;
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Anchorage response is too large ({length} bytes).");
		var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
		if (bytes.Length > _maximumResponseLength)
			throw new InvalidDataException(
				$"Anchorage response is too large ({bytes.Length} bytes).");
		return bytes;
	}

	private AnchorageApiException CreateException(HttpStatusCode statusCode,
		byte[] body)
	{
		AnchorageErrorDetails error = null;
		if (body.Length > 0)
		{
			try
			{
				error = JsonConvert.DeserializeObject<AnchorageErrorDetails>(
					Encoding.UTF8.GetString(body), _settings);
			}
			catch (JsonException)
			{
			}
		}
		var message = error?.Message;
		if (message.IsEmpty())
			message = $"Anchorage API returned HTTP {(int)statusCode} " +
				$"({statusCode}).";
		else
			message = $"Anchorage API returned HTTP {(int)statusCode}: {message}";
		return new(statusCode, error?.Type ?? AnchorageErrorTypes.Unknown,
			message);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == (HttpStatusCode)429 ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private static int ValidatePage(int pageSize, int maximum)
	{
		if (pageSize is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		ValidateMaximum(maximum);
		return pageSize.Min(maximum);
	}

	private static void ValidateMaximum(int maximum)
	{
		if (maximum is < 1 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximum));
	}

	private static string Format(int value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private static string Escape(string value)
		=> value.ThrowIfEmpty(nameof(value)).DataEscape();

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_client.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
