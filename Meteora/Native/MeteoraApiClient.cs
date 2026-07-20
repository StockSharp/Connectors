namespace StockSharp.Meteora.Native;

sealed class MeteoraApiClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _httpClient;
	private readonly JsonSerializerSettings _serializerSettings = new()
	{
		NullValueHandling = NullValueHandling.Ignore,
	};
	private bool _isDisposed;

	public MeteoraApiClient(string endpoint)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_httpClient = new()
		{
			BaseAddress = new Uri(endpoint + '/', UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Meteora/1.0");
	}

	public override string Name => "Meteora_Data_API";

	public async ValueTask<MeteoraApiPool[]> GetPoolsAsync(int maximum,
		CancellationToken cancellationToken)
	{
		if (maximum is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		var path = "pools?page=1&page_size=" + maximum.ToString(
			CultureInfo.InvariantCulture) +
			"&sort_by=volume_24h%3Adesc" +
			"&filter_by=is_blacklisted%3Dfalse";
		var response = await GetAsync<MeteoraApiPage<MeteoraApiPool>>(path,
			cancellationToken);
		return response?.Data?.Where(static pool => pool is not null &&
			!pool.IsBlacklisted).Take(maximum).ToArray() ?? [];
	}

	public async ValueTask<MeteoraApiPool> GetPoolAsync(string address,
		CancellationToken cancellationToken)
	{
		address = address.NormalizePublicKey();
		try
		{
			return await GetAsync<MeteoraApiPool>(
				"pools/" + Uri.EscapeDataString(address), cancellationToken);
		}
		catch (MeteoraApiException error) when (
			error.StatusCode == HttpStatusCode.NotFound)
		{
			return null;
		}
	}

	public ValueTask<MeteoraApiOhlcvResponse> GetCandlesAsync(string address,
		string timeFrame, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		address = address.NormalizePublicKey();
		timeFrame = timeFrame.ThrowIfEmpty(nameof(timeFrame)).Trim();
		var path = "pools/" + Uri.EscapeDataString(address) + "/ohlcv" +
			"?timeframe=" + Uri.EscapeDataString(timeFrame) +
			"&start_time=" + from.ToUniversalTime().ToUnix().ToString(
				CultureInfo.InvariantCulture) +
			"&end_time=" + to.ToUniversalTime().ToUnix().ToString(
				CultureInfo.InvariantCulture);
		return GetAsync<MeteoraApiOhlcvResponse>(path, cancellationToken);
	}

	public ValueTask<MeteoraApiOpenOrders> GetOpenOrdersAsync(string wallet,
		string pool, int pageSize, CancellationToken cancellationToken)
	{
		wallet = wallet.NormalizePublicKey();
		pool = pool.NormalizePublicKey();
		if (pageSize is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		return GetAsync<MeteoraApiOpenOrders>(
			"wallets/" + Uri.EscapeDataString(wallet) +
			"/limit_orders/open/pools/" + Uri.EscapeDataString(pool) +
			"?page=1&page_size=" + pageSize.ToString(
				CultureInfo.InvariantCulture), cancellationToken);
	}

	public ValueTask<MeteoraApiClosedOrders> GetClosedOrdersAsync(string wallet,
		string pool, int pageSize, CancellationToken cancellationToken)
	{
		wallet = wallet.NormalizePublicKey();
		pool = pool.NormalizePublicKey();
		if (pageSize is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(pageSize));
		return GetAsync<MeteoraApiClosedOrders>(
			"wallets/" + Uri.EscapeDataString(wallet) +
			"/limit_orders/closed/pools/" + Uri.EscapeDataString(pool) +
			"?page=1&page_size=" + pageSize.ToString(
				CultureInfo.InvariantCulture), cancellationToken);
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_httpClient.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask<TResult> GetAsync<TResult>(string path,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		using var response = await _httpClient.GetAsync(path,
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"Meteora API response exceeds the configured safety limit.");
		var body = await ReadBodyAsync(response.Content, cancellationToken);
		if (!response.IsSuccessStatusCode)
		{
			MeteoraApiError apiError = null;
			try
			{
				apiError = JsonConvert.DeserializeObject<MeteoraApiError>(body,
					_serializerSettings);
			}
			catch (JsonException)
			{
			}
			throw new MeteoraApiException(response.StatusCode,
				$"Meteora API request '{path}' failed: " +
				$"{Limit(apiError?.Message ?? body, 1024)}");
		}
		try
		{
			return JsonConvert.DeserializeObject<TResult>(body,
				_serializerSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"Meteora API returned malformed JSON for '{path}'.", error);
		}
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Meteora API endpoint must use HTTP or HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string Limit(string value, int maximum)
		=> value.IsEmpty() || value.Length <= maximum ? value : value[..maximum];

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
					"Meteora API response exceeds the configured safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
