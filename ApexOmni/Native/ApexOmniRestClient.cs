using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace StockSharp.ApexOmni.Native;

sealed class ApexOmniRestClient : BaseLogReceiver
{
	private const int _maximumResponseBytes = 16 * 1024 * 1024;
	private readonly Uri _endpoint;
	private readonly string _apiKey;
	private readonly string _secret;
	private readonly string _passphrase;
	private readonly HttpClient _http;
	private readonly SemaphoreSlim _rateSync = new(1, 1);
	private readonly JsonSerializerSettings _jsonSettings = new()
	{
		DateParseHandling = DateParseHandling.None,
		FloatParseHandling = FloatParseHandling.Decimal,
		NullValueHandling = NullValueHandling.Ignore,
		Formatting = Formatting.None,
		Culture = CultureInfo.InvariantCulture,
	};
	private DateTime _nextRequestTime;
	private TimeSpan _timeOffset;

	public ApexOmniRestClient(string endpoint, SecureString key,
		SecureString secret, SecureString passphrase)
	{
		_endpoint = CreateEndpoint(endpoint);
		_apiKey = key.IsEmpty() ? null : key.UnSecure().Trim();
		_secret = secret.IsEmpty() ? null : secret.UnSecure().Trim();
		_passphrase = passphrase.IsEmpty()
			? null
			: passphrase.UnSecure().Trim();
		if (new[] { _apiKey, _secret, _passphrase }.Count(static value =>
			!value.IsEmpty()) is not (0 or 3))
			throw new ArgumentException(
				"ApeX Omni private access requires key, secret, and passphrase.");
		_http = new(new HttpClientHandler
		{
			AutomaticDecompression = DecompressionMethods.All,
		});
		_http.DefaultRequestHeaders.UserAgent.ParseAdd(
			"StockSharp-ApeX-Omni-Connector/1.0");
		_http.DefaultRequestHeaders.Accept.Add(
			new MediaTypeWithQualityHeaderValue("application/json"));
	}

	public override string Name => "APEXOMNI_REST";

	public bool IsAuthenticated => !_apiKey.IsEmpty();

	public long CurrentTimestamp =>
		(DateTime.UtcNow + _timeOffset).ToUnixMilliseconds();

	public void SetServerTime(DateTime serverTime, DateTime localTime)
	{
		if (serverTime.Kind != DateTimeKind.Utc)
			throw new ArgumentException("Server time must be UTC.",
				nameof(serverTime));
		if (localTime.Kind != DateTimeKind.Utc)
			throw new ArgumentException("Local time must be UTC.",
				nameof(localTime));
		_timeOffset = serverTime - localTime;
	}

	protected override void DisposeManaged()
	{
		_rateSync.Dispose();
		_http.Dispose();
		base.DisposeManaged();
	}

	public async ValueTask<ApexOmniConfiguration> GetConfigurationAsync(
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<ApexOmniEmptyRequest,
			ApexOmniResponse<ApexOmniConfiguration>>(HttpMethod.Get,
			"/api/v3/symbols", new(), true, cancellationToken)).AsData(
				"configuration");

	public async ValueTask<DateTime> GetServerTimeAsync(
		CancellationToken cancellationToken)
	{
		var response = await SendPublicAsync<ApexOmniEmptyRequest,
			ApexOmniResponse<ApexOmniServerTime>>(HttpMethod.Get,
			"/api/v3/time", new(), true, cancellationToken);
		var value = response.AsData("server time").Time;
		if (value <= 0)
			throw new InvalidDataException(
				"ApeX Omni returned an invalid server time.");
		return value.ToApexOmniTime();
	}

	public async ValueTask<ApexOmniTicker[]> GetTickerAsync(string symbol,
		CancellationToken cancellationToken)
		=> (await SendPublicAsync<ApexOmniSymbolRequest,
			ApexOmniResponse<ApexOmniTicker[]>>(HttpMethod.Get,
			"/api/v3/ticker", new() { Symbol = symbol }, true,
			cancellationToken)).AsData("ticker") ?? [];

	public async ValueTask<ApexOmniOrderBook> GetOrderBookAsync(string symbol,
		int limit, CancellationToken cancellationToken)
		=> (await SendPublicAsync<ApexOmniDepthRequest,
			ApexOmniResponse<ApexOmniOrderBook>>(HttpMethod.Get,
			"/api/v3/depth", new() { Symbol = symbol, Limit = limit }, true,
			cancellationToken)).AsData("order book");

