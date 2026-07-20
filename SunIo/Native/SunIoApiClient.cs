namespace StockSharp.SunIo.Native;

sealed class SunIoApiClient : BaseLogReceiver
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
	private readonly HttpClient _routerClient;
	private readonly HttpClient _nodeClient;
	private readonly RequestLane _dataLane =
		new(TimeSpan.FromMilliseconds(120));
	private readonly RequestLane _routerLane =
		new(TimeSpan.FromMilliseconds(300));
	private readonly RequestLane _nodeLane =
		new(TimeSpan.FromMilliseconds(120));
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
		DateParseHandling = DateParseHandling.None,
	};
	private bool _isDisposed;

	public SunIoApiClient(string dataEndpoint, string routerEndpoint,
		string nodeEndpoint, SecureString sunApiKey, SecureString tronApiKey)
	{
		_dataClient = CreateClient(dataEndpoint, "X-API-KEY",
			sunApiKey.IsEmpty() ? null : sunApiKey.UnSecure());
		_routerClient = CreateClient(routerEndpoint, null, null);
		_nodeClient = CreateClient(nodeEndpoint, "TRON-PRO-API-KEY",
			tronApiKey.IsEmpty() ? null : tronApiKey.UnSecure());
	}

	public override string Name => "SUN.io_REST";

	public async ValueTask<SunIoToken[]> GetTopTokensAsync(int pageSize,
		CancellationToken cancellationToken)
	{
		if (pageSize is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		var response = await GetAsync<SunIoResponse<
			SunIoPagedData<SunIoToken>>>(_dataClient, _dataLane,
			"apiv2/tokens?protocol=ALL&pageNo=1&pageSize=" +
			pageSize.ToString(CultureInfo.InvariantCulture) +
			"&sort=volumeUsd1d&filterBlackList=true", cancellationToken);
		return ValidateData(response, "token discovery")?.Items ?? [];
	}

	public async ValueTask<SunIoToken[]> GetTokensAsync(string[] addresses,
		CancellationToken cancellationToken)
	{
		if (addresses is not { Length: > 0 })
			throw new ArgumentException(
				"At least one token address is required.", nameof(addresses));
		var normalized = addresses.Select(static address =>
			address.NormalizeTronAddress()).Distinct(StringComparer.Ordinal)
			.ToArray();
		var result = new List<SunIoToken>(normalized.Length);
		foreach (var batch in normalized.Chunk(100))
		{
			var response = await GetAsync<SunIoResponse<
				SunIoPagedData<SunIoToken>>>(_dataClient, _dataLane,
				"apiv2/tokens?protocol=ALL&pageNo=1&pageSize=" +
				batch.Length.ToString(CultureInfo.InvariantCulture) +
				"&filterBlackList=true&tokenAddress=" + Escape(string.Join(',',
					batch)), cancellationToken);
			result.AddRange(ValidateData(response, "token lookup")?.Items ?? []);
		}
		return [.. result];
	}

	public async ValueTask<SunIoRoute[]> GetRoutesAsync(string fromToken,
		string toToken, BigInteger amount,
		CancellationToken cancellationToken)
	{
		if (amount <= 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		var path = new StringBuilder("swap/routerUniversal?fromToken=")
			.Append(Escape(fromToken.NormalizeTronAddress()))
			.Append("&toToken=").Append(Escape(
				toToken.NormalizeTronAddress()))
			.Append("&amountIn=").Append(amount.ToString(
				CultureInfo.InvariantCulture))
			.Append("&typeList=").Append(Escape(SunIoExtensions.RouteTypes));
		var response = await GetAsync<SunIoRouterResponse>(_routerClient,
			_routerLane, path.ToString(), cancellationToken);
		if (response.Code != 0)
			throw new SunIoApiException(HttpStatusCode.OK, response.Code,
				$"SUN.io route calculation failed: {response.Message}");
		return response.Routes ?? [];
	}

	public async ValueTask<SunIoPagedData<SunIoRouterTransaction>>
		GetRouterTransactionsAsync(string tokenAddress, string userAddress,
		DateTime? from, DateTime? to, int pageSize, string offset,
		CancellationToken cancellationToken)
	{
		if (pageSize is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		var path = new StringBuilder(
			"apiv2/transactions/router/scan?pageSize=")
			.Append(pageSize.ToString(CultureInfo.InvariantCulture));
		if (!tokenAddress.IsEmpty())
			path.Append("&tokenAddress=").Append(Escape(
				tokenAddress.NormalizeTronAddress()));
		if (!userAddress.IsEmpty())
			path.Append("&userAddress=").Append(Escape(
				userAddress.NormalizeTronAddress()));
		if (from is DateTime start)
			path.Append("&startTime=").Append(Escape(start.ToUniversalTime()
				.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
		if (to is DateTime end)
			path.Append("&endTime=").Append(Escape(end.ToUniversalTime()
				.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
		if (!offset.IsEmpty())
			path.Append("&offset=").Append(Escape(offset));
		var response = await GetAsync<SunIoResponse<
			SunIoPagedData<SunIoRouterTransaction>>>(_dataClient, _dataLane,
			path.ToString(), cancellationToken);
		return ValidateData(response, "router transaction scan") ?? new();
	}

	public ValueTask<SunIoBlock> GetNowBlockAsync(
		CancellationToken cancellationToken)
		=> PostAsync<SunIoBlock, SunIoEmptyRequest>(_nodeClient, _nodeLane,
			"wallet/getnowblock", new(), true, cancellationToken);

	public ValueTask<SunIoTronAccount> GetAccountAsync(string walletAddress,
		CancellationToken cancellationToken)
		=> PostAsync<SunIoTronAccount, SunIoTronAccountRequest>(_nodeClient,
			_nodeLane, "wallet/getaccount", new()
			{
				Address = walletAddress.NormalizeTronAddress(),
				IsVisible = true,
			}, true, cancellationToken);

	public async ValueTask<BigInteger> GetTokenBalanceAsync(
		string tokenAddress, string walletAddress,
		CancellationToken cancellationToken)
	{
		var response = await PostAsync<SunIoConstantContractResponse,
			SunIoConstantContractRequest>(_nodeClient, _nodeLane,
			"wallet/triggerconstantcontract", new()
			{
				OwnerAddress = walletAddress.NormalizeTronAddress(),
				ContractAddress = tokenAddress.NormalizeTronAddress(),
				FunctionSelector = "balanceOf(address)",
				Parameter = SunIoAbiEncoder.EncodeAddressParameter(walletAddress),
				IsVisible = true,
			}, true, cancellationToken);
		if (response.Result?.IsSuccess != true)
			throw CreateNodeException("TRC-20 balance query", response.Result);
		var value = response.ConstantResults?.FirstOrDefault();
		if (value.IsEmpty() || value.Length > 64 ||
			value.Any(static character => !Uri.IsHexDigit(character)))
			throw new InvalidDataException(
				"TRON returned an invalid TRC-20 balance word.");
		return new BigInteger(value.HexToByteArray(), true, true);
	}

	public ValueTask<SunIoTriggerContractResponse> TriggerSwapAsync(
		SunIoTriggerContractRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);
		return PostAsync<SunIoTriggerContractResponse,
			SunIoTriggerContractRequest>(_nodeClient, _nodeLane,
			"wallet/triggersmartcontract", request, true, cancellationToken);
	}

	public ValueTask<SunIoBroadcastResponse> BroadcastAsync(
		SunIoTronTransaction transaction,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(transaction);
		return PostAsync<SunIoBroadcastResponse, SunIoTronTransaction>(
			_nodeClient, _nodeLane, "wallet/broadcasttransaction",
			transaction, false, cancellationToken);
	}

	public async ValueTask<SunIoTransactionInfo> TryGetTransactionInfoAsync(
		string transactionHash, CancellationToken cancellationToken)
	{
		var result = await PostAsync<SunIoTransactionInfo,
			SunIoTransactionRequest>(_nodeClient, _nodeLane,
			"wallet/gettransactioninfobyid", new()
			{
				Value = transactionHash.NormalizeTransactionHash(),
			}, true, cancellationToken);
		return result.Id.IsEmpty() ? null : result;
	}

	private static T ValidateData<T>(SunIoResponse<T> response,
		string operation)
	{
		ArgumentNullException.ThrowIfNull(response);
		if (response.Code != 0)
			throw new SunIoApiException(HttpStatusCode.OK, response.Code,
				$"SUN.io {operation} failed: {response.Message}");
		return response.Data;
	}

	private async ValueTask<TResponse> GetAsync<TResponse>(HttpClient client,
		RequestLane lane, string path, CancellationToken cancellationToken)
		=> await SendAsync<TResponse>(client, lane, HttpMethod.Get, path,
			null, true, cancellationToken);

	private async ValueTask<TResponse> PostAsync<TResponse, TRequest>(
		HttpClient client, RequestLane lane, string path, TRequest request,
		bool isRetryAllowed, CancellationToken cancellationToken)
		=> await SendAsync<TResponse>(client, lane, HttpMethod.Post, path,
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
				throw new SunIoApiException(response.StatusCode, null,
					"SUN.io response exceeds the safety limit.");
			var body = await response.Content.ReadAsStringAsync(
				cancellationToken);
			if (body.Length > _maximumResponseLength)
				throw new SunIoApiException(response.StatusCode, null,
					"SUN.io response exceeds the safety limit.");
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
				throw new SunIoApiException(response.StatusCode, null,
					$"SUN.io HTTP {(int)response.StatusCode} " +
					$"({response.StatusCode}): {Limit(body)}");
			}
			try
			{
				var result = JsonConvert.DeserializeObject<TResponse>(body,
					_serializerSettings);
				return result is null
					? throw new SunIoApiException(response.StatusCode, null,
						"SUN.io returned an empty JSON response.")
					: result;
			}
			catch (JsonException error)
			{
				throw new SunIoApiException(
					"SUN.io returned malformed JSON.", error);
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

	private static SunIoApiException CreateNodeException(string operation,
		SunIoNodeResult result)
		=> new(HttpStatusCode.OK, null,
			$"TRON {operation} failed: {result?.Code} " +
			DecodeNodeMessage(result?.Message));

	public static string DecodeNodeMessage(string value)
	{
		if (value.IsEmpty())
			return string.Empty;
		try
		{
			if ((value.Length & 1) == 0 &&
				value.All(static character => Uri.IsHexDigit(character)))
				return Encoding.UTF8.GetString(value.HexToByteArray());
			return Encoding.UTF8.GetString(Convert.FromBase64String(value));
		}
		catch (FormatException)
		{
			return value;
		}
	}

	private static HttpClient CreateClient(string endpoint, string keyHeader,
		string apiKey)
	{
		var client = new HttpClient
		{
			BaseAddress = new Uri(NormalizeEndpoint(endpoint),
				UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-SUNio/1.0");
		if (!keyHeader.IsEmpty() && !apiKey.IsEmpty())
			client.DefaultRequestHeaders.TryAddWithoutValidation(keyHeader,
				apiKey.Trim());
		return client;
	}

	private static string NormalizeEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https") || !uri.UserInfo.IsEmpty() ||
			(uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback))
			throw new ArgumentException(
				"SUN.io endpoint must use HTTPS, except for a local node.",
				nameof(value));
		return value.TrimEnd('/') + "/";
	}

	private static string Escape(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)));

	private static string Limit(string value)
		=> value.IsEmpty() ? string.Empty : value.Length <= 512
			? value
			: value[..512];

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_dataClient.Dispose();
		_routerClient.Dispose();
		_nodeClient.Dispose();
		_dataLane.Dispose();
		_routerLane.Dispose();
		_nodeLane.Dispose();
		base.DisposeManaged();
	}
}

sealed class SunIoEmptyRequest
{
}
