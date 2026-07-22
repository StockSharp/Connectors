namespace StockSharp.Pyth.Native;

sealed class PythRestClient : BaseLogReceiver
{
	private const int _maximumResponseLength = 64 * 1024 * 1024;
	private readonly HttpClient _historyClient;
	private readonly HttpClient _routerClient;
	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly TimeSpan _requestInterval;
	private readonly string _token;
	private readonly JsonSerializerSettings _settings = new()
	{
		Culture = CultureInfo.InvariantCulture,
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		Formatting = Formatting.None,
		NullValueHandling = NullValueHandling.Ignore,
	};
	private DateTime _lastRequestTime;
	private bool _isDisposed;

	public PythRestClient(string historyEndpoint, string routerEndpoint,
		SecureString token, TimeSpan requestInterval)
	{
		var historyRoot = ValidateEndpoint(historyEndpoint, "history");
		var routerRoot = ValidateEndpoint(routerEndpoint, "router");
		if (token.IsEmpty())
			throw new ArgumentNullException(nameof(token));
		if (requestInterval < TimeSpan.Zero ||
			requestInterval > TimeSpan.FromMinutes(1))
			throw new ArgumentOutOfRangeException(nameof(requestInterval));

		_token = token.UnSecure().Trim();
		if (_token.IsEmpty() || _token.Length > 8192 || _token.Any(char.IsControl))
			throw new ArgumentException("Pyth API token is empty or invalid.",
				nameof(token));
		_requestInterval = requestInterval;
		_historyClient = CreateClient(historyRoot);
		_routerClient = CreateClient(routerRoot);
	}

	public override string Name => "Pyth_REST";

	public ValueTask<PythSymbol[]> GetSymbolsAsync(bool isEntitledOnly,
		CancellationToken cancellationToken)
		=> GetAsync<PythSymbol[]>(_historyClient, "symbols?entitled_only=" +
			(isEntitledOnly ? "true" : "false"), cancellationToken);

	public ValueTask<PythUpdate> GetLatestPriceAsync(PythLatestPriceRequest value,
		CancellationToken cancellationToken)
	{
		ValidateSubscription(value);
		return PostAsync<PythLatestPriceRequest, PythUpdate>(_routerClient,
			"latest_price", value, cancellationToken);
	}

	public async IAsyncEnumerable<PythHistoryCandle> GetHistoryAsync(
		PythSymbol instrument, PythChannels channel, TimeSpan timeFrame,
		DateTime from, DateTime to, int maximumBarsPerRequest,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(instrument);
		if (instrument.Symbol.IsEmpty())
			throw new ArgumentException("Pyth symbol is missing.", nameof(instrument));
		if (channel == PythChannels.Unknown)
			throw new ArgumentOutOfRangeException(nameof(channel));
		var resolution = timeFrame.ToResolution();
		from = from.EnsureUtc();
		to = to.EnsureUtc();
		if (from < DateTime.UnixEpoch || from > to)
			throw new ArgumentOutOfRangeException(nameof(from), from,
				"Pyth history range is invalid.");
		if (maximumBarsPerRequest is < 100 or > 100000)
			throw new ArgumentOutOfRangeException(nameof(maximumBarsPerRequest));

		var batchSpan = TimeSpan.FromTicks(checked(timeFrame.Ticks *
			(maximumBarsPerRequest - 1L)));
		var begin = from;
		while (begin <= to)
		{
			var end = AddClamped(begin, batchSpan);
			if (end > to)
				end = to;
			var path = channel.ToWire() + "/history?symbol=" +
				Uri.EscapeDataString(PythExtensions.NormalizePythSymbol(
					instrument.Symbol, nameof(instrument.Symbol))) +
				"&from=" + Format(begin.ToUnixSeconds()) +
				"&to=" + Format(end.ToUnixSeconds()) +
				"&resolution=" + Uri.EscapeDataString(resolution);
			var response = await GetAsync<PythHistoryResponse>(_historyClient, path,
				cancellationToken);
			foreach (var candle in ParseHistory(response))
				yield return candle;
			if (end >= to || end == DateTime.MaxValue)
				break;
			begin = end.AddSeconds(1);
		}
	}

