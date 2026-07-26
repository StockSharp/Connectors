namespace StockSharp.HitBtc.Native;

sealed class HitBtcRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;

	private readonly Uri _endpoint;
	private readonly System.Net.Http.HttpClient _http;
	private readonly SecureString _key;
	private readonly SecureString _secret;
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.DateTime,
		DateTimeZoneHandling = DateTimeZoneHandling.Utc,
		NullValueHandling = NullValueHandling.Ignore,
		Culture = CultureInfo.InvariantCulture,
	};

	public HitBtcRestClient(string endpoint, SecureString key, SecureString secret)
	{
		var value = endpoint.ThrowIfEmpty(nameof(endpoint)).Trim().TrimEnd('/') + "/";

		if (!Uri.TryCreate(value, UriKind.Absolute, out _endpoint) ||
			!_endpoint.Scheme.EqualsIgnoreCase(Uri.UriSchemeHttps))
			throw new ArgumentException("HitBTC REST endpoint must be an absolute HTTPS URI.", nameof(endpoint));

		_key = key;
		_secret = secret;
		_http = new System.Net.Http.HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-HitBTC-Connector/3.0");
	}

	public override string Name => nameof(HitBtc) + "_Rest";

	protected override void DisposeManaged()
	{
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<Symbol[]> GetSymbolsAsync(CancellationToken cancellationToken)
	{
		var response = await GetAsync<Dictionary<string, Symbol>>(
			"public/symbol", [], cancellationToken) ?? [];

		foreach (var pair in response)
			pair.Value.Id = pair.Key;

		return [.. response.Values];
	}

	public ValueTask<Trade[]> GetTradesAsync(string symbol, DateTime? from, DateTime? till,
		long? limit, CancellationToken cancellationToken)
	{
		var query = new List<KeyValuePair<string, string>>
		{
			new("sort", "ASC"),
			new("by", "timestamp"),
			new("limit", NormalizeLimit(limit).ToString(CultureInfo.InvariantCulture)),
		};

		AddTimeRange(query, from, till);

		return GetAsync<Trade[]>($"public/trades/{EscapePath(symbol)}", query, cancellationToken);
	}

	public ValueTask<Ohlc[]> GetCandlesAsync(string symbol, string period, DateTime? from,
		DateTime? till, long? limit, CancellationToken cancellationToken)
	{
		var query = new List<KeyValuePair<string, string>>
		{
			new("sort", "ASC"),
			new("period", period.ThrowIfEmpty(nameof(period))),
			new("limit", NormalizeLimit(limit).ToString(CultureInfo.InvariantCulture)),
		};

		AddTimeRange(query, from, till);

		return GetAsync<Ohlc[]>($"public/candles/{EscapePath(symbol)}", query, cancellationToken);
	}

	public async ValueTask<string> WithdrawAsync(string currency, decimal volume, WithdrawInfo info,
		CancellationToken cancellationToken)
	{
		if (info is null)
			throw new ArgumentNullException(nameof(info));

		if (info.Type != WithdrawTypes.Crypto)
			throw new NotSupportedException(LocalizedStrings.WithdrawTypeNotSupported.Put(info.Type));

		if (info.ChargeFee is not null)
			throw new NotSupportedException(
				"HitBTC API v3 does not allow a custom withdrawal fee.");

		EnsureCredentials();

		var form = new List<KeyValuePair<string, string>>
		{
			new("currency", currency.ThrowIfEmpty(nameof(currency)).ToUpperInvariant()),
			new("amount", volume.ToString(CultureInfo.InvariantCulture)),
			new("address", info.CryptoAddress.ThrowIfEmpty(nameof(info.CryptoAddress))),
		};

		if (!info.PaymentId.IsEmpty())
			form.Add(new("payment_id", info.PaymentId));

		using var request = new HttpRequestMessage(HttpMethod.Post,
			new Uri(_endpoint, "wallet/crypto/withdraw"))
		{
			Content = new FormUrlEncodedContent(form),
		};
		AddBasicAuthentication(request);

		var response = await SendAsync<WithdrawalResponse>(request, false, cancellationToken);
		return response?.Id ??
			throw new InvalidDataException("HitBTC returned an empty withdrawal response.");
	}

	private async ValueTask<T> GetAsync<T>(string path,
		IEnumerable<KeyValuePair<string, string>> query, CancellationToken cancellationToken)
	{
		var target = path.TrimStart('/');
		var queryString = BuildQuery(query);

		if (!queryString.IsEmpty())
			target += "?" + queryString;

		for (var attempt = 1; ; attempt++)
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_endpoint, target));

			try
			{
				return await SendAsync<T>(request, true, cancellationToken);
			}
			catch (HttpRequestException error) when (attempt < _maximumReadAttempts &&
				(error.StatusCode is null or HttpStatusCode.TooManyRequests ||
					(int)error.StatusCode.Value >= 500))
			{
				var delay = TimeSpan.FromMilliseconds(250 * (1 << (attempt - 1)));
				this.AddWarningLog("HitBTC {0} failed ({1}). Retrying in {2}.",
					target, error.Message, delay);
				await Task.Delay(delay, cancellationToken);
			}
		}
	}

	private async ValueTask<T> SendAsync<T>(HttpRequestMessage request, bool safe,
		CancellationToken cancellationToken)
	{
		this.AddVerboseLog("HitBTC {0} {1}", request.Method, request.RequestUri.PathAndQuery);

		using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
			cancellationToken);
		var body = await response.Content.ReadAsStringAsync(cancellationToken);

		if (!response.IsSuccessStatusCode)
			throw CreateHttpError(response.StatusCode, request.RequestUri.PathAndQuery, body, safe);

		if (body.IsEmpty())
			return default;

		try
		{
			var error = JsonConvert.DeserializeObject<ErrorEnvelope>(body, _jsonSettings)?.Error;
			if (error is not null)
				throw new InvalidOperationException(
					$"HitBTC {request.RequestUri.PathAndQuery} failed: {error}");

			return JsonConvert.DeserializeObject<T>(body, _jsonSettings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"HitBTC {request.RequestUri.PathAndQuery} returned malformed JSON.", error);
		}
	}

	private void EnsureCredentials()
	{
		if (_key.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);

		if (_secret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.SecretNotSpecified);
	}

	private void AddBasicAuthentication(HttpRequestMessage request)
	{
		var credentials = Convert.ToBase64String(
			$"{_key.UnSecure()}:{_secret.UnSecure()}".UTF8());
		request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
	}

	private static Exception CreateHttpError(HttpStatusCode statusCode, string target, string body,
		bool safe)
	{
		string message;
		try
		{
			message = JsonConvert.DeserializeObject<ErrorEnvelope>(body)?.Error?.ToString();
		}
		catch (JsonException)
		{
			message = null;
		}

		message = message.IsEmpty() ? body?.Trim() : message;
		if (message?.Length > 512)
			message = message[..512];

		return new HttpRequestException(
			$"HitBTC {target} returned HTTP {(int)statusCode}: {message}. " +
			(safe ? "The read request failed." :
				"The write was not retried; inspect exchange state before retrying."),
			null, statusCode);
	}

	private static void AddTimeRange(ICollection<KeyValuePair<string, string>> query,
		DateTime? from, DateTime? till)
	{
		if (from is DateTime fromTime)
			query.Add(new("from", FormatTime(fromTime)));

		if (till is DateTime tillTime)
			query.Add(new("till", FormatTime(tillTime)));
	}

	private static string FormatTime(DateTime value)
		=> value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

	private static int NormalizeLimit(long? limit)
		=> (int)Math.Clamp(limit ?? 1000, 1, 1000);

	private static string BuildQuery(IEnumerable<KeyValuePair<string, string>> query)
		=> query
			.Where(static pair => !pair.Key.IsEmpty() && pair.Value is not null)
			.Select(static pair =>
				Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))
			.Join("&");

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(value.ThrowIfEmpty(nameof(value)).ToUpperInvariant());
}