	public async ValueTask<ApexOmniTrade[]> GetTradesAsync(string symbol,
		int limit, CancellationToken cancellationToken)
		=> (await SendPublicAsync<ApexOmniTradesRequest,
			ApexOmniResponse<ApexOmniTrade[]>>(HttpMethod.Get,
			"/api/v3/trades", new() { Symbol = symbol, Limit = limit }, true,
			cancellationToken)).AsData("trades") ?? [];

	public async ValueTask<ApexOmniCandle[]> GetCandlesAsync(
		ApexOmniKlinesRequest request, CancellationToken cancellationToken)
		=> (await SendPublicAsync<ApexOmniKlinesRequest,
			ApexOmniResponse<ApexOmniKlineData>>(HttpMethod.Get,
			"/api/v3/klines", request, true, cancellationToken))
			.AsData("candles").Candles ?? [];

	public async ValueTask<ApexOmniAccount> GetAccountAsync(
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<ApexOmniEmptyRequest,
			ApexOmniResponse<ApexOmniAccount>>(HttpMethod.Get,
			"/api/v3/account", new(), true, cancellationToken)).AsData("account");

	public async ValueTask<ApexOmniAccountBalance> GetAccountBalanceAsync(
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<ApexOmniEmptyRequest,
			ApexOmniResponse<ApexOmniAccountBalance>>(HttpMethod.Get,
			"/api/v3/account-balance", new(), true, cancellationToken))
			.AsData("account balance");

