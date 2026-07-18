namespace StockSharp.ThetaData.Native;

sealed class ThetaDataApiException : InvalidOperationException
{
	public ThetaDataApiException(HttpStatusCode statusCode, string message)
		: base(message)
	{
		StatusCode = statusCode;
	}

	public HttpStatusCode StatusCode { get; }
}

sealed class ThetaDataRestClient : BaseLogReceiver
{
	private const int _noDataStatus = 472;

	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

	public ThetaDataRestClient(Uri address, int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-ThetaData/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await GetSymbols(ThetaDataMarkets.Stocks, cancellationToken);
	}

	public async Task<ThetaSymbol[]> GetSymbols(ThetaDataMarkets market,
		CancellationToken cancellationToken)
	{
		var response = await Get<ThetaResponse<ThetaSymbol>>(
			$"{market.ToNative()}/list/symbols?format=json", cancellationToken);
		return response?.Response ?? [];
	}

	public async Task<ThetaOptionContract[]> GetOptionContracts(string symbol, DateTime date,
		CancellationToken cancellationToken)
	{
		var path = CreatePath("option/list/contracts/quote");
		Append(path, "symbol", symbol.ThrowIfEmpty(nameof(symbol)));
		Append(path, "date", ToDate(date));
		var response = await Get<ThetaResponse<ThetaOptionContract>>(path.ToString(),
			cancellationToken);
		return response?.Response ?? [];
	}

	public async Task<ThetaQuote[]> GetSnapshotQuotes(ThetaSecurityKey key,
		ThetaDataStockVenues stockVenue, CancellationToken cancellationToken)
	{
		var path = CreatePath($"{key.Market.ToNative()}/snapshot/quote");
		AppendContract(path, key);
		if (key.Market == ThetaDataMarkets.Stocks)
			Append(path, "venue", stockVenue.ToNative());
		return key.Market == ThetaDataMarkets.Options
			? await GetOptionData<ThetaQuote>(path.ToString(), key, cancellationToken)
			: (await Get<ThetaResponse<ThetaQuote>>(path.ToString(), cancellationToken))
				?.Response ?? [];
	}

	public async Task<ThetaPrice[]> GetSnapshotPrices(ThetaSecurityKey key,
		CancellationToken cancellationToken)
	{
		if (key.Market != ThetaDataMarkets.Indices)
			throw new ArgumentOutOfRangeException(nameof(key), key, "Index key is required.");
		var path = CreatePath("index/snapshot/price");
		Append(path, "symbol", key.Root);
		return (await Get<ThetaResponse<ThetaPrice>>(path.ToString(), cancellationToken))
			?.Response ?? [];
	}

	public async Task<ThetaQuote[]> GetHistoryQuotes(ThetaSecurityKey key, DateTime date,
		TimeSpan startTime, TimeSpan endTime, ThetaDataStockVenues stockVenue,
		CancellationToken cancellationToken)
	{
		if (key.Market == ThetaDataMarkets.Indices)
			throw new NotSupportedException("ThetaData index feeds do not publish quotes.");
		var path = CreatePath($"{key.Market.ToNative()}/history/quote");
		AppendContract(path, key);
		Append(path, "date", ToDate(date));
		Append(path, "interval", "tick");
		Append(path, "start_time", ToTime(startTime));
		Append(path, "end_time", ToTime(endTime));
		if (key.Market == ThetaDataMarkets.Stocks)
			Append(path, "venue", stockVenue.ToNative());
		return key.Market == ThetaDataMarkets.Options
			? await GetOptionData<ThetaQuote>(path.ToString(), key, cancellationToken)
			: (await Get<ThetaResponse<ThetaQuote>>(path.ToString(), cancellationToken))
				?.Response ?? [];
	}

	public async Task<ThetaTrade[]> GetHistoryTrades(ThetaSecurityKey key, DateTime date,
		TimeSpan startTime, TimeSpan endTime, ThetaDataStockVenues stockVenue,
		CancellationToken cancellationToken)
	{
		if (key.Market == ThetaDataMarkets.Indices)
			throw new NotSupportedException(
				"ThetaData index price reports are not exchange trades.");
		var path = CreatePath($"{key.Market.ToNative()}/history/trade");
		AppendContract(path, key);
		Append(path, "date", ToDate(date));
		Append(path, "start_time", ToTime(startTime));
		Append(path, "end_time", ToTime(endTime));
		if (key.Market == ThetaDataMarkets.Stocks)
			Append(path, "venue", stockVenue.ToNative());
		return key.Market == ThetaDataMarkets.Options
			? await GetOptionData<ThetaTrade>(path.ToString(), key, cancellationToken)
			: (await Get<ThetaResponse<ThetaTrade>>(path.ToString(), cancellationToken))
				?.Response ?? [];
	}

	public async Task<ThetaPrice[]> GetHistoryPrices(ThetaSecurityKey key, DateTime date,
		TimeSpan startTime, TimeSpan endTime, CancellationToken cancellationToken)
	{
		if (key.Market != ThetaDataMarkets.Indices)
			throw new ArgumentOutOfRangeException(nameof(key), key, "Index key is required.");
		var path = CreatePath("index/history/price");
		Append(path, "symbol", key.Root);
		Append(path, "date", ToDate(date));
		Append(path, "interval", "tick");
		Append(path, "start_time", ToTime(startTime));
		Append(path, "end_time", ToTime(endTime));
		return (await Get<ThetaResponse<ThetaPrice>>(path.ToString(), cancellationToken))
			?.Response ?? [];
	}

