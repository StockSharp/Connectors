namespace StockSharp.StonFi.Native;

sealed class StonFiRestClient : BaseLogReceiver
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
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Double,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;

	public StonFiRestClient(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.EndsWith('/'))
			endpoint += "/";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out _endpoint) ||
			!(_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttp) ||
				_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps)))
			throw new ArgumentException(
				"STON.fi API endpoint must be an absolute HTTP or HTTPS URI.",
				nameof(endpoint));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-StonFi-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
		_http.DefaultRequestHeaders.AcceptEncoding.ParseAdd("deflate");
	}

	public override string Name => "STON.fi_REST";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<StonPoolInfo[]> GetPoolsAsync(int limit,
		string poolFilter, CancellationToken cancellationToken)
	{
		if (!poolFilter.IsEmpty())
		{
			var addresses = poolFilter.Split([';', ','],
				StringSplitOptions.RemoveEmptyEntries |
				StringSplitOptions.TrimEntries)
				.Select(static address => address.NormalizeTonAddress())
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (addresses.Length == 0)
				throw new InvalidOperationException(
					"The STON.fi pool filter contains no addresses.");
			var selected = new List<StonPoolInfo>();
			foreach (var address in addresses)
				selected.Add(await GetPoolAsync(address, cancellationToken));
			return [.. selected];
		}

		var response = await PostAsync<StonPoolsResponse>("v1/pools/query",
			new StonPoolQuery
			{
				Condition = "!deprecated",
				Limit = Math.Min(1000, Math.Max(limit, limit * 3)),
				SortBy = ["popularity_index:desc"],
			}, cancellationToken);
		var pools = (response?.Pools ?? [])
			.Where(static pool => pool is not null && !pool.Deprecated &&
				!pool.Address.IsEmpty() && !pool.RouterAddress.IsEmpty() &&
				!pool.Token0Address.IsEmpty() &&
				!pool.Token1Address.IsEmpty())
			.GroupBy(static pool => pool.GetPairKey(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group
				.OrderByDescending(static pool => pool.PopularityIndex)
				.First())
			.Take(limit)
			.ToArray();
		if (pools.Length == 0)
			throw new InvalidDataException(
				"STON.fi returned no active liquidity pools.");
		return pools;
	}

	public async ValueTask<StonPoolInfo> GetPoolAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizeTonAddress();
		var response = await GetAsync<StonPoolResponse>(
			"v1/pools/" + Uri.EscapeDataString(address),
			cancellationToken);
		var pool = response?.Pool;
		if (pool is null || pool.Address.IsEmpty() ||
			pool.Token0Address.IsEmpty() || pool.Token1Address.IsEmpty())
			throw new InvalidDataException(
				$"STON.fi returned no pool '{address}'.");
		return pool;
	}

	public async ValueTask<StonAssetInfo[]> GetAssetsAsync(
		IEnumerable<string> addresses, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(addresses);
		var normalized = addresses
			.Select(static address => address.NormalizeTonAddress())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();
		if (normalized.Length == 0)
			return [];

		var result = new List<StonAssetInfo>();
		foreach (var batch in normalized.Chunk(500))
		{
			var response = await PostAsync<StonAssetsResponse>(
				"v1/assets/query", new StonAssetQuery
				{
					Condition = "false",
					UnconditionalAssets = batch,
					Limit = batch.Length,
				}, cancellationToken);
			result.AddRange(response?.Assets ?? []);
		}
		var assets = result.Where(static asset => asset is not null &&
				!asset.Address.IsEmpty())
			.GroupBy(static asset => asset.Address.NormalizeTonAddress(),
				StringComparer.OrdinalIgnoreCase)
			.Select(static group => group.First())
			.ToArray();
		foreach (var address in normalized)
		{
			if (!assets.Any(asset =>
				asset.Address.SameTonAddress(address)))
				throw new InvalidDataException(
					$"STON.fi returned no asset '{address}'.");
		}
		return assets;
	}

	public async ValueTask<StonAssetInfo> GetWalletAssetAsync(
		string walletAddress, string assetAddress,
		CancellationToken cancellationToken)
	{
		walletAddress = walletAddress.NormalizeTonAddress();
		assetAddress = assetAddress.NormalizeTonAddress();
		var response = await GetAsync<StonAssetResponse>(
			$"v1/wallets/{Uri.EscapeDataString(walletAddress)}/assets/" +
				Uri.EscapeDataString(assetAddress), cancellationToken);
		return response?.Asset ?? throw new InvalidDataException(
			$"STON.fi returned no wallet asset '{assetAddress}'.");
	}

	public ValueTask<StonLatestBlockResponse> GetLatestBlockAsync(
		CancellationToken cancellationToken)
		=> GetAsync<StonLatestBlockResponse>(
			"export/dexscreener/v1/latest-block", cancellationToken);

	public async ValueTask<StonEvent[]> GetEventsAsync(int fromBlock,
		int toBlock, CancellationToken cancellationToken)
	{
		if (fromBlock < 0 || toBlock < fromBlock ||
			toBlock - fromBlock > StonFiExtensions.MaximumEventBlockRange)
			throw new ArgumentOutOfRangeException(nameof(fromBlock),
				$"STON.fi event range '{fromBlock}-{toBlock}' is invalid.");
		var response = await GetAsync<StonEventsResponse>(
			$"export/dexscreener/v1/events?fromBlock={fromBlock}" +
				$"&toBlock={toBlock}", cancellationToken);
		return response?.Events ?? [];
	}

	public ValueTask<StonSwapSimulation> SimulateSwapAsync(
		string offerAddress, string askAddress, BigInteger units,
		decimal slippageFraction, string poolAddress, bool reverse,
		CancellationToken cancellationToken)
	{
		if (units <= 0)
			throw new ArgumentOutOfRangeException(nameof(units));
		if (slippageFraction is <= 0 or >= 1)
			throw new ArgumentOutOfRangeException(
				nameof(slippageFraction));
		var path = reverse
			? "v1/reverse_swap/simulate"
			: "v1/swap/simulate";
		path += "?offer_address=" + Uri.EscapeDataString(
			offerAddress.NormalizeTonAddress()) + "&ask_address=" +
			Uri.EscapeDataString(askAddress.NormalizeTonAddress()) +
			"&units=" + units.ToString(CultureInfo.InvariantCulture) +
			"&slippage_tolerance=" +
			slippageFraction.ToString(CultureInfo.InvariantCulture) +
			"&pool_address=" + Uri.EscapeDataString(
				poolAddress.NormalizeTonAddress());
		return PostAsync<StonSwapSimulation>(path, null,
			cancellationToken);
	}

	public ValueTask<StonSwapStatus> GetSwapStatusAsync(
		string routerAddress, string ownerAddress, ulong queryId,
		CancellationToken cancellationToken)
		=> GetAsync<StonSwapStatus>(
			"v1/swap/status?router_address=" +
				Uri.EscapeDataString(routerAddress.NormalizeTonAddress()) +
				"&owner_address=" +
				Uri.EscapeDataString(ownerAddress.NormalizeTonAddress()) +
				"&query_id=" +
				queryId.ToString(CultureInfo.InvariantCulture),
			cancellationToken);

	public async ValueTask<StonOperation[]> GetOperationsAsync(
		string walletAddress, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		walletAddress = walletAddress.NormalizeTonAddress();
		from = from.ToUniversalTime();
		to = to.ToUniversalTime();
		if (from > to)
			throw new ArgumentOutOfRangeException(nameof(from));
		var path = $"v1/wallets/{Uri.EscapeDataString(walletAddress)}/" +
			"operations?since=" + Uri.EscapeDataString(
				from.ToString("yyyy-MM-ddTHH:mm:ss",
					CultureInfo.InvariantCulture)) + "&until=" +
			Uri.EscapeDataString(to.ToString("yyyy-MM-ddTHH:mm:ss",
				CultureInfo.InvariantCulture)) + "&op_type=Swap";
		var response = await GetAsync<StonOperationsResponse>(path,
			cancellationToken);
		return
		[
			.. (response?.Operations ?? [])
				.Select(static item => item?.Operation)
				.Where(static operation => operation is not null)
		];
	}

	private ValueTask<T> GetAsync<T>(string path,
		CancellationToken cancellationToken)
		=> SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);

	private ValueTask<T> PostAsync<T>(string path, object body,
		CancellationToken cancellationToken)
		=> SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);

	private async ValueTask<T> SendAsync<T>(HttpMethod method,
		string path, object body, CancellationToken cancellationToken)
	{
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, path));
			if (body is not null)
				request.Content = new StringContent(
					JsonConvert.SerializeObject(body, _jsonSettings),
					Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await ReadBodyAsync(response.Content,
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
					$"STON.fi API HTTP {(int)response.StatusCode}: " +
						Truncate(content));
			try
			{
				return JsonConvert.DeserializeObject<T>(content,
					_jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					"STON.fi API returned an unexpected payload.", error);
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

	private static async ValueTask<string> ReadBodyAsync(
		HttpContent content, CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"STON.fi API response exceeds the 16 MiB safety limit.");
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
					"STON.fi API response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}

	private static string Truncate(string value)
	{
		value = value?.Trim();
		return value.IsEmpty()
			? "(empty response)"
			: value.Length <= 1000
				? value
				: value[..1000] + "...";
	}
}
