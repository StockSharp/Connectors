namespace StockSharp.SynFutures.Native;

sealed class SynFuturesApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private const string _alphabet =
		"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _settings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;

	public SynFuturesApiClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).TrimEnd('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"SynFutures API endpoint must use HTTP or HTTPS.",
				nameof(endpoint));
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip |
				DecompressionMethods.Deflate,
		})
		{
			BaseAddress = uri,
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"Mozilla/5.0 (compatible; StockSharp-SynFutures/1.0)");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
		_http.DefaultRequestHeaders.TryAddWithoutValidation("Origin",
			"https://app.synfutures.com");
	}

	public override string Name => "SynFutures_REST";

	public ValueTask<SynFuturesMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> GetAsync<SynFuturesMarket[]>("/v4/public/market/marketList",
			[new("chainId", SynFuturesExtensions.ChainId.ToString(
				CultureInfo.InvariantCulture))], cancellationToken);

	public ValueTask<SynFuturesDepthSteps> GetOrderBookAsync(
		SynFuturesMarket market, CancellationToken cancellationToken)
		=> GetAsync<SynFuturesDepthSteps>("/v4/public/market/orderBook",
			PairParameters(market, "address"), cancellationToken);

	public ValueTask<SynFuturesCandle[]> GetCandlesAsync(
		SynFuturesMarket market, TimeSpan timeFrame, DateTime to, int limit,
		CancellationToken cancellationToken)
	{
		if (limit is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(limit));
		return GetAsync<SynFuturesCandle[]>("/v4/public/market/kline",
		[
			new("chainId", SynFuturesExtensions.ChainId.ToString(
				CultureInfo.InvariantCulture)),
			new("endTime", to.EnsureUtc().ToUnix().ToString(
				CultureInfo.InvariantCulture)),
			new("expiry", market.Expiry.ToString(CultureInfo.InvariantCulture)),
			new("instrument", market.InstrumentAddress.NormalizeAddress()),
			new("interval", timeFrame.ToApiInterval()),
			new("limit", limit.ToString(CultureInfo.InvariantCulture)),
		], cancellationToken);
	}

	public ValueTask<SynFuturesPortfolioData> GetPortfolioAsync(string wallet,
		CancellationToken cancellationToken)
		=> GetAsync<SynFuturesPortfolioData>("/v4/public/market/portfolio",
			WalletParameters(wallet), cancellationToken);

	public ValueTask<SynFuturesGateData> GetGateBalancesAsync(string wallet,
		CancellationToken cancellationToken)
		=> GetAsync<SynFuturesGateData>(
			"/v4/public/market/user/gateValue", WalletParameters(wallet),
			cancellationToken);

	public ValueTask<SynFuturesHistoryPage<SynFuturesTrade>> GetTradeHistoryAsync(
		string wallet, int page, int size, CancellationToken cancellationToken)
		=> GetAsync<SynFuturesHistoryPage<SynFuturesTrade>>(
			"/v4/public/history/trade", HistoryParameters(wallet, page, size),
			cancellationToken);

	public ValueTask<SynFuturesHistoryPage<SynFuturesOrderHistory>>
		GetOrderHistoryAsync(string wallet, int page, int size,
		CancellationToken cancellationToken)
		=> GetAsync<SynFuturesHistoryPage<SynFuturesOrderHistory>>(
			"/v4/public/history/order", HistoryParameters(wallet, page, size),
			cancellationToken);

	public ValueTask<SynFuturesQuotation> InquireAsync(
		SynFuturesMarket market, BigInteger signedSize,
		CancellationToken cancellationToken)
		=> GetAsync<SynFuturesQuotation>("/v4/public/market/inquire",
		[
			new("chainId", SynFuturesExtensions.ChainId.ToString(
				CultureInfo.InvariantCulture)),
			new("expiry", market.Expiry.ToString(CultureInfo.InvariantCulture)),
			new("instrument", market.InstrumentAddress.NormalizeAddress()),
			new("size", signedSize.ToString(CultureInfo.InvariantCulture)),
		], cancellationToken);

	private async ValueTask<T> GetAsync<T>(string path,
		SynFuturesQueryParameter[] parameters,
		CancellationToken cancellationToken)
	{
		var uri = BuildUri(path, parameters);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			var signed = Sign(uri);
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.TryAddWithoutValidation("X-Api-Nonce", signed.Nonce);
			request.Headers.TryAddWithoutValidation("X-Api-Sign",
				signed.Signature);
			request.Headers.TryAddWithoutValidation("X-Api-Ts",
				signed.Timestamp.ToString(CultureInfo.InvariantCulture));
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (attempt < 3 && (response.StatusCode == (HttpStatusCode)429 ||
				(int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250 * (1 << attempt)),
					cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					"SynFutures API HTTP " + (int)response.StatusCode + ": " +
					Truncate(body));
			SynFuturesApiResponse<T> result;
			try
			{
				result = JsonConvert.DeserializeObject<SynFuturesApiResponse<T>>(
					body, _settings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"SynFutures API returned an unexpected response shape.", error);
			}
			if (result is null)
				throw new InvalidDataException(
					"SynFutures API returned an empty response.");
			if (result.Code != 0)
				throw new InvalidOperationException(
					"SynFutures API " + result.Code + ": " +
					(result.Message.IsEmpty() ? "request rejected" : result.Message));
			return result.Data;
		}
	}

	private (string Nonce, string Signature, long Timestamp) Sign(string uri)
	{
		var random = RandomNumberGenerator.GetBytes(96);
		var nonce = new string(random.Select(static value =>
			_alphabet[value % _alphabet.Length]).Take(48).ToArray());
		var now = DateTime.UtcNow;
		var timestamp = (now - DateTime.UnixEpoch).Ticks /
			TimeSpan.TicksPerMillisecond;
		var keccak = new Sha3Keccack();
		var hash = keccak.CalculateHash(Encoding.UTF8.GetBytes(nonce));
		var plain = JsonConvert.SerializeObject(new SynFuturesSigningPayload
		{
			Uri = uri,
			Nonce = nonce,
			Timestamp = timestamp,
		}, _settings);
		byte[] cipher;
		using (var aes = Aes.Create())
		{
			aes.Key = hash;
			aes.IV = hash[^16..];
			aes.Mode = CipherMode.CBC;
			aes.Padding = PaddingMode.PKCS7;
			using var encryptor = aes.CreateEncryptor();
			var bytes = Encoding.UTF8.GetBytes(plain);
			cipher = encryptor.TransformFinalBlock(bytes, 0, bytes.Length);
		}
		var first = Convert.ToBase64String(cipher);
		var second = Convert.ToBase64String(Encoding.UTF8.GetBytes(first));
		var signature = Convert.ToHexString(keccak.CalculateHash(
			Encoding.UTF8.GetBytes(second))).ToLowerInvariant();
		return (nonce, signature, timestamp);
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(25);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static SynFuturesQueryParameter[] PairParameters(
		SynFuturesMarket market, string addressName)
	{
		ArgumentNullException.ThrowIfNull(market);
		return
		[
			new(addressName, market.InstrumentAddress.NormalizeAddress()),
			new("chainId", SynFuturesExtensions.ChainId.ToString(
				CultureInfo.InvariantCulture)),
			new("expiry", market.Expiry.ToString(CultureInfo.InvariantCulture)),
		];
	}

	private static SynFuturesQueryParameter[] WalletParameters(string wallet)
		=>
		[
			new("chainId", SynFuturesExtensions.ChainId.ToString(
				CultureInfo.InvariantCulture)),
			new("userAddress", wallet.NormalizeAddress()),
		];

	private static SynFuturesQueryParameter[] HistoryParameters(string wallet,
		int page, int size)
	{
		if (page < 1)
			throw new ArgumentOutOfRangeException(nameof(page));
		if (size is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(size));
		return
		[
			new("chainId", SynFuturesExtensions.ChainId.ToString(
				CultureInfo.InvariantCulture)),
			new("page", page.ToString(CultureInfo.InvariantCulture)),
			new("size", size.ToString(CultureInfo.InvariantCulture)),
			new("userAddress", wallet.NormalizeAddress()),
		];
	}

	private static string BuildUri(string path,
		IEnumerable<SynFuturesQueryParameter> parameters)
	{
		path = path.ThrowIfEmpty(nameof(path));
		if (!path.StartsWith('/'))
			throw new ArgumentException(
				"SynFutures API path must be absolute.", nameof(path));
		var query = string.Join("&", parameters
			.OrderBy(static parameter => parameter.Name, StringComparer.Ordinal)
			.Select(static parameter => Uri.EscapeDataString(parameter.Name) +
				"=" + Uri.EscapeDataString(parameter.Value ?? string.Empty)));
		return query.IsEmpty() ? path : path + "?" + query;
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "request rejected"
			: value.Truncate(512, string.Empty);
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"SynFutures API response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var block = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(block, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"SynFutures API response exceeds the 16 MiB safety limit.");
			target.Write(block, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}
}