	public async Task<ThetaEod[]> GetEod(ThetaSecurityKey key, DateTime from, DateTime to,
		CancellationToken cancellationToken)
	{
		var path = CreatePath($"{key.Market.ToNative()}/history/eod");
		AppendContract(path, key);
		Append(path, "start_date", ToDate(from));
		Append(path, "end_date", ToDate(to));
		return key.Market == ThetaDataMarkets.Options
			? await GetOptionData<ThetaEod>(path.ToString(), key, cancellationToken)
			: (await Get<ThetaResponse<ThetaEod>>(path.ToString(), cancellationToken))
				?.Response ?? [];
	}

	public async Task<ThetaBar[]> GetBars(ThetaSecurityKey key, DateTime date,
		TimeSpan timeFrame, TimeSpan startTime, TimeSpan endTime,
		ThetaDataStockVenues stockVenue, CancellationToken cancellationToken)
	{
		var path = CreatePath($"{key.Market.ToNative()}/history/ohlc");
		AppendContract(path, key);
		Append(path, "date", ToDate(date));
		Append(path, "interval", timeFrame.ToThetaInterval());
		Append(path, "start_time", ToTime(startTime));
		Append(path, "end_time", ToTime(endTime));
		if (key.Market == ThetaDataMarkets.Stocks)
			Append(path, "venue", stockVenue.ToNative());
		return key.Market == ThetaDataMarkets.Options
			? await GetOptionData<ThetaBar>(path.ToString(), key, cancellationToken)
			: (await Get<ThetaResponse<ThetaBar>>(path.ToString(), cancellationToken))
				?.Response ?? [];
	}

	private async Task<T[]> GetOptionData<T>(string path, ThetaSecurityKey key,
		CancellationToken cancellationToken)
	{
		var response = await Get<ThetaResponse<ThetaContractData<T>>>(path, cancellationToken);
		return (response?.Response ?? [])
			.Where(item => item?.Contract.Matches(key) == true)
			.SelectMany(item => item.Data ?? [])
			.Where(item => item != null)
			.ToArray();
	}

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, uri);
				request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
				using var response = await _http.SendAsync(request,
					HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				var content = await response.Content.ReadAsStringAsync(cancellationToken);
				var status = (int)response.StatusCode;
				if (status == _noDataStatus)
					return default;
				if (IsTransient(status) && attempt < _maxAttempts)
				{
					await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
					continue;
				}
				if (!response.IsSuccessStatusCode)
					throw CreateError(response.StatusCode, content, uri.AbsolutePath);
				if (content.IsEmpty())
					return default;
				try
				{
					return JsonConvert.DeserializeObject<T>(content, _jsonSettings);
				}
				catch (JsonException error)
				{
					throw new InvalidDataException(
						$"Invalid ThetaData response from '{uri.AbsolutePath}'.", error);
				}
			}
			catch (HttpRequestException) when (attempt < _maxAttempts)
			{
				await Task.Delay(TimeSpan.FromSeconds(Math.Min(10, attempt * 2)),
					cancellationToken);
			}
		}
	}

	private static ThetaDataApiException CreateError(HttpStatusCode statusCode,
		string content, string path)
	{
		var details = content?.Trim();
		if (details?.Length > 1000)
			details = details[..1000];
		var status = (int)statusCode;
		var name = status switch
		{
			470 => "GENERAL",
			471 => "PERMISSION",
			472 => "NO_DATA",
			473 => "INVALID_PARAMS",
			474 => "DISCONNECTED",
			475 => "TERMINAL_PARSE",
			476 => "WRONG_IP",
			477 => "NO_PAGE_FOUND",
			478 => "INVALID_SESSION_ID",
			570 => "LARGE_REQUEST",
			571 => "SERVER_STARTING",
			572 => "UNCAUGHT_ERROR",
			_ => statusCode.ToString(),
		};
		return new(statusCode, $"ThetaData request '{path}' failed ({status} {name})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static bool IsTransient(int status)
		=> status == 429 || status is 474 or 571 or 572 || status is >= 500 and <= 569;

	private static TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
	{
		if (response.Headers.RetryAfter?.Delta is { } delta)
			return ClampDelay(delta);
		if (response.Headers.RetryAfter?.Date is { } date)
			return ClampDelay(date.UtcDateTime - DateTime.UtcNow);
		return TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(attempt, 5)));
	}

	private static TimeSpan ClampDelay(TimeSpan delay)
		=> delay < TimeSpan.Zero ? TimeSpan.Zero :
			delay > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delay;

	private static StringBuilder CreatePath(string endpoint)
		=> new StringBuilder(endpoint).Append("?format=json");

	private static void AppendContract(StringBuilder path, ThetaSecurityKey key)
	{
		Append(path, "symbol", key.Root);
		if (key.Market != ThetaDataMarkets.Options)
			return;
		Append(path, "expiration", ToDate(key.Expiration));
		Append(path, "strike", key.Strike.ToString(CultureInfo.InvariantCulture));
		Append(path, "right", key.OptionType == OptionTypes.Call ? "call" : "put");
	}

	private static void Append(StringBuilder path, string name, string value)
	{
		if (!value.IsEmpty())
			path.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value));
	}

	private static string ToDate(DateTime value)
		=> value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

	private static string ToTime(TimeSpan value)
		=> value.ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}
}
