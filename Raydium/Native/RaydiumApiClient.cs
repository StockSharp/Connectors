namespace StockSharp.Raydium.Native;

sealed class RaydiumApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _apiClient;
	private readonly HttpClient _tradeClient;
	private readonly SemaphoreSlim _quoteGate = new(1, 1);
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _nextQuoteTime;
	private bool _isDisposed;

	public RaydiumApiClient(string apiEndpoint, string tradeEndpoint)
	{
		_apiClient = CreateClient(apiEndpoint, "StockSharp-Raydium/1.0");
		_tradeClient = CreateClient(tradeEndpoint, "StockSharp-Raydium/1.0");
	}

	public override string Name => "Raydium_HTTP_API";

	public async ValueTask<RaydiumApiPool[]> GetPoolsAsync(int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum is < 1 or > 100)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var path = "pools/info/list?poolType=all&poolSortField=volume24h" +
			"&sortType=desc&pageSize=" + maximum.ToString(
				CultureInfo.InvariantCulture) + "&page=1";
		var response = await GetAsync<RaydiumApiResponse<RaydiumApiPoolPage>>(
			_apiClient, path, false, cancellationToken);
		EnsureSuccess(response, path);
		return response.Data?.Pools?.Where(IsValidPool).Take(maximum)
			.ToArray() ?? [];
	}

	public async ValueTask<RaydiumApiPool[]> GetPoolsByIdsAsync(
		IEnumerable<string> poolAddresses,
		CancellationToken cancellationToken)
	{
		var addresses = NormalizeAddresses(poolAddresses);
		if (addresses.Length == 0)
			return [];
		var path = "pools/info/ids?ids=" + Uri.EscapeDataString(
			string.Join(',', addresses));
		var response = await GetAsync<RaydiumApiResponse<RaydiumApiPool[]>>(
			_apiClient, path, false, cancellationToken);
		EnsureSuccess(response, path);
		return response.Data?.Where(IsValidPool).ToArray() ?? [];
	}

	public async ValueTask<RaydiumApiPoolKeys[]> GetPoolKeysAsync(
		IEnumerable<string> poolAddresses,
		CancellationToken cancellationToken)
	{
		var addresses = NormalizeAddresses(poolAddresses);
		if (addresses.Length == 0)
			return [];
		var path = "pools/key/ids?ids=" + Uri.EscapeDataString(
			string.Join(',', addresses));
		var response = await GetAsync<RaydiumApiResponse<RaydiumApiPoolKeys[]>>(
			_apiClient, path, false, cancellationToken);
		EnsureSuccess(response, path);
		return response.Data?.Where(static keys => keys is not null &&
			!keys.Id.IsEmpty() && !keys.ProgramId.IsEmpty() &&
			keys.MintA is not null && keys.MintB is not null &&
			keys.Vault is not null && !keys.Vault.A.IsEmpty() &&
			!keys.Vault.B.IsEmpty()).ToArray() ?? [];
	}

	public async ValueTask<RaydiumQuote> GetQuoteAsync(RaydiumMarket market,
		Sides side, BigInteger baseAmount, int slippageBasisPoints,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		if (baseAmount <= 0)
			throw new ArgumentOutOfRangeException(nameof(baseAmount));
		if (slippageBasisPoints is < 1 or > 5000)
			throw new ArgumentOutOfRangeException(nameof(slippageBasisPoints));
		var isSell = side == Sides.Sell;
		var inputMint = isSell ? market.TokenA.Mint : market.TokenB.Mint;
		var outputMint = isSell ? market.TokenB.Mint : market.TokenA.Mint;
		var method = isSell ? "swap-base-in" : "swap-base-out";
		var path = "compute/" + method + "?inputMint=" +
			Uri.EscapeDataString(inputMint) + "&outputMint=" +
			Uri.EscapeDataString(outputMint) + "&amount=" +
			baseAmount.ToString(CultureInfo.InvariantCulture) +
			"&slippageBps=" + slippageBasisPoints.ToString(
				CultureInfo.InvariantCulture) + "&txVersion=" +
			RaydiumTransactionVersions.V0;
		var response = await GetAsync<
			RaydiumApiResponse<RaydiumSwapQuoteData>>(_tradeClient, path, true,
			cancellationToken);
		EnsureSuccess(response, path);
		ValidateQuote(response, market, side, baseAmount,
			slippageBasisPoints);
		return new()
		{
			Side = side,
			Market = market,
			Response = response,
		};
	}

	public async ValueTask<RaydiumBuiltTransaction[]> BuildSwapAsync(
		RaydiumQuote quote, string wallet, long computeUnitPrice,
		bool isNativeSolUsed, string inputAccount, string outputAccount,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(quote);
		wallet = wallet.NormalizePublicKey();
		if (computeUnitPrice < 0)
			throw new ArgumentOutOfRangeException(nameof(computeUnitPrice));
		var data = quote.Data;
		var isInputSol = isNativeSolUsed && string.Equals(data.InputMint,
			RaydiumExtensions.WrappedSolMint, StringComparison.Ordinal);
		var isOutputSol = isNativeSolUsed && string.Equals(data.OutputMint,
			RaydiumExtensions.WrappedSolMint, StringComparison.Ordinal);
		if (!isInputSol)
			inputAccount = inputAccount.NormalizePublicKey();
		if (!isOutputSol && !outputAccount.IsEmpty())
			outputAccount = outputAccount.NormalizePublicKey();
		var request = new RaydiumBuildSwapRequest
		{
			ComputeUnitPriceMicroLamports = computeUnitPrice.ToString(
				CultureInfo.InvariantCulture),
			SwapResponse = quote.Response,
			Wallet = wallet,
			IsSolWrapped = isInputSol,
			IsSolUnwrapped = isOutputSol,
			InputAccount = isInputSol ? null : inputAccount,
			OutputAccount = isOutputSol ? null : outputAccount,
		};
		var method = data.SwapType == RaydiumSwapTypes.BaseIn
			? "swap-base-in"
			: "swap-base-out";
		var path = "transaction/" + method;
		var response = await PostAsync<RaydiumBuildSwapRequest,
			RaydiumApiResponse<RaydiumBuiltTransaction[]>>(_tradeClient, path,
			request, cancellationToken);
		EnsureSuccess(response, path);
		var transactions = response.Data?.Where(static item => item is not null &&
			!item.Transaction.IsEmpty()).ToArray() ?? [];
		if (transactions.Length == 0)
			throw new InvalidDataException(
				"Raydium Transaction API returned no serialized transactions.");
		return transactions;
	}

	public async ValueTask<long> GetPriorityFeeAsync(
		RaydiumPriorityFeeLevels level, CancellationToken cancellationToken)
	{
		const string path = "main/auto-fee";
		var response = await GetAsync<RaydiumApiResponse<RaydiumPriorityFees>>(
			_apiClient, path, false, cancellationToken);
		EnsureSuccess(response, path);
		var fees = response.Data?.Default ?? throw new InvalidDataException(
			"Raydium API returned no automatic priority fees.");
		var value = level switch
		{
			RaydiumPriorityFeeLevels.Medium => fees.Medium,
			RaydiumPriorityFeeLevels.High => fees.High,
			RaydiumPriorityFeeLevels.VeryHigh => fees.VeryHigh,
			_ => throw new ArgumentOutOfRangeException(nameof(level), level,
				"Unsupported Raydium priority-fee level."),
		};
		if (value < 0)
			throw new InvalidDataException(
				"Raydium API returned a negative priority fee.");
		return value;
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_apiClient.Dispose();
		_tradeClient.Dispose();
		_quoteGate.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask<TResult> GetAsync<TResult>(HttpClient client,
		string path, bool isQuote, CancellationToken cancellationToken)
	{
		if (isQuote)
			await WaitForQuoteLimitAsync(cancellationToken);
		return await SendAsync<TResult>(client, HttpMethod.Get, path, null,
			cancellationToken);
	}

	private ValueTask<TResult> PostAsync<TRequest, TResult>(HttpClient client,
		string path, TRequest request, CancellationToken cancellationToken)
		=> SendAsync<TResult>(client, HttpMethod.Post, path,
			JsonConvert.SerializeObject(request, _serializerSettings),
			cancellationToken);

	private async ValueTask<TResult> SendAsync<TResult>(HttpClient client,
		HttpMethod method, string path, string requestBody,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		for (var attempt = 0; ; attempt++)
		{
			using var request = new HttpRequestMessage(method, path);
			if (requestBody is not null)
				request.Content = new StringContent(requestBody, Encoding.UTF8,
					"application/json");
			using var response = await client.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			if (attempt < 2 && (response.StatusCode ==
					HttpStatusCode.TooManyRequests ||
				(int)response.StatusCode >= 500))
			{
				var delay = response.Headers.RetryAfter?.Delta ??
					TimeSpan.FromMilliseconds(300 * (attempt + 1));
				await Task.Delay(delay.Min(TimeSpan.FromSeconds(5)),
					cancellationToken);
				continue;
			}
			if (response.Content.Headers.ContentLength is long length &&
				length > _maximumResponseLength)
				throw new InvalidDataException(
					"Raydium API response exceeds the safety limit.");
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new RaydiumApiException(response.StatusCode,
					$"Raydium API request '{path}' failed: {Limit(body, 1024)}");
			try
			{
				return JsonConvert.DeserializeObject<TResult>(body,
					_serializerSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Raydium API returned malformed JSON for '{path}'.", error);
			}
		}
	}

	private async ValueTask WaitForQuoteLimitAsync(
		CancellationToken cancellationToken)
	{
		await _quoteGate.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextQuoteTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextQuoteTime = DateTime.UtcNow + TimeSpan.FromMilliseconds(500);
		}
		finally
		{
			_quoteGate.Release();
		}
	}

	private static void EnsureSuccess<TResult>(
		RaydiumApiResponse<TResult> response, string path)
	{
		if (response is null)
			throw new InvalidDataException(
				$"Raydium API returned an empty response for '{path}'.");
		if (!response.IsSuccessful)
			throw new InvalidOperationException(
				$"Raydium API request '{path}' failed" +
				(response.Error is null ? string.Empty :
					$" ({response.Error.Code})") + ": " +
				(response.Error?.Message ?? response.Message ?? "unknown error"));
	}

	private static void ValidateQuote(
		RaydiumApiResponse<RaydiumSwapQuoteData> response,
		RaydiumMarket market, Sides side, BigInteger baseAmount,
		int slippageBasisPoints)
	{
		var data = response.Data ?? throw new InvalidDataException(
			"Raydium Trade API returned no quote data.");
		var isSell = side == Sides.Sell;
		var expectedInput = isSell ? market.TokenA.Mint : market.TokenB.Mint;
		var expectedOutput = isSell ? market.TokenB.Mint : market.TokenA.Mint;
		if (!string.Equals(data.InputMint, expectedInput,
				StringComparison.Ordinal) ||
			!string.Equals(data.OutputMint, expectedOutput,
				StringComparison.Ordinal) ||
			data.SwapType != (isSell ? RaydiumSwapTypes.BaseIn :
				RaydiumSwapTypes.BaseOut) ||
			data.SlippageBasisPoints != slippageBasisPoints ||
			data.RoutePlan is not { Length: > 0 } ||
			!BigInteger.TryParse(data.InputAmount, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var input) || input <= 0 ||
			!BigInteger.TryParse(data.OutputAmount, NumberStyles.Integer,
				CultureInfo.InvariantCulture, out var output) || output <= 0 ||
			(isSell && input != baseAmount) || (!isSell && output != baseAmount))
			throw new InvalidDataException(
				"Raydium Trade API returned an inconsistent quote.");
		if (data.RoutePlan.Any(static route =>
				route is null || route.PoolId.IsEmpty() ||
				route.InputMint.IsEmpty() || route.OutputMint.IsEmpty()) ||
			!string.Equals(data.RoutePlan[0].InputMint, expectedInput,
				StringComparison.Ordinal) ||
			!string.Equals(data.RoutePlan[^1].OutputMint, expectedOutput,
				StringComparison.Ordinal))
			throw new InvalidDataException(
				"Raydium Trade API returned an invalid route plan.");
		for (var index = 1; index < data.RoutePlan.Length; index++)
			if (!string.Equals(data.RoutePlan[index - 1].OutputMint,
				data.RoutePlan[index].InputMint, StringComparison.Ordinal))
				throw new InvalidDataException(
					"Raydium Trade API returned a discontinuous route plan.");
	}

	private static bool IsValidPool(RaydiumApiPool pool)
		=> pool is not null && !pool.Id.IsEmpty() && !pool.ProgramId.IsEmpty() &&
			pool.MintA is not null && pool.MintB is not null &&
			!pool.MintA.Address.IsEmpty() && !pool.MintB.Address.IsEmpty() &&
			!pool.MintA.ProgramId.IsEmpty() && !pool.MintB.ProgramId.IsEmpty();

	private static string[] NormalizeAddresses(
		IEnumerable<string> poolAddresses)
	{
		var addresses = (poolAddresses ?? []).Select(static address =>
			address.NormalizePublicKey()).Distinct(StringComparer.Ordinal)
			.ToArray();
		if (addresses.Length > 100)
			throw new ArgumentOutOfRangeException(nameof(poolAddresses),
				"Raydium bulk pool endpoints accept at most 100 addresses.");
		return addresses;
	}

	private static HttpClient CreateClient(string endpoint, string userAgent)
	{
		endpoint = NormalizeEndpoint(endpoint);
		var client = new HttpClient
		{
			BaseAddress = new Uri(endpoint + '/', UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
		return client;
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Raydium endpoint must use HTTP or HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum
			? value
			: value[..maximum];

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
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
					"Raydium API response exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
