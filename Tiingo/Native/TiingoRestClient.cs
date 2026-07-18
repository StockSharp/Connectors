namespace StockSharp.Tiingo.Native;

sealed class TiingoApiException : InvalidOperationException
{
	public TiingoApiException(HttpStatusCode statusCode, int? code, string message)
		: base(message)
	{
		StatusCode = statusCode;
		Code = code;
	}

	public HttpStatusCode StatusCode { get; }
	public int? Code { get; }

	public bool IsLookupMiss => StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound;
}

sealed class TiingoRestClient : BaseLogReceiver
{
	private static readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		NullValueHandling = NullValueHandling.Ignore,
	};

	private readonly Uri _address;
	private readonly Uri _supportedTickersAddress;
	private readonly string _token;
	private readonly int _maxAttempts;
	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
	private readonly SemaphoreSlim _supportedTickersSync = new(1, 1);
	private TiingoSupportedTicker[] _supportedTickers;

	public TiingoRestClient(Uri address, Uri supportedTickersAddress, string token,
		int maxAttempts)
	{
		_address = EnsureTrailingSlash(address ?? throw new ArgumentNullException(nameof(address)));
		_supportedTickersAddress = supportedTickersAddress ??
			throw new ArgumentNullException(nameof(supportedTickersAddress));
		_token = token.ThrowIfEmpty(nameof(token));
		_maxAttempts = Math.Max(1, maxAttempts);
		_http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "StockSharp-Tiingo/1.0");
	}

	public async Task Validate(CancellationToken cancellationToken)
	{
		_ = await Get<TiingoTestResponse>("api/test/", cancellationToken);
	}

	public Task<TiingoSearchItem[]> Search(string query, CancellationToken cancellationToken)
		=> Get<TiingoSearchItem[]>("tiingo/utilities/search?query=" +
			Escape(query.ThrowIfEmpty(nameof(query))), cancellationToken);

	public Task<TiingoEodMeta> GetStockMetadata(string ticker,
		CancellationToken cancellationToken)
		=> Get<TiingoEodMeta>("tiingo/daily/" +
			Escape(ticker.ThrowIfEmpty(nameof(ticker))), cancellationToken);

	public Task<TiingoCryptoMeta[]> GetCryptoMetadata(string ticker,
		CancellationToken cancellationToken)
	{
		var path = "tiingo/crypto";
		if (!ticker.IsEmpty())
			path += "?tickers=" + Escape(ticker);
		return Get<TiingoCryptoMeta[]>(path, cancellationToken);
	}

	public Task<TiingoFxQuote[]> GetForexQuotes(string ticker,
		CancellationToken cancellationToken)
	{
		var path = "tiingo/fx/top";
		if (!ticker.IsEmpty())
			path += "?tickers=" + Escape(ticker);
		return Get<TiingoFxQuote[]>(path, cancellationToken);
	}

	public Task<TiingoIexQuote[]> GetStockQuote(string ticker,
		CancellationToken cancellationToken)
		=> Get<TiingoIexQuote[]>("iex/" + Escape(ticker.ThrowIfEmpty(nameof(ticker))),
			cancellationToken);

	public Task<TiingoCandle[]> GetStockCandles(string ticker, TimeSpan timeFrame,
		DateTime from, DateTime to, bool isAfterHours, bool isForceFill,
		CancellationToken cancellationToken)
	{
		ticker.ThrowIfEmpty(nameof(ticker));
		if (timeFrame >= TimeSpan.FromDays(1))
		{
			return Get<TiingoCandle[]>("tiingo/daily/" + Escape(ticker) + "/prices" +
				$"?startDate={Escape(ToDateTime(from))}&endDate={Escape(ToDateTime(to))}" +
				$"&resampleFreq={Escape(timeFrame.ToEodResample())}", cancellationToken);
		}

		return Get<TiingoCandle[]>("iex/" + Escape(ticker) + "/prices" +
			$"?startDate={Escape(ToDateTime(from))}&endDate={Escape(ToDateTime(to))}" +
			$"&resampleFreq={Escape(timeFrame.ToIntradayResample())}" +
			"&columns=open,high,low,close,volume" +
			$"&afterHours={ToBoolean(isAfterHours)}&forceFill={ToBoolean(isForceFill)}",
			cancellationToken);
	}

	public Task<TiingoCandle[]> GetForexCandles(string ticker, TimeSpan timeFrame,
		DateTime from, DateTime to, CancellationToken cancellationToken)
		=> Get<TiingoCandle[]>("tiingo/fx/" +
			Escape(ticker.ThrowIfEmpty(nameof(ticker))) + "/prices" +
			$"?startDate={Escape(ToDateTime(from))}&endDate={Escape(ToDateTime(to))}" +
			$"&resampleFreq={Escape(ToResample(timeFrame))}", cancellationToken);

	public Task<TiingoCryptoPrices[]> GetCryptoCandles(string ticker, TimeSpan timeFrame,
		DateTime? from, DateTime? to, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("tiingo/crypto/prices?tickers=")
			.Append(Escape(ticker.ThrowIfEmpty(nameof(ticker))))
			.Append("&resampleFreq=").Append(Escape(ToResample(timeFrame)));
		if (from != null)
			path.Append("&startDate=").Append(Escape(ToDateTime(from.Value)));
		if (to != null)
			path.Append("&endDate=").Append(Escape(ToDateTime(to.Value)));
		return Get<TiingoCryptoPrices[]>(path.ToString(), cancellationToken);
	}

	public Task<TiingoNewsItem[]> GetNews(string ticker, DateTime? from, DateTime? to,
		int limit, int offset, CancellationToken cancellationToken)
	{
		var path = new StringBuilder("tiingo/news?limit=")
			.Append(Math.Clamp(limit, 1, 1000).ToString(CultureInfo.InvariantCulture))
			.Append("&offset=").Append(Math.Max(0, offset).ToString(CultureInfo.InvariantCulture))
			.Append("&sortBy=publishedDate");
		if (!ticker.IsEmpty())
			path.Append("&tickers=").Append(Escape(ticker));
		if (from != null)
			path.Append("&startDate=").Append(Escape(ToDateTime(from.Value)));
		if (to != null)
			path.Append("&endDate=").Append(Escape(ToDateTime(to.Value)));
		return Get<TiingoNewsItem[]>(path.ToString(), cancellationToken);
	}

	public async Task<TiingoSupportedTicker[]> GetSupportedTickers(
		CancellationToken cancellationToken)
	{
		if (_supportedTickers != null)
			return _supportedTickers;

		await _supportedTickersSync.WaitAsync(cancellationToken);
		try
		{
			if (_supportedTickers != null)
				return _supportedTickers;
			var content = await GetContent(_supportedTickersAddress, "text/csv",
				false, cancellationToken);
			_supportedTickers = ParseSupportedTickers(content);
			return _supportedTickers;
		}
		finally
		{
			_supportedTickersSync.Release();
		}
	}

	private async Task<T> Get<T>(string path, CancellationToken cancellationToken)
	{
		var uri = new Uri(_address, path);
		var content = await GetContent(uri, "application/json", true, cancellationToken);
		if (content.IsEmpty())
			return default;
		try
		{
			return JsonConvert.DeserializeObject<T>(content, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"Invalid Tiingo response from '{uri.AbsolutePath}'.", error);
		}
	}

	private async Task<string> GetContent(Uri uri, string mediaType, bool isAuthenticated,
		CancellationToken cancellationToken)
	{
		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, uri);
			request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
			if (isAuthenticated)
				request.Headers.TryAddWithoutValidation("Authorization", "Token " + _token);
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var content = await response.Content.ReadAsStringAsync(cancellationToken);
			if (IsTransient(response.StatusCode) && attempt < _maxAttempts)
			{
				await Task.Delay(GetRetryDelay(response, attempt), cancellationToken);
				continue;
			}
			if (!response.IsSuccessStatusCode)
				throw CreateError(response.StatusCode, content, uri.AbsolutePath);
			return content;
		}
	}

	private static TiingoSupportedTicker[] ParseSupportedTickers(string content)
	{
		using var reader = new StringReader(content ?? string.Empty);
		var header = reader.ReadLine();
		if (header == null || !header.TrimStart('\ufeff').EqualsIgnoreCase(
			"ticker,exchange,assetType,priceCurrency,startDate,endDate"))
		{
			throw new InvalidDataException("Unexpected Tiingo supported-tickers CSV header.");
		}

		var result = new List<TiingoSupportedTicker>();
		string line;
		while ((line = reader.ReadLine()) != null)
		{
			if (line.IsEmpty())
				continue;
			var fields = ParseCsvLine(line);
			if (fields.Length != 6 || fields[0].IsEmpty())
				throw new InvalidDataException("Invalid row in Tiingo supported-tickers CSV.");
			result.Add(new()
			{
				Ticker = fields[0],
				Exchange = fields[1],
				AssetType = fields[2],
				PriceCurrency = fields[3],
				StartDate = fields[4],
				EndDate = fields[5],
			});
		}
		return [.. result];
	}

	private static string[] ParseCsvLine(string line)
	{
		var fields = new List<string>();
		var field = new StringBuilder();
		var isQuoted = false;
		for (var index = 0; index < line.Length; index++)
		{
			var ch = line[index];
			if (ch == '"')
			{
				if (isQuoted && index + 1 < line.Length && line[index + 1] == '"')
				{
					field.Append('"');
					index++;
				}
				else
					isQuoted = !isQuoted;
			}
			else if (ch == ',' && !isQuoted)
			{
				fields.Add(field.ToString());
				field.Clear();
			}
			else
				field.Append(ch);
		}
		if (isQuoted)
			throw new InvalidDataException("Unclosed quote in Tiingo supported-tickers CSV.");
		fields.Add(field.ToString());
		return [.. fields];
	}

	private static TiingoApiException CreateError(HttpStatusCode statusCode, string content,
		string path)
	{
		TiingoErrorResponse response = null;
		try
		{
			response = JsonConvert.DeserializeObject<TiingoErrorResponse>(content, _jsonSettings);
		}
		catch (JsonException)
		{
		}
		var details = response?.Detail.IsEmpty(response?.Message).IsEmpty(content);
		if (details?.Length > 1000)
			details = details[..1000];
		return new(statusCode, response?.Code,
			$"Tiingo request '{path}' failed ({(int)statusCode} {statusCode})" +
			(details.IsEmpty() ? "." : $": {details}"));
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode is >= 500 and <= 511;

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

	private static string ToResample(TimeSpan timeFrame)
		=> timeFrame < TimeSpan.FromDays(1) ? timeFrame.ToIntradayResample() :
			timeFrame == TimeSpan.FromDays(1) ? "1day" :
			timeFrame == TimeSpan.FromDays(7) ? "7day" :
			timeFrame == TimeSpan.FromDays(30) ? "30day" :
			throw new NotSupportedException($"Tiingo does not support {timeFrame} candles.");

	private static string ToDateTime(DateTime value)
		=> value.ToUtc().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

	private static string ToBoolean(bool value) => value ? "true" : "false";
	private static string Escape(string value) => Uri.EscapeDataString(value ?? string.Empty);

	private static Uri EnsureTrailingSlash(Uri address)
		=> address.AbsoluteUri.EndsWith('/') ? address : new(address.AbsoluteUri + "/");

	protected override void DisposeManaged()
	{
		_supportedTickersSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}
}