	private IEnumerable<PythHistoryCandle> ParseHistory(
		PythHistoryResponse response)
	{
		if (response is null)
			throw new InvalidDataException("Pyth returned an empty history response.");
		if (response.Status == PythHistoryStatuses.Error)
			throw new InvalidOperationException(
				"Pyth history request failed: " +
				Redact(response.ErrorMessage.IsEmpty("unspecified error")));
		if (response.Status != PythHistoryStatuses.Ok)
			throw new InvalidDataException("Pyth returned an unknown history status.");

		var times = response.Times ?? [];
		var opens = response.Opens ?? [];
		var highs = response.Highs ?? [];
		var lows = response.Lows ?? [];
		var closes = response.Closes ?? [];
		var volumes = response.Volumes ?? [];
		if (opens.Length != times.Length || highs.Length != times.Length ||
			lows.Length != times.Length || closes.Length != times.Length ||
			volumes.Length != 0 && volumes.Length != times.Length)
			throw new InvalidDataException(
				"Pyth returned misaligned history arrays.");

		for (var index = 0; index < times.Length; index++)
		{
			var volume = volumes.Length == 0 ? 0 : volumes[index];
			if (volume < 0)
				throw new InvalidDataException(
					"Pyth returned negative history volume.");
			yield return new()
			{
				OpenTime = times[index].FromUnixSeconds(),
				OpenPrice = opens[index],
				HighPrice = highs[index],
				LowPrice = lows[index],
				ClosePrice = closes[index],
				Volume = volume,
			};
		}
	}

	private ValueTask<T> GetAsync<T>(HttpClient client, string path,
		CancellationToken cancellationToken)
		=> SendAsync<T>(client, HttpMethod.Get, path, null, cancellationToken);

	private ValueTask<TResponse> PostAsync<TRequest, TResponse>(HttpClient client,
		string path, TRequest value, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(value);
		byte[] content;
		try
		{
			content = Encoding.UTF8.GetBytes(
				JsonConvert.SerializeObject(value, _settings));
		}
		catch (JsonException error)
		{
			throw new InvalidOperationException(
				"Pyth request could not be serialized.", error);
		}
		return SendAsync<TResponse>(client, HttpMethod.Post, path, content,
			cancellationToken);
	}

	private async ValueTask<T> SendAsync<T>(HttpClient client, HttpMethod method,
		string path, byte[] body, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);
		await _gate.WaitAsync(cancellationToken);
		try
		{
			for (var attempt = 0; ; attempt++)
			{
				await EnforceRequestIntervalAsync(cancellationToken);
				using var request = new HttpRequestMessage(method, path);
				request.Headers.Accept.ParseAdd("application/json");
				if (body is not null)
					request.Content = new ByteArrayContent(body)
					{
						Headers = { ContentType = new("application/json") },
					};
				HttpResponseMessage response;
				try
				{
					response = await client.SendAsync(request,
						HttpCompletionOption.ResponseHeadersRead, cancellationToken);
				}
				catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
					error is HttpRequestException or TaskCanceledException)
				{
					if (attempt < 3)
					{
						await DelayRetryAsync(attempt, null, cancellationToken);
						continue;
					}
					throw new IOException("Pyth REST transport failed: " +
						Redact(error.Message));
				}

				using (response)
				{
					_lastRequestTime = DateTime.UtcNow;
					var content = await ReadResponseAsync(response, cancellationToken);
					if (response.IsSuccessStatusCode)
						return Deserialize<T>(content);
					if (attempt < 3 && IsTransient(response.StatusCode))
					{
						await DelayRetryAsync(attempt,
							response.Headers.RetryAfter?.Delta, cancellationToken);
						continue;
					}
					throw CreateException(response.StatusCode, content);
				}
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private T Deserialize<T>(byte[] content)
	{
		if (content.Length == 0)
			return default;
		try
		{
			return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(content),
				_settings);
		}
		catch (JsonException error)
		{
			throw new InvalidDataException("Pyth returned invalid JSON.", error);
		}
	}

	private HttpClient CreateClient(Uri endpoint)
	{
		var client = new HttpClient
		{
			BaseAddress = endpoint,
			Timeout = TimeSpan.FromSeconds(90),
		};
		client.DefaultRequestHeaders.Authorization = new("Bearer", _token);
		client.DefaultRequestHeaders.UserAgent.ParseAdd("StockSharp-Pyth/1.0");
		return client;
	}

	private async ValueTask EnforceRequestIntervalAsync(
		CancellationToken cancellationToken)
	{
		var remaining = _requestInterval - (DateTime.UtcNow - _lastRequestTime);
		if (remaining > TimeSpan.Zero)
			await Task.Delay(remaining, cancellationToken);
	}

