namespace StockSharp.Cetus.Native;

sealed class CetusApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 1024 * 1024;
	private const int _routerVersion = 1_010_601;

	private readonly HttpClient _httpClient;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _nextRequestTime;
	private bool _isDisposed;

	public CetusApiClient(string endpoint)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_httpClient = new()
		{
			BaseAddress = new Uri(endpoint + "/", UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Cetus/1.0");
	}

	public override string Name => "Cetus_Router";

	public ValueTask<CetusQuote> GetExactInputQuoteAsync(CetusMarket market,
		string inputCoinType, string outputCoinType, ulong amount,
		CancellationToken cancellationToken)
		=> GetQuoteAsync(market, inputCoinType, outputCoinType, amount,
			CetusSwapKinds.ExactInput, cancellationToken);

	public ValueTask<CetusQuote> GetExactOutputQuoteAsync(CetusMarket market,
		string inputCoinType, string outputCoinType, ulong amount,
		CancellationToken cancellationToken)
		=> GetQuoteAsync(market, inputCoinType, outputCoinType, amount,
			CetusSwapKinds.ExactOutput, cancellationToken);

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_httpClient.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask<CetusQuote> GetQuoteAsync(CetusMarket market,
		string inputCoinType, string outputCoinType, ulong amount,
		CetusSwapKinds kind, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		ArgumentNullException.ThrowIfNull(market);
		inputCoinType = inputCoinType.NormalizeCoinType();
		outputCoinType = outputCoinType.NormalizeCoinType();
		if (inputCoinType == outputCoinType)
			throw new ArgumentException(
				"Cetus quote coin types must differ.", nameof(outputCoinType));
		if (amount == 0)
			throw new ArgumentOutOfRangeException(nameof(amount));
		if (!System.Enum.IsDefined(kind))
			throw new ArgumentOutOfRangeException(nameof(kind));

		var request = "find_routes?from=" + Uri.EscapeDataString(inputCoinType) +
			"&target=" + Uri.EscapeDataString(outputCoinType) +
			"&amount=" + amount.ToString(CultureInfo.InvariantCulture) +
			"&by_amount_in=" + (kind == CetusSwapKinds.ExactInput
				? "true"
				: "false") +
			"&providers=CETUS&depth=1&split_count=1&v=" +
			_routerVersion.ToString(CultureInfo.InvariantCulture);
		var response = await GetAsync<CetusQuoteData>(request,
			cancellationToken);
		var data = response.Data ?? throw new InvalidDataException(
			"Cetus router returned no quote data.");
		if (data.Paths is not { Length: 1 })
			throw new InvalidDataException(
				"Cetus router did not return one direct pool path.");
		var path = data.Paths[0] ?? throw new InvalidDataException(
			"Cetus router returned an empty path.");
		if (!System.Enum.IsDefined(path.Provider) ||
			path.Provider != CetusProviders.Cetus)
			throw new InvalidDataException(
				"Cetus router returned an unsupported provider.");
		var poolId = path.PoolId.NormalizeSuiAddress();
		if (poolId != market.PoolId)
			throw new InvalidDataException(
				$"Cetus router selected pool '{poolId}' instead of configured " +
				$"pool '{market.PoolId}'.");
		if (path.InputCoinType.NormalizeCoinType() != inputCoinType ||
			path.OutputCoinType.NormalizeCoinType() != outputCoinType)
			throw new InvalidDataException(
				"Cetus router returned a path for different coin types.");
		var expectedDirection = inputCoinType == market.CoinA.CoinType;
		if (inputCoinType != market.CoinA.CoinType &&
			inputCoinType != market.CoinB.CoinType)
			throw new InvalidOperationException(
				"Cetus quote input is not part of the configured pool.");
		if (path.IsAToB != expectedDirection)
			throw new InvalidDataException(
				"Cetus router returned an unexpected swap direction.");
		if (path.InputAmount == 0 || path.OutputAmount == 0 ||
			path.InputAmount != data.InputAmount ||
			path.OutputAmount != data.OutputAmount)
			throw new InvalidDataException(
				"Cetus router returned inconsistent quote amounts.");
		if (kind == CetusSwapKinds.ExactInput &&
			path.InputAmount != amount || kind == CetusSwapKinds.ExactOutput &&
			path.OutputAmount != amount)
			throw new InvalidDataException(
				"Cetus router did not preserve the specified exact amount.");
		if (!decimal.TryParse(path.FeeRate, NumberStyles.Float,
			CultureInfo.InvariantCulture, out var feeRate) ||
			feeRate is < 0 or >= 1)
			throw new InvalidDataException(
				$"Cetus router returned invalid fee rate '{path.FeeRate}'.");
		return new()
		{
			RequestId = data.RequestId.ThrowIfEmpty(nameof(data.RequestId)),
			Kind = kind,
			PoolId = poolId,
			InputCoinType = inputCoinType,
			OutputCoinType = outputCoinType,
			InputAmount = path.InputAmount,
			OutputAmount = path.OutputAmount,
			IsAToB = path.IsAToB,
			FeeRate = feeRate,
		};
	}

	private async ValueTask<CetusApiResponse<TData>> GetAsync<TData>(
		string request, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRateLimitAsync(cancellationToken);
			using var response = await _httpClient.GetAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
					HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)),
					cancellationToken);
				continue;
			}
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new CetusApiException(response.StatusCode,
					$"Cetus router request failed: {Limit(body, 1024)}");
			CetusApiResponse<TData> envelope;
			try
			{
				envelope = JsonConvert.DeserializeObject<
					CetusApiResponse<TData>>(body, _serializerSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Cetus router returned malformed JSON.", error);
			}
			if (envelope is null)
				throw new InvalidDataException(
					"Cetus router returned an empty response.");
			if (envelope.Code != 200)
				throw new InvalidOperationException(
					$"Cetus router rejected the request ({envelope.Code}): " +
					$"{envelope.Message}");
			return envelope;
		}
	}

	private async ValueTask WaitForRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _requestGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme != Uri.UriSchemeHttps)
			throw new ArgumentException(
				"Cetus router endpoint must use HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"Cetus router response exceeds the safety limit.");
		await using var source = await content.ReadAsStreamAsync(
			cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseLength)
				throw new InvalidDataException(
					"Cetus router response exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
