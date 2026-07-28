namespace StockSharp.Chainflip.Native;

sealed class ChainflipHttpClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 8 * 1024 * 1024;
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

	public ChainflipHttpClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(endpoint + "/", UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Chainflip backend endpoint must be an absolute HTTP or HTTPS " +
					"URI.", nameof(endpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Chainflip-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
		_http.DefaultRequestHeaders.TryAddWithoutValidation(
			"x-client-version", "StockSharp-1.0");
	}

	public override string Name => "Chainflip_Swap_API";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<ChainflipQuote> GetQuoteAsync(
		ChainflipAsset source, ChainflipAsset destination,
		BigInteger sourceAmount, bool isVaultSwap,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		if (sourceAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(sourceAmount));
		if (source.Key.EqualsIgnoreCase(destination.Key))
			throw new ArgumentException(
				"Chainflip quote assets must be different.");
		var query = new Dictionary<string, string>
		{
			["amount"] = sourceAmount.ToString(
				CultureInfo.InvariantCulture),
			["srcChain"] = source.Chain,
			["srcAsset"] = source.Symbol,
			["destChain"] = destination.Chain,
			["destAsset"] = destination.Symbol,
			["isVaultSwap"] = isVaultSwap ? "true" : "false",
			["isOnChain"] = "false",
			["dcaV2Enabled"] = "false",
		};
		var path = "v2/quote?" + string.Join("&", query.Select(static pair =>
			Uri.EscapeDataString(pair.Key) + "=" +
			Uri.EscapeDataString(pair.Value)));
		var quotes = await SendAsync<ChainflipQuote[]>(HttpMethod.Get, path,
			null, true, cancellationToken);
		var quote = quotes?.FirstOrDefault(static item =>
			item?.Type.EqualsIgnoreCase("REGULAR") == true) ??
			throw new InvalidDataException(
				"Chainflip returned no regular quote.");
		ValidateQuote(quote, source, destination, sourceAmount, isVaultSwap);
		return quote;
	}

	public async ValueTask<ChainflipVaultResponse> BuildVaultSwapAsync(
		ChainflipQuote quote, ChainflipAsset source,
		ChainflipAsset destination, string sourceAddress,
		string destinationAddress, decimal slippageTolerance,
		int retryDurationBlocks, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(quote);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(destination);
		sourceAddress = sourceAddress.NormalizeAddress();
		destinationAddress = destinationAddress.ThrowIfEmpty(
			nameof(destinationAddress)).Trim();
		if (!source.IsEvm)
			throw new NotSupportedException(
				"Chainflip vault signing currently supports EVM source chains.");
		if (retryDurationBlocks <= 0)
			throw new ArgumentOutOfRangeException(
				nameof(retryDurationBlocks));
		var amount = quote.DepositAmount.ParseInteger();
		var minimumPrice = ChainflipExtensions.GetMinimumPriceX128(
			quote.EstimatedPrice, source, destination,
			slippageTolerance);
		var liveTolerance = quote
			.RecommendedLivePriceSlippageTolerancePercent;
		var oracleTolerance = liveTolerance is null
			? (int?)null
			: decimal.Ceiling(liveTolerance.Value * 100m).To<int>();
		var refundParameters = new JObject
		{
			["retry_duration"] = retryDurationBlocks,
			["refund_address"] = sourceAddress,
			["min_price"] = minimumPrice.ToRpcHex(),
			["max_oracle_price_slippage"] = oracleTolerance is null
				? JValue.CreateNull()
				: oracleTolerance.Value,
			["refund_ccm_metadata"] = JValue.CreateNull(),
		};
		var body = new JObject
		{
			["srcAsset"] = JObject.FromObject(source.ToRpc()),
			["destAsset"] = JObject.FromObject(destination.ToRpc()),
			["destAddress"] = destinationAddress,
			["amount"] = amount.ToString(CultureInfo.InvariantCulture),
			["commissionBps"] = 0,
			["fillOrKillParams"] = new JObject
			{
				["retryDurationBlocks"] = retryDurationBlocks,
				["refundAddress"] = sourceAddress,
				["minPriceX128"] = minimumPrice.ToString(
					CultureInfo.InvariantCulture),
				["maxOraclePriceSlippage"] = oracleTolerance is null
					? JValue.CreateNull()
					: oracleTolerance.Value,
			},
			["extraParams"] = new JObject
			{
				["chain"] = source.Chain,
				["input_amount"] = amount.ToRpcHex(),
				["refund_parameters"] = refundParameters,
			},
		};
		var response = await SendAsync<ChainflipVaultResponse>(
			HttpMethod.Post, "api/encodeVaultSwapData", body, false,
			cancellationToken);
		ValidateVaultResponse(response, source, amount);
		return response;
	}

	public async ValueTask<ChainflipSwapStatus> GetStatusAsync(string id,
		CancellationToken cancellationToken)
	{
		id = id.ThrowIfEmpty(nameof(id)).Trim();
		return await SendAsync<ChainflipSwapStatus>(HttpMethod.Get,
			"v2/swaps/" + Uri.EscapeDataString(id), null, true,
			cancellationToken);
	}

	internal static void ValidateQuote(ChainflipQuote quote,
		ChainflipAsset source, ChainflipAsset destination,
		BigInteger requestedAmount, bool isVaultSwap)
	{
		ArgumentNullException.ThrowIfNull(quote);
		if (quote.SourceAsset is null || quote.DestinationAsset is null ||
			!quote.SourceAsset.Chain.EqualsIgnoreCase(source.Chain) ||
			!quote.SourceAsset.Symbol.EqualsIgnoreCase(source.Symbol) ||
			!quote.DestinationAsset.Chain.EqualsIgnoreCase(
				destination.Chain) ||
			!quote.DestinationAsset.Symbol.EqualsIgnoreCase(
				destination.Symbol))
			throw new InvalidDataException(
				"Chainflip quote assets do not match the request.");
		if (quote.DepositAmount.ParseInteger() != requestedAmount ||
			quote.EgressAmount.ParseInteger() <= 0)
			throw new InvalidDataException(
				"Chainflip quote returned invalid swap amounts.");
		if (decimal.Parse(quote.EstimatedPrice,
			NumberStyles.Number, CultureInfo.InvariantCulture) <= 0)
			throw new InvalidDataException(
				"Chainflip quote returned a non-positive price.");
		if (quote.IsVaultSwap != isVaultSwap)
			throw new InvalidDataException(
				"Chainflip quote returned the wrong execution mode.");
	}

	internal static void ValidateVaultResponse(
		ChainflipVaultResponse response, ChainflipAsset source,
		BigInteger amount)
	{
		ArgumentNullException.ThrowIfNull(response);
		if (!response.Chain.EqualsIgnoreCase(source.Chain))
			throw new InvalidDataException(
				"Chainflip vault transaction uses the wrong source chain.");
		_ = response.To.NormalizeAddress();
		_ = response.Calldata.NormalizeData();
		var value = response.Value.ParseInteger();
		if (value < 0 || (source.IsNative && value != amount) ||
			(!source.IsNative && value != 0))
			throw new InvalidDataException(
				"Chainflip vault transaction has an invalid native value.");
		if (source.IsNative)
		{
			if (!response.SourceTokenAddress.IsEmpty())
				throw new InvalidDataException(
					"Chainflip native-asset transaction unexpectedly requires " +
						"a token approval.");
		}
		else if (response.SourceTokenAddress.IsEmpty() ||
			!response.SourceTokenAddress.NormalizeAddress().EqualsIgnoreCase(
				source.ContractAddress))
			throw new InvalidDataException(
				"Chainflip vault transaction requires an unexpected token.");
	}

	private async ValueTask<TResult> SendAsync<TResult>(HttpMethod method,
		string path, JToken body, bool isRead,
		CancellationToken cancellationToken)
	{
		path = path.ThrowIfEmpty(nameof(path)).TrimStart('/');
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, path));
			if (body is not null)
				request.Content = new StringContent(
					body.ToString(Formatting.None), Encoding.UTF8,
					"application/json");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var text = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (isRead && attempt < 3 && (response.StatusCode ==
					(HttpStatusCode)429 || (int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(
					250 * (1 << attempt)), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new ChainflipApiException(response.StatusCode,
					$"Chainflip API HTTP {(int)response.StatusCode}: " +
						Truncate(text));
			try
			{
				return JsonConvert.DeserializeObject<TResult>(text,
					_jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Chainflip API returned an unexpected response.", error);
			}
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Chainflip API response exceeds the 8 MiB safety limit.");
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
					"Chainflip API response exceeds the 8 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
	}
}