	private static async ValueTask<byte[]> ReadResponseAsync(
		HttpResponseMessage response, CancellationToken cancellationToken)
	{
		if (response.Content.Headers.ContentLength is long contentLength &&
			contentLength > _maximumResponseLength)
			throw new InvalidDataException("Pyth response is too large.");
		await using var stream = await response.Content.ReadAsStreamAsync(
			cancellationToken);
		using var buffer = new MemoryStream();
		var chunk = new byte[81920];
		while (true)
		{
			var read = await stream.ReadAsync(chunk, cancellationToken);
			if (read == 0)
				break;
			if (buffer.Length + read > _maximumResponseLength)
				throw new InvalidDataException("Pyth response is too large.");
			buffer.Write(chunk, 0, read);
		}
		return buffer.ToArray();
	}

	private InvalidOperationException CreateException(HttpStatusCode statusCode,
		byte[] content)
	{
		string detail = null;
		if (content.Length > 0)
		{
			try
			{
				var error = JsonConvert.DeserializeObject<PythErrorResponse>(
					Encoding.UTF8.GetString(content), _settings);
				detail = error?.Message.IsEmpty(error?.Detail).IsEmpty(error?.Error);
			}
			catch (JsonException)
			{
			}
			if (detail.IsEmpty())
				detail = Encoding.UTF8.GetString(content, 0,
					Math.Min(content.Length, 4096));
		}
		detail = Redact(detail.IsEmpty(statusCode.ToString()));
		return new($"Pyth REST request failed ({(int)statusCode}): {detail}");
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			statusCode is HttpStatusCode.InternalServerError or
			HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or
			HttpStatusCode.GatewayTimeout;

	private static async ValueTask DelayRetryAsync(int attempt,
		TimeSpan? serverDelay, CancellationToken cancellationToken)
	{
		var delay = serverDelay ?? TimeSpan.FromMilliseconds(500 * (1 << attempt));
		if (delay < TimeSpan.Zero)
			delay = TimeSpan.Zero;
		if (delay > TimeSpan.FromMinutes(1))
			delay = TimeSpan.FromMinutes(1);
		await Task.Delay(delay, cancellationToken);
	}

	private static void ValidateSubscription(PythSubscriptionParameters value)
	{
		ArgumentNullException.ThrowIfNull(value);
		if (value.PriceFeedIds is not { Length: > 0 } ||
			value.PriceFeedIds.Any(static id => id == 0))
			throw new ArgumentException("Pyth feed IDs are invalid.", nameof(value));
		if (value.Properties is not { Length: > 0 } ||
			value.Properties.Any(static property => property == PythProperties.Unknown))
			throw new ArgumentException("Pyth properties are invalid.", nameof(value));
		if (value.Formats is null)
			throw new ArgumentException("Pyth formats are missing.", nameof(value));
		if (value.Channel == PythChannels.Unknown || !value.IsParsed)
			throw new ArgumentException("Pyth subscription is incomplete.",
				nameof(value));
	}

	private static Uri ValidateEndpoint(string endpoint, string description)
	{
		if (!Uri.TryCreate(endpoint?.Trim().TrimEnd('/') + "/",
			UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
			uri.Host.IsEmpty() || !uri.UserInfo.IsEmpty() || !uri.Query.IsEmpty() ||
			!uri.Fragment.IsEmpty() || !uri.AbsolutePath.EndsWith("/v1/",
				StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(
				$"Pyth {description} endpoint must be an HTTPS '/v1/' root without credentials, query, or fragment.",
				nameof(endpoint));
		return uri;
	}

	private static DateTime AddClamped(DateTime value, TimeSpan interval)
	{
		value = value.EnsureUtc();
		var ticks = Math.Min(interval.Ticks, DateTime.MaxValue.Ticks - value.Ticks);
		return new(value.Ticks + ticks, DateTimeKind.Utc);
	}

	private static string Format(long value)
		=> value.ToString(CultureInfo.InvariantCulture);

	private string Redact(string value)
		=> value.IsEmpty()
			? value
			: value.Replace(_token, "***", StringComparison.Ordinal);

	protected override void DisposeManaged()
	{
		if (_isDisposed)
			return;
		_isDisposed = true;
		_historyClient.Dispose();
		_routerClient.Dispose();
		_gate.Dispose();
		base.DisposeManaged();
	}
}
