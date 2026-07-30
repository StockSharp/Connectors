namespace StockSharp.Quidax.Native;

sealed class QuidaxApiException(
	HttpStatusCode statusCode,
	string message)
	: InvalidOperationException(
		$"Quidax API error {(int)statusCode} ({statusCode}): {message}")
{
	public HttpStatusCode StatusCode { get; } = statusCode;
}

sealed class QuidaxRestClient : BaseLogReceiver
{
	private const int _maximumReadAttempts = 4;
	private const int _maximumPayloadLength = 8 * 1024 * 1024;

	private readonly Uri _endpoint;
	private readonly HttpClient _http;
	private readonly string _token;
	private readonly string _userId;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings =
		CreateJsonSettings();
	private DateTime _nextRequestTime;

	public QuidaxRestClient(
		string endpoint,
		SecureString token,
		string userId)
	{
		_endpoint = ValidateEndpoint(endpoint);
		_token = token.IsEmpty() ? null : token.UnSecure().Trim();
		_userId = userId.ThrowIfEmpty(nameof(userId)).Trim();
		_http = new HttpClient(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		})
		{
			Timeout = TimeSpan.FromSeconds(30),
		};
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-Quidax-Connector/1.0");
	}

	public override string Name => "Quidax_Rest";

	public bool IsCredentialsAvailable => !_token.IsEmpty();

	protected override void DisposeManaged()
	{
		_http.Dispose();
		_rateSync.Dispose();
		base.DisposeManaged();
	}

	public ValueTask<QuidaxMarket[]> GetMarketsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxMarket[]>(
			HttpMethod.Get,
			"/markets",
			[],
			null,
			false,
			true,
			cancellationToken);

	public async ValueTask<QuidaxTicker> GetTickerAsync(
		string market,
		CancellationToken cancellationToken)
	{
		var json = await SendRawAsync(
			HttpMethod.Get,
			$"/markets/tickers/{EscapePath(market)}",
			[],
			null,
			false,
			true,
			cancellationToken);
		return DeserializeTicker(json, market);
	}

	public ValueTask<QuidaxDepth> GetDepthAsync(
		string market,
		int depth,
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxDepth>(
			HttpMethod.Get,
			$"/markets/{EscapePath(market)}/depth",
			Values(("limit", NormalizeDepth(depth))),
			null,
			false,
			true,
			cancellationToken);

	public async ValueTask<QuidaxTrade[]> GetPublicTradesAsync(
		string market,
		CancellationToken cancellationToken)
	{
		var json = await SendRawAsync(
			HttpMethod.Get,
			$"/trades/{EscapePath(market)}",
			[],
			null,
			false,
			true,
			cancellationToken);
		return DeserializePublicTrades(json);
	}

	public ValueTask<QuidaxCandle[]> GetCandlesAsync(
		string market,
		TimeSpan timeFrame,
		DateTime? from,
		int limit,
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxCandle[]>(
			HttpMethod.Get,
			$"/markets/{EscapePath(market)}/k",
			Values(
				("timestamp", from?.ToQuidaxSeconds()),
				("period", timeFrame.ToQuidaxPeriod()),
				("limit", NormalizeCandleLimit(limit))),
			null,
			false,
			true,
			cancellationToken);

	public ValueTask<QuidaxWallet[]> GetWalletsAsync(
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxWallet[]>(
			HttpMethod.Get,
			$"/users/{EscapePath(_userId)}/wallets",
			[],
			null,
			true,
			true,
			cancellationToken);

	public ValueTask<QuidaxOrder> PlaceOrderAsync(
		QuidaxPlaceOrderRequest order,
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxOrder>(
			HttpMethod.Post,
			$"/users/{EscapePath(_userId)}/orders",
			[],
			order ?? throw new ArgumentNullException(nameof(order)),
			true,
			false,
			cancellationToken);

	public ValueTask<QuidaxOrder> GetOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxOrder>(
			HttpMethod.Get,
			$"/users/{EscapePath(_userId)}/orders/" +
				EscapePath(orderId),
			[],
			null,
			true,
			true,
			cancellationToken);

	public ValueTask<QuidaxOrder> CancelOrderAsync(
		string orderId,
		CancellationToken cancellationToken)
		=> SendAsync<QuidaxOrder>(
			HttpMethod.Post,
			$"/users/{EscapePath(_userId)}/orders/" +
				$"{EscapePath(orderId)}/cancel",
			[],
			new { },
			true,
			false,
			cancellationToken);

	public ValueTask<QuidaxOrder[]> GetOrdersAsync(
		string market,
		string state,
		int limit,
		CancellationToken cancellationToken)
		=> SendArrayAsync<QuidaxOrder>(
			HttpMethod.Get,
			$"/users/{EscapePath(_userId)}/orders",
			Values(
				("market", NormalizeOptionalMarket(market)),
				("state", state),
				("order_by", "desc"),
				("limit", NormalizePrivateLimit(limit))),
			true,
			cancellationToken);

	public ValueTask<QuidaxTrade[]> GetPrivateTradesAsync(
		string market,
		int limit,
		CancellationToken cancellationToken)
		=> SendArrayAsync<QuidaxTrade>(
			HttpMethod.Get,
			$"/users/{EscapePath(_userId)}/trades",
			Values(
				("market", NormalizeOptionalMarket(market)),
				("order_by", "desc"),
				("limit", NormalizePrivateLimit(limit))),
			true,
			cancellationToken);

	internal static int NormalizeDepth(int depth)
		=> depth.Max(1).Min(100);

	internal static int NormalizeCandleLimit(int limit)
		=> limit.Max(1).Min(10000);

	internal static string CreateAuthorizationValue(string token)
		=> "Bearer " + token.ThrowIfEmpty(nameof(token)).Trim();

	internal static TData Deserialize<TData>(string body)
	{
		var settings = CreateJsonSettings();
		try
		{
			var envelope = JsonConvert.DeserializeObject<
				QuidaxEnvelope>(
					body.ThrowIfEmpty(nameof(body)),
					settings);
			EnsureSuccess(envelope);
			if (envelope.Data is null ||
				envelope.Data.Type == JTokenType.Null)
				return default;
			return envelope.Data.ToObject<TData>(
				JsonSerializer.Create(settings));
		}
		catch (QuidaxApiException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Quidax returned malformed JSON.", error);
		}
	}

	internal static QuidaxTicker DeserializeTicker(
		string body,
		string market)
	{
		var settings = CreateJsonSettings();
		try
		{
			var envelope = JsonConvert.DeserializeObject<
				QuidaxEnvelope>(
					body.ThrowIfEmpty(nameof(body)),
					settings);
			EnsureSuccess(envelope);
			var data = envelope.Data as JObject ??
				throw new InvalidDataException(
					"Quidax ticker response contains no market map.");
			var property = data.Properties().FirstOrDefault(value =>
				value.Name.EqualsIgnoreCase(market));
			var entry = property?.Value.ToObject<QuidaxTickerEntry>(
				JsonSerializer.Create(settings)) ??
				throw new InvalidDataException(
					$"Quidax returned no ticker for '{market}'.");
			if (entry.Ticker is null)
				throw new InvalidDataException(
					$"Quidax returned an empty ticker for '{market}'.");
			entry.Ticker.Timestamp = entry.Timestamp;
			return entry.Ticker;
		}
		catch (QuidaxApiException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Quidax returned malformed ticker JSON.", error);
		}
	}

	internal static QuidaxTrade[] DeserializePublicTrades(
		string body)
	{
		var settings = CreateJsonSettings();
		try
		{
			var envelope = JsonConvert.DeserializeObject<
				QuidaxEnvelope>(
					body.ThrowIfEmpty(nameof(body)),
					settings);
			EnsureSuccess(envelope);
			return envelope.Data is JArray trades
				? trades.ToObject<QuidaxTrade[]>(
					JsonSerializer.Create(settings)) ?? []
				: [];
		}
		catch (QuidaxApiException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Quidax returned malformed trades JSON.", error);
		}
	}

	internal static string SerializeBody(object value)
		=> JsonConvert.SerializeObject(
			value ?? throw new ArgumentNullException(nameof(value)),
			CreateJsonSettings());

	private async ValueTask<TData[]> SendArrayAsync<TData>(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		bool isPrivate,
		CancellationToken cancellationToken)
	{
		var raw = await SendRawAsync(
			method,
			path,
			query,
			null,
			isPrivate,
			true,
			cancellationToken);
		var settings = CreateJsonSettings();
		try
		{
			var envelope = JsonConvert.DeserializeObject<
				QuidaxEnvelope>(raw, settings);
			EnsureSuccess(envelope);
			if (envelope.Data is JArray array)
				return array.ToObject<TData[]>(
					JsonSerializer.Create(settings)) ?? [];
			var page = envelope.Data?.ToObject<QuidaxPage<TData>>(
				JsonSerializer.Create(settings));
			return page?.Models ?? [];
		}
		catch (QuidaxApiException)
		{
			throw;
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				"Quidax returned malformed list JSON.", error);
		}
	}

	private async ValueTask<TData> SendAsync<TData>(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isPrivate,
		bool isRetryable,
		CancellationToken cancellationToken)
		=> Deserialize<TData>(await SendRawAsync(
			method,
			path,
			query,
			body,
			isPrivate,
			isRetryable,
			cancellationToken));

	private async ValueTask<string> SendRawAsync(
		HttpMethod method,
		string path,
		IReadOnlyList<(string Name, object Value)> query,
		object body,
		bool isPrivate,
		bool isRetryable,
		CancellationToken cancellationToken)
	{
		if (isPrivate && !IsCredentialsAvailable)
			throw new InvalidOperationException(
				"Quidax secret key is required for private operations.");

		var target = CreateUri(path, query);
		var bodyText = body is null ? null : SerializeBody(body);
		var attempts = isRetryable ? _maximumReadAttempts : 1;
		Exception lastError = null;

		for (var attempt = 1; attempt <= attempts; attempt++)
		{
			try
			{
				await WaitRateLimitAsync(cancellationToken);
				using var request = new HttpRequestMessage(method, target);
				if (bodyText is not null)
					request.Content = new StringContent(
						bodyText,
						Encoding.UTF8,
						"application/json");
				if (isPrivate)
					request.Headers.TryAddWithoutValidation(
						"Authorization",
						CreateAuthorizationValue(_token));
				using var response = await _http.SendAsync(
					request,
					HttpCompletionOption.ResponseHeadersRead,
					cancellationToken);
				if (response.Content.Headers.ContentLength >
					_maximumPayloadLength)
					throw new InvalidDataException(
						"Quidax response exceeds the size limit.");
				var responseBody =
					await response.Content.ReadAsStringAsync(
						cancellationToken);
				if (responseBody.Length > _maximumPayloadLength)
					throw new InvalidDataException(
						"Quidax response exceeds the size limit.");
				if (response.IsSuccessStatusCode)
					return responseBody;
				var error = CreateApiError(
					response.StatusCode,
					responseBody,
					response.ReasonPhrase);
				if (attempt >= attempts ||
					!IsTransient(response.StatusCode))
					throw error;
				lastError = error;
				var delay = response.Headers.RetryAfter?.Delta ??
					GetRetryDelay(attempt);
				await Task.Delay(delay, cancellationToken);
			}
			catch (Exception error) when (
				attempt < attempts &&
				!cancellationToken.IsCancellationRequested &&
				error is HttpRequestException or TaskCanceledException)
			{
				lastError = error;
				await Task.Delay(
					GetRetryDelay(attempt), cancellationToken);
			}
		}

		throw lastError ?? new InvalidOperationException(
			"Quidax API request failed.");
	}

	private async ValueTask WaitRateLimitAsync(
		CancellationToken cancellationToken)
	{
		await _rateSync.WaitAsync(cancellationToken);
		try
		{
			var delay = _nextRequestTime - DateTime.UtcNow;
			if (delay > TimeSpan.Zero)
				await Task.Delay(delay, cancellationToken);
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(200);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private Uri CreateUri(
		string path,
		IEnumerable<(string Name, object Value)> query)
	{
		var target = new Uri(
			_endpoint,
			path.ThrowIfEmpty(nameof(path)).TrimStart('/'));
		var queryString = (query ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)
			.Select(static value =>
				Uri.EscapeDataString(value.Name) + "=" +
				Uri.EscapeDataString(Convert.ToString(
					value.Value,
					CultureInfo.InvariantCulture)))
			.Join("&");
		if (queryString.IsEmpty())
			return target;
		var builder = new UriBuilder(target)
		{
			Query = queryString,
		};
		return builder.Uri;
	}

	private static JsonSerializerSettings CreateJsonSettings()
		=> new()
		{
			DateParseHandling = DateParseHandling.DateTime,
			FloatParseHandling = FloatParseHandling.Decimal,
			NullValueHandling = NullValueHandling.Ignore,
			Formatting = Formatting.None,
			Culture = CultureInfo.InvariantCulture,
		};

	private static void EnsureSuccess(QuidaxEnvelope envelope)
	{
		if (envelope is null)
			throw new InvalidDataException(
				"Quidax returned an empty response.");
		if (!envelope.Status.IsEmpty() &&
			!envelope.Status.EqualsIgnoreCase("success"))
			throw new QuidaxApiException(
				HttpStatusCode.OK,
				envelope.Message ?? envelope.Status);
	}

	private static QuidaxApiException CreateApiError(
		HttpStatusCode statusCode,
		string body,
		string reasonPhrase)
	{
		string details = null;
		try
		{
			var error = JsonConvert.DeserializeObject<QuidaxError>(
				body,
				CreateJsonSettings());
			details = error?.Message;
			if (details.IsEmpty() &&
				error?.Error is not null)
				details = error.Error.ToString(Formatting.None);
		}
		catch (JsonException)
		{
		}
		details = details.IsEmpty()
			? reasonPhrase.IsEmpty() ? body : reasonPhrase
			: details;
		if (details?.Length > 512)
			details = details[..512];
		return new(statusCode, details);
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode == 444 ||
			(int)statusCode >= 500;

	private static TimeSpan GetRetryDelay(int attempt)
		=> TimeSpan.FromMilliseconds(
			Math.Min(5000, 250 * (1 << attempt)));

	private static string EscapePath(string value)
		=> Uri.EscapeDataString(
			value.ThrowIfEmpty(nameof(value)).Trim());

	private static string NormalizeOptionalMarket(string value)
		=> value.IsEmpty() ? null : value.ToQuidaxSymbol();

	private static int NormalizePrivateLimit(int value)
		=> value.Max(1).Min(100);

	private static (string Name, object Value)[] Values(
		params (string Name, object Value)[] values)
		=> [.. (values ?? [])
			.Where(static value =>
				!value.Name.IsEmpty() && value.Value is not null)];

	private static Uri ValidateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim();
		if (!value.EndsWith('/'))
			value += "/";
		if (!Uri.TryCreate(
			value,
			UriKind.Absolute,
			out var endpoint) ||
			!endpoint.Scheme.EqualsIgnoreCase("https"))
			throw new ArgumentException(
				"Quidax endpoint must be an absolute HTTPS URI.",
				nameof(value));
		return endpoint;
	}
}
