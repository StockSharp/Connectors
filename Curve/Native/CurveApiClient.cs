namespace StockSharp.Curve.Native;

sealed class CurveApiClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly HttpClient _poolClient;
	private readonly HttpClient _pricesClient;
	private readonly SemaphoreSlim _requestGate = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequest;
	private bool _isDisposed;

	public CurveApiClient(string apiEndpoint, string pricesEndpoint)
	{
		_poolClient = CreateClient(apiEndpoint, "StockSharp-Curve/1.0");
		_pricesClient = CreateClient(pricesEndpoint, "StockSharp-Curve/1.0");
	}

	public override string Name => "Curve_HTTP_API";

	public ValueTask<CurveApiPool[]> GetLargePoolsAsync(
		CancellationToken cancellationToken)
		=> GetPoolsAsync("getPools/big/ethereum", cancellationToken);

	public ValueTask<CurveApiPool[]> GetAllPoolsAsync(
		CancellationToken cancellationToken)
		=> GetPoolsAsync("getPools/all/ethereum", cancellationToken);

	public async ValueTask<CurveTrade[]> GetTradesAsync(CurveMarket market,
		DateTime from, DateTime to, int maximum,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(market);
		from = from.ToUniversalTime();
		to = to.ToUniversalTime();
		if (to < from)
			throw new ArgumentOutOfRangeException(nameof(to));
		if (maximum is < 1 or > 1000)
			throw new ArgumentOutOfRangeException(nameof(maximum));

		var result = new List<CurveTrade>(maximum);
		for (var page = 1; page <= 10 && result.Count < maximum; page++)
		{
			var path = "v1/trades/ethereum/" + market.PoolId +
				"?main_token=" + Uri.EscapeDataString(
					market.BaseToken.Address) +
				"&reference_token=" + Uri.EscapeDataString(
					market.QuoteToken.Address) +
				"&page=" + page.ToString(CultureInfo.InvariantCulture) +
				"&per_page=100&include_state=false";
			var response = await GetAsync<CurvePricesTradesResponse>(
				_pricesClient, path, cancellationToken);
			ValidateTradeResponse(response, market);
			var pageTrades = response.Trades ?? [];
			if (pageTrades.Length == 0)
				break;
			var oldest = DateTime.MaxValue;
			foreach (var item in pageTrades)
			{
				var time = ParseTime(item.Time);
				oldest = time.Min(oldest);
				if (time < from || time > to || !TryConvertTrade(item,
					response, time, out var trade))
					continue;
				result.Add(trade);
			}
			if (pageTrades.Length < 100 || oldest <= from)
				break;
		}
		return [.. result.GroupBy(static trade => trade.Id,
			StringComparer.Ordinal).Select(static group => group.First())
			.OrderBy(static trade => trade.Time).TakeLast(maximum)];
	}

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_poolClient.Dispose();
		_pricesClient.Dispose();
		_requestGate.Dispose();
		base.DisposeManaged();
	}

	private async ValueTask<CurveApiPool[]> GetPoolsAsync(string path,
		CancellationToken cancellationToken)
	{
		var response = await GetAsync<CurveApiPoolsResponse>(_poolClient, path,
			cancellationToken);
		if (response?.IsSuccessful != true || response.Data is null)
			throw new InvalidDataException(
				$"Curve API returned an unsuccessful response for '{path}'.");
		return response.Data.Pools?.Where(static pool => pool is not null &&
			!pool.IsBroken && !pool.Address.IsEmpty() &&
			pool.Coins is { Length: >= 2 }).ToArray() ?? [];
	}

	private async ValueTask<TResult> GetAsync<TResult>(HttpClient client,
		string path, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		for (var attempt = 0; ; attempt++)
		{
			await WaitForRequestAsync(cancellationToken);
			using var response = await client.GetAsync(path,
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
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (!response.IsSuccessStatusCode)
				throw new InvalidOperationException(
					$"Curve HTTP {(int)response.StatusCode} for '{path}': " +
					Truncate(body));
			try
			{
				return JsonConvert.DeserializeObject<TResult>(body,
					_jsonSettings);
			}
			catch (JsonException error)
			{
				throw new InvalidDataException(
					$"Curve API returned malformed JSON for '{path}'.", error);
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
			_nextRequest = DateTime.UtcNow + TimeSpan.FromMilliseconds(100);
		}
		finally
		{
			_requestGate.Release();
		}
	}

	private static void ValidateTradeResponse(
		CurvePricesTradesResponse response, CurveMarket market)
	{
		if (response is null || !response.Address.EqualsIgnoreCase(
				market.PoolId) ||
			response.MainToken is null || response.ReferenceToken is null ||
			!response.MainToken.Address.EqualsIgnoreCase(
				market.BaseToken.Address) ||
			!response.ReferenceToken.Address.EqualsIgnoreCase(
				market.QuoteToken.Address))
			throw new InvalidDataException(
				"Curve Prices API returned inconsistent pair metadata.");
	}

	private static bool TryConvertTrade(CurvePricesTrade source,
		CurvePricesTradesResponse response, DateTime time,
		out CurveTrade trade)
	{
		trade = null;
		if (source is null || source.TokensSold <= 0 ||
			source.TokensBought <= 0 || source.TransactionHash.IsEmpty())
			return false;
		var baseIndex = response.MainToken.EventIndex ??
			response.MainToken.PoolIndex;
		var quoteIndex = response.ReferenceToken.EventIndex ??
			response.ReferenceToken.PoolIndex;
		Sides side;
		decimal volume;
		decimal quote;
		if (source.SoldIndex == baseIndex && source.BoughtIndex == quoteIndex)
		{
			side = Sides.Sell;
			volume = source.TokensSold;
			quote = source.TokensBought;
		}
		else if (source.SoldIndex == quoteIndex &&
			source.BoughtIndex == baseIndex)
		{
			side = Sides.Buy;
			volume = source.TokensBought;
			quote = source.TokensSold;
		}
		else
		{
			return false;
		}
		if (volume <= 0 || quote <= 0)
			return false;
		string hash;
		try
		{
			hash = source.TransactionHash.NormalizeHash();
		}
		catch (InvalidDataException)
		{
			return false;
		}
		trade = new()
		{
			Id = CurveExtensions.CreateTradeId(hash, source.SoldIndex,
				source.BoughtIndex),
			Time = time,
			Price = quote / volume,
			Volume = volume,
			Side = side,
			TransactionHash = hash,
		};
		return true;
	}

	private static DateTime ParseTime(string value)
	{
		if (!DateTime.TryParse(value, CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var time))
			throw new InvalidDataException(
				$"Curve Prices API returned invalid UTC time '{value}'.");
		return DateTime.SpecifyKind(time, DateTimeKind.Utc);
	}

	private static HttpClient CreateClient(string endpoint, string userAgent)
	{
		endpoint = NormalizeEndpoint(endpoint);
		var client = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.GZip |
				DecompressionMethods.Deflate,
		})
		{
			BaseAddress = new Uri(endpoint + '/', UriKind.Absolute),
			Timeout = TimeSpan.FromSeconds(45),
		};
		client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");
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
				"Curve API endpoint must use HTTP or HTTPS.", nameof(endpoint));
		return endpoint.TrimEnd('/');
	}

	private static string Truncate(string value)
		=> value.IsEmpty() ? "request rejected" :
			value.Trim().Truncate(512, string.Empty);

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"Curve API response exceeds the 16 MiB safety limit.");
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
					"Curve API response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.GetBuffer(), 0,
			checked((int)target.Length));
	}
}
