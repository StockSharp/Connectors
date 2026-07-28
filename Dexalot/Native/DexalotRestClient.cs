namespace StockSharp.Dexalot.Native;

sealed class DexalotRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly HttpClient _http = new(new HttpClientHandler
	{
		AutomaticDecompression = DecompressionMethods.GZip |
			DecompressionMethods.Deflate,
	});
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private readonly string _signedHeader;
	private DateTime _nextRequest;

	public DexalotRestClient(string endpoint, string walletAddress,
		SecureString privateKey)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"Dexalot REST endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		var privateKeyText = privateKey.IsEmpty()
			? null
			: privateKey.UnSecure().Trim();
		if (!privateKeyText.IsEmpty())
		{
			var key = new EthECKey(privateKeyText);
			var derived = key.GetPublicAddress().NormalizeAddress();
			if (!walletAddress.IsEmpty() &&
				!derived.EqualsIgnoreCase(walletAddress.NormalizeAddress()))
				throw new ArgumentException(
					"The configured Dexalot wallet does not match the private " +
						"key.", nameof(walletAddress));
			var signature = new EthereumMessageSigner()
				.EncodeUTF8AndSign("dexalot", key);
			_signedHeader = $"{derived}:{signature}";
			WalletAddress = derived;
		}
		else if (!walletAddress.IsEmpty())
		{
			WalletAddress = walletAddress.NormalizeAddress();
		}
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Dexalot-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "Dexalot_REST";

	public string WalletAddress { get; }

	public bool CanReadPrivateData => !_signedHeader.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<(DexalotEnvironment Network,
		DexalotPair[] Pairs, DexalotDeployment TradePairs,
		DexalotDeployment Portfolio)> LoadReferenceDataAsync(
		CancellationToken cancellationToken)
	{
		var environments = await GetAsync<DexalotEnvironment[]>(
			"trading/environments", false, cancellationToken);
		var network = environments?.SingleOrDefault(item =>
			item.Type.EqualsIgnoreCase("subnet") &&
			item.ChainId == 432204) ?? throw new InvalidDataException(
				"Dexalot REST returned no production L1 environment.");
		var pairs = await GetAsync<DexalotPair[]>("trading/pairs", false,
			cancellationToken);
		pairs = [.. (pairs ?? []).Where(pair =>
			pair.Environment.EqualsIgnoreCase(network.Environment) &&
			pair.Status.EqualsIgnoreCase("deployed") &&
			!pair.Pair.IsEmpty() && !pair.Base.IsEmpty() &&
			!pair.Quote.IsEmpty() && pair.BaseDecimals is >= 0 and <= 28 &&
			pair.QuoteDecimals is >= 0 and <= 28 &&
			pair.BaseDisplayDecimals is >= 0 and <= 28 &&
			pair.QuoteDisplayDecimals is >= 0 and <= 28)];
		if (pairs.Length == 0)
			throw new InvalidDataException(
				"Dexalot REST returned no deployed trading pairs.");
		var tradePairs = await GetDeploymentAsync("TradePairs",
			network.Environment, cancellationToken);
		var portfolio = await GetDeploymentAsync("Portfolio",
			network.Environment, cancellationToken);
		return (network, pairs, tradePairs, portfolio);
	}

	public ValueTask<DexalotBalance[]> GetBalancesAsync(
		CancellationToken cancellationToken)
		=> GetAsync<DexalotBalance[]>("signed/portfoliobalance", true,
			cancellationToken);

	public ValueTask<DexalotOrder[]> GetOrdersAsync(string pair,
		DateTime? from, DateTime? to, int maximum,
		CancellationToken cancellationToken)
	{
		maximum = maximum.Max(1).Min(1000);
		var query = new List<string>
		{
			"category=0",
			$"itemsperpage={maximum}",
			"pageno=1",
		};
		if (!pair.IsEmpty())
			query.Add("pair=" + Uri.EscapeDataString(pair));
		if (from is DateTime first)
			query.Add("periodfrom=" + Uri.EscapeDataString(
				first.ToUniversalTime().ToString("O",
					CultureInfo.InvariantCulture)));
		if (to is DateTime last)
			query.Add("periodto=" + Uri.EscapeDataString(
				last.ToUniversalTime().ToString("O",
					CultureInfo.InvariantCulture)));
		return GetAsync<DexalotOrder[]>(
			"signed/orders?" + string.Join("&", query), true,
			cancellationToken);
	}

	public ValueTask<DexalotFill[]> GetFillsAsync(DateTime from, DateTime to,
		int maximum, CancellationToken cancellationToken)
	{
		maximum = maximum.Max(1).Min(100);
		var path = "signed/trader-fills?periodfrom=" +
			Uri.EscapeDataString(from.ToUniversalTime().ToString("O",
				CultureInfo.InvariantCulture)) + "&periodto=" +
			Uri.EscapeDataString(to.ToUniversalTime().ToString("O",
				CultureInfo.InvariantCulture)) + $"&itemsperpage={maximum}" +
			"&pageno=1";
		return GetAsync<DexalotFill[]>(path, true, cancellationToken);
	}

	private async ValueTask<DexalotDeployment> GetDeploymentAsync(
		string contractType, string environment,
		CancellationToken cancellationToken)
	{
		var token = await GetAsync<JToken>(
			"trading/deployment?contracttype=" +
				Uri.EscapeDataString(contractType), false,
			cancellationToken);
		var deployments = token switch
		{
			JArray array => array.ToObject<DexalotDeployment[]>(
				JsonSerializer.Create(_jsonSettings)),
			JObject item => [item.ToObject<DexalotDeployment>(
				JsonSerializer.Create(_jsonSettings))],
			_ => [],
		};
		return deployments.SingleOrDefault(item =>
			item.Environment.EqualsIgnoreCase(environment) &&
			item.Status.EqualsIgnoreCase("deployed") &&
			!item.Address.IsEmpty()) ?? throw new InvalidDataException(
				$"Dexalot REST returned no deployed {contractType} contract " +
					$"for '{environment}'.");
	}

	private async ValueTask<T> GetAsync<T>(string path, bool isSigned,
		CancellationToken cancellationToken)
	{
		if (isSigned && _signedHeader.IsEmpty())
			throw new InvalidOperationException(
				"A Dexalot private key is required for signed REST data.");
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(HttpMethod.Get,
				new Uri(_endpoint, path));
			if (isSigned)
				request.Headers.TryAddWithoutValidation("x-signature",
					_signedHeader);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content,
				cancellationToken);
			if (attempt < 3 && (response.StatusCode ==
					(HttpStatusCode)429 || (int)response.StatusCode >= 500))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(
					250 * (1 << attempt)), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"Dexalot REST HTTP {(int)response.StatusCode}: " +
						Truncate(body));
			try
			{
				return JsonConvert.DeserializeObject<T>(body, _jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"Dexalot REST returned an unexpected payload.", error);
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
				"Dexalot REST response exceeds the 16 MiB safety limit.");
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
					"Dexalot REST response exceeds the 16 MiB safety limit.");
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
