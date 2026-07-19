namespace StockSharp.ManifestTrade.Native;

sealed class ManifestTradeStatsClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 16 * 1024 * 1024;
	private readonly HttpClient _httpClient;
	private bool _isDisposed;

	public ManifestTradeStatsClient(string endpoint)
	{
		endpoint = NormalizeEndpoint(endpoint);
		_httpClient = new()
		{
			BaseAddress = new Uri(endpoint + '/', UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(30),
		};
		_httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-ManifestTrade/1.0");
	}

	public override string Name => "ManifestTrade_Stats_API";

	public async ValueTask<ManifestTradeTicker[]> GetTickersAsync(
		int maximum, CancellationToken cancellationToken)
	{
		if (maximum is < 1 or > 200)
			throw new ArgumentOutOfRangeException(nameof(maximum));
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		using var response = await _httpClient.GetAsync("tickers",
			HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		if (response.Content.Headers.ContentLength is long length &&
			length > _maximumResponseLength)
			throw new InvalidDataException(
				"Manifest Trade stats response exceeds the safety limit.");
		var body = await ReadBodyAsync(response.Content, cancellationToken);
		if (!response.IsSuccessStatusCode)
			throw new ManifestTradeStatsException(response.StatusCode,
				$"Manifest Trade stats request failed: {Limit(body, 1024)}");
		ManifestTradeTicker[] tickers;
		try
		{
			tickers = JsonConvert.DeserializeObject<ManifestTradeTicker[]>(body);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Manifest Trade stats returned malformed JSON.", error);
		}
		return [.. (tickers ?? [])
			.Where(static ticker => ticker is not null &&
				!ticker.MarketAddress.IsEmpty() && !ticker.BaseMint.IsEmpty() &&
				!ticker.QuoteMint.IsEmpty())
			.OrderByDescending(static ticker => ticker.QuoteVolume)
			.ThenByDescending(static ticker => ticker.LiquidityUsd)
			.Take(maximum)];
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_httpClient.Dispose();
		base.DisposeManaged();
	}

	private static string NormalizeEndpoint(string endpoint)
	{
		endpoint = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim();
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = $"https://{endpoint.TrimStart('/')}";
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			uri.Scheme is not ("http" or "https"))
			throw new ArgumentException(
				"Manifest Trade stats endpoint must use HTTP or HTTPS.",
				nameof(endpoint));
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
					"Manifest Trade stats response exceeds the safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