	public async ValueTask<ApexOmniOrder[]> GetOpenOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var response = await SendPrivateAsync<ApexOmniSymbolRequest,
			ApexOmniResponse<ApexOmniOrdersPayload>>(HttpMethod.Get,
			"/api/v3/open-orders", new() { Symbol = symbol }, true,
			cancellationToken);
		return response.AsData("open orders").Orders ?? [];
	}

	public async ValueTask<ApexOmniOrdersPayload> GetOrderHistoryAsync(
		ApexOmniOrderHistoryRequest request,
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<ApexOmniOrderHistoryRequest,
			ApexOmniResponse<ApexOmniOrdersPayload>>(HttpMethod.Get,
			"/api/v3/history-orders", request, true, cancellationToken))
			.AsData("order history");

	public async ValueTask<ApexOmniFillsPayload> GetFillsAsync(
		ApexOmniOrderHistoryRequest request,
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<ApexOmniOrderHistoryRequest,
			ApexOmniResponse<ApexOmniFillsPayload>>(HttpMethod.Get,
			"/api/v3/fills", request, true, cancellationToken))
			.AsData("fills");

	public async ValueTask<ApexOmniWorstPrice> GetWorstPriceAsync(
		ApexOmniWorstPriceRequest request,
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<ApexOmniWorstPriceRequest,
			ApexOmniResponse<ApexOmniWorstPrice>>(HttpMethod.Get,
			"/api/v3/get-worst-price", request, true, cancellationToken))
			.AsData("worst price");

	public async ValueTask<ApexOmniOrder> CreateOrderAsync(
		ApexOmniCreateOrderRequest request,
		CancellationToken cancellationToken)
		=> (await SendPrivateAsync<ApexOmniCreateOrderRequest,
			ApexOmniResponse<ApexOmniOrder>>(HttpMethod.Post, "/api/v3/order", request,
			false, cancellationToken)).AsData("create order");

	public async ValueTask CancelOrderAsync(string id, bool isClientId,
		CancellationToken cancellationToken)
	{
		var path = isClientId
			? "/api/v3/delete-client-order-id"
			: "/api/v3/delete-order";
		var response = await SendPrivateAsync<ApexOmniCancelOrderRequest,
			ApexOmniResponse<string>>(HttpMethod.Post, path,
			new() { Id = id }, false, cancellationToken);
		response.EnsureSuccess("cancel order");
	}

	public async ValueTask CancelAllOrdersAsync(string symbol,
		CancellationToken cancellationToken)
	{
		var response = await SendPrivateAsync<ApexOmniCancelAllOrdersRequest,
			ApexOmniResponse<string>>(HttpMethod.Post,
			"/api/v3/delete-open-orders", new() { Symbol = symbol }, false,
			cancellationToken);
		response.EnsureSuccess("cancel all orders");
	}

	public string CreateWebSocketSignature(long timestamp)
	{
		EnsureCredentials();
		return Sign(timestamp, "GET", "/ws/accounts", string.Empty);
	}

	public ApexOmniPrivateLogin CreateWebSocketLogin(long timestamp)
	{
		EnsureCredentials();
		return new()
		{
			Topics = ["ws_zk_accounts_v3"],
			ApiKey = _apiKey,
			Passphrase = _passphrase,
			Timestamp = timestamp,
			Signature = CreateWebSocketSignature(timestamp),
		};
	}

	private ValueTask<TResponse> SendPublicAsync<TRequest, TResponse>(
		HttpMethod method, string path, TRequest request, bool isRead,
		CancellationToken cancellationToken)
		=> SendAsync<TRequest, TResponse>(method, path, request, isRead, false,
			cancellationToken);

	private ValueTask<TResponse> SendPrivateAsync<TRequest, TResponse>(
		HttpMethod method, string path, TRequest request, bool isRead,
		CancellationToken cancellationToken)
	{
		EnsureCredentials();
		return SendAsync<TRequest, TResponse>(method, path, request, isRead,
			true, cancellationToken);
	}

	private async ValueTask<TResponse> SendAsync<TRequest, TResponse>(
		HttpMethod method, string path, TRequest payload, bool isRead,
		bool isPrivate, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(payload);
		var parameters = GetParameters(payload);
		var requestPath = method == HttpMethod.Get
			? AppendQuery(path, parameters)
			: path;
		var form = method == HttpMethod.Post
			? JoinParameters(parameters)
			: string.Empty;

		for (var attempt = 0; ; attempt++)
		{
			await WaitRateLimitAsync(cancellationToken);
			using var request = new HttpRequestMessage(method,
				new Uri(_endpoint, requestPath));
			if (method == HttpMethod.Post)
				request.Content = new FormUrlEncodedContent(parameters.Select(
					static parameter => new KeyValuePair<string, string>(
						parameter.Name, parameter.Value)));
			if (isPrivate)
			{
				var timestamp = CurrentTimestamp;
				request.Headers.TryAddWithoutValidation("APEX-SIGNATURE",
					Sign(timestamp, method.Method.ToUpperInvariant(), requestPath,
						form));
				request.Headers.TryAddWithoutValidation("APEX-API-KEY", _apiKey);
				request.Headers.TryAddWithoutValidation("APEX-TIMESTAMP",
					timestamp.ToString(CultureInfo.InvariantCulture));
				request.Headers.TryAddWithoutValidation("APEX-PASSPHRASE",
					_passphrase);
			}
			using var response = await _http.SendAsync(request,
				HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			var body = await ReadBodyAsync(response.Content, cancellationToken);
			if (response.IsSuccessStatusCode)
				return Deserialize<TResponse>(body, requestPath);
			if (isRead && attempt < 3 && IsTransient(response.StatusCode))
			{
				await DelayRetryAsync(response, attempt, cancellationToken);
				continue;
			}
			throw CreateException(response.StatusCode, body);
		}
	}

	private string Sign(long timestamp, string method, string requestPath,
		string form)
	{
		var message = timestamp.ToString(CultureInfo.InvariantCulture) +
			method + requestPath + form;
		var encodedSecret = Convert.ToBase64String(
			Encoding.UTF8.GetBytes(_secret));
		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(encodedSecret));
		return Convert.ToBase64String(hmac.ComputeHash(
			Encoding.UTF8.GetBytes(message)));
	}

	private static ApexOmniParameter[] GetParameters<TRequest>(TRequest payload)
	{
		var parameters = new List<ApexOmniParameter>();
		foreach (var property in payload.GetType().GetProperties(
			BindingFlags.Instance | BindingFlags.Public))
		{
			var attribute = property.GetCustomAttribute<JsonPropertyAttribute>();
			if (attribute is null || !property.CanRead)
				continue;
			var value = property.GetValue(payload);
			if (value is null)
			{
				if (attribute.Required == Required.Always)
					throw new InvalidOperationException(
						$"ApeX Omni request field '{attribute.PropertyName}' is required.");
				continue;
			}
			parameters.Add(new()
			{
				Name = attribute.PropertyName,
				Value = ToParameterValue(value),
			});
		}
		return [.. parameters.OrderBy(static item => item.Name,
			StringComparer.Ordinal)];
	}

	private static string ToParameterValue(object value)
		=> value switch
		{
			bool flag => flag ? "true" : "false",
			Enum enumeration => enumeration.GetType()
				.GetMember(enumeration.ToString())[0]
				.GetCustomAttribute<EnumMemberAttribute>()?.Value ??
				enumeration.ToString(),
			IFormattable formattable => formattable.ToString(null,
				CultureInfo.InvariantCulture),
			_ => value.ToString(),
		};

	private static string AppendQuery(string path,
		ApexOmniParameter[] parameters)
		=> parameters.Length == 0
			? path
			: path + "?" + JoinParameters(parameters);

	private static string JoinParameters(ApexOmniParameter[] parameters)
		=> string.Join("&", parameters.Select(static item =>
			$"{item.Name}={item.Value}"));

	private TResponse Deserialize<TResponse>(string body, string operation)
	{
		if (body.IsEmpty())
			throw new InvalidDataException(
				$"ApeX Omni returned an empty response for {operation}.");
		try
		{
			return JsonConvert.DeserializeObject<TResponse>(body, _jsonSettings)
				?? throw new InvalidDataException(
					$"ApeX Omni returned an empty payload for {operation}.");
		}
		catch (JsonException error)
		{
			throw new InvalidDataException(
				$"ApeX Omni returned malformed JSON for {operation}.", error);
		}
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
			_nextRequestTime = DateTime.UtcNow.AddMilliseconds(100);
		}
		finally
		{
			_rateSync.Release();
		}
	}

	private void EnsureCredentials()
	{
		if (!IsAuthenticated)
			throw new InvalidOperationException(
				"ApeX Omni API key, secret, and passphrase are required.");
	}

	private static Uri CreateEndpoint(string value)
	{
		value = value.ThrowIfEmpty(nameof(value)).Trim().TrimEnd('/');
		if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
			(!uri.Scheme.Equals(Uri.UriSchemeHttp,
				StringComparison.OrdinalIgnoreCase) &&
			 !uri.Scheme.Equals(Uri.UriSchemeHttps,
				StringComparison.OrdinalIgnoreCase)))
			throw new ArgumentException(
				"ApeX Omni endpoint must be an HTTP or HTTPS URI.",
				nameof(value));
		return uri;
	}

	private static bool IsTransient(HttpStatusCode statusCode)
		=> statusCode == HttpStatusCode.TooManyRequests ||
			(int)statusCode >= 500;

	private static async ValueTask DelayRetryAsync(HttpResponseMessage response,
		int attempt, CancellationToken cancellationToken)
	{
		var delay = response.Headers.RetryAfter?.Delta ??
			TimeSpan.FromMilliseconds(250 * Math.Pow(2, attempt));
		await Task.Delay(delay.Min(TimeSpan.FromSeconds(10)), cancellationToken);
	}

	private static Exception CreateException(HttpStatusCode statusCode,
		string body)
	{
		ApexOmniErrorResponse error = null;
		try
		{
			if (!body.IsEmpty())
				error = JsonConvert.DeserializeObject<ApexOmniErrorResponse>(body);
		}
		catch (JsonException)
		{
		}
		var detail = error?.Message.IsEmpty() == false
			? error.Message
			: error?.Detail.IsEmpty() == false
				? error.Detail
				: error?.ReturnMessage.IsEmpty() == false
					? error.ReturnMessage
					: body?.Trim().Truncate(512, string.Empty);
		return new InvalidOperationException(
			$"ApeX Omni HTTP {(int)statusCode} ({statusCode})" +
			$"{(error?.Code.IsEmpty() == false ? $" code {error.Code}" : string.Empty)}: " +
			(detail.IsEmpty() ? "request rejected" : detail));
	}

	private static async ValueTask<string> ReadBodyAsync(HttpContent content,
		CancellationToken cancellationToken)
	{
		if (content.Headers.ContentLength is > _maximumResponseBytes)
			throw new InvalidDataException(
				"ApeX Omni response exceeds the 16 MiB safety limit.");
		await using var source = await content.ReadAsStreamAsync(cancellationToken);
		using var target = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var read = await source.ReadAsync(buffer, cancellationToken);
			if (read == 0)
				break;
			if (target.Length + read > _maximumResponseBytes)
				throw new InvalidDataException(
					"ApeX Omni response exceeds the 16 MiB safety limit.");
			target.Write(buffer, 0, read);
		}
		return Encoding.UTF8.GetString(target.ToArray());
	}
}

static class ApexOmniResponseExtensions
{
	public static TResult AsData<TResult>(this ApexOmniResponse<TResult> response,
		string operation)
	{
		response.EnsureSuccess(operation);
		if (response.Data is null)
			throw new InvalidDataException(
				$"ApeX Omni returned no data for {operation}.");
		return response.Data;
	}

	public static void EnsureSuccess<TResult>(
		this ApexOmniResponse<TResult> response, string operation)
	{
		if (response is null)
			throw new InvalidDataException(
				$"ApeX Omni returned no response for {operation}.");
		if (!response.Code.IsEmpty() && response.Code != "0")
			throw new InvalidOperationException(
				$"ApeX Omni rejected {operation}: {response.Code} " +
				response.Message);
	}
}
