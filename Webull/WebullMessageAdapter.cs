namespace StockSharp.Webull;

partial class WebullMessageAdapter
{
	private HttpClient _client;
	private WebullMqttClient _mqtt;
	private WebullTradeEventsClient _events;
	private readonly string _streamSessionId = Guid.NewGuid().ToString();
	private readonly ConcurrentDictionary<long, SecurityId> _level1Subscriptions = new();
	private readonly ConcurrentDictionary<long, SecurityId> _depthSubscriptions = new();
	private readonly ConcurrentDictionary<long, SecurityId> _tickSubscriptions = new();

	/// <summary>
	/// Initializes a new instance of the adapter.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction identifier generator.</param>
	public WebullMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddMarketDataSupport();
		this.AddTransactionalSupport();
		this.AddSupportedMarketDataType(DataType.Level1);
		this.AddSupportedMarketDataType(DataType.MarketDepth);
		this.AddSupportedMarketDataType(DataType.Ticks);
	}

	/// <inheritdoc />
	protected override async ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		if (Key.IsEmpty() || Secret.IsEmpty())
			throw new InvalidOperationException(LocalizedStrings.KeyNotSpecified);
		if (_client is not null)
			throw new InvalidOperationException(LocalizedStrings.NotDisconnectPrevTime);

		_client = new() { BaseAddress = BaseAddress };
		_mqtt = new(IsDemo ? "data-api.sandbox.webull.com" : "data-api.webull.com", 1883, Key.UnSecure(), _streamSessionId);
		_mqtt.MessageReceived += OnMqttMessage;
		_mqtt.Error += OnStreamError;
		await _mqtt.ConnectAsync(cancellationToken);

		var accounts = Account.IsEmpty()
			? (await Send<AccountInfo[]>(HttpMethod.Get, "/openapi/account/list", null, null, cancellationToken) ?? [])
				.Select(a => a.AccountId)
				.Where(a => !a.IsEmpty())
				.ToArray()
			: [Account];

		if (accounts.Length > 0)
		{
			_events = new(IsDemo ? "https://events-api.sandbox.webull.com" : "https://events-api.webull.com", Key.UnSecure(), Secret.UnSecure(), accounts);
			_events.EventReceived += OnTradeEvent;
			_events.Error += OnStreamError;
			_events.Start();
		}

		await base.ConnectAsync(message, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		if (_mqtt is not null)
		{
			_mqtt.MessageReceived -= OnMqttMessage;
			_mqtt.Error -= OnStreamError;
			await _mqtt.DisposeAsync();
			_mqtt = null;
		}

		if (_events is not null)
		{
			_events.EventReceived -= OnTradeEvent;
			_events.Error -= OnStreamError;
			await _events.DisposeAsync();
			_events = null;
		}

		_level1Subscriptions.Clear();
		_depthSubscriptions.Clear();
		_tickSubscriptions.Clear();
		_client?.Dispose();
		_client = null;

		await base.ResetAsync(message, cancellationToken);
	}

	private static KeyValuePair<string, string>[] ToQuery(object query)
	{
		if (query is null)
			return [];

		return query.GetType().GetProperties()
			.Select(property => (property, value: property.GetValue(query)))
			.Where(item => item.value is not null)
			.Select(item => new KeyValuePair<string, string>(
				item.property.GetAttribute<JsonPropertyAttribute>()?.PropertyName ?? item.property.Name,
				item.value switch
				{
					bool value => value.ToString().ToLowerInvariant(),
					Enum value => value.GetAttributeOfType<EnumMemberAttribute>()?.Value ?? value.ToString(),
					IEnumerable<string> values => values.Join(","),
					IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
					_ => item.value.ToString(),
				}))
			.ToArray();
	}

	private async Task<string> SendRaw(HttpMethod method, string path, object query, object body, CancellationToken cancellationToken)
	{
		var queryParameters = ToQuery(query);
		var queryString = queryParameters.Select(p => $"{p.Key.DataEscape()}={p.Value.DataEscape()}").Join("&");
		var bodyString = body is null ? null : JsonConvert.SerializeObject(body, Formatting.None);
		var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
		var nonce = Guid.NewGuid().ToString("N");

		using var request = new HttpRequestMessage(method, path + (queryString.IsEmpty() ? string.Empty : "?" + queryString));
		request.Headers.TryAddWithoutValidation("x-app-key", Key.UnSecure());
		request.Headers.TryAddWithoutValidation("x-timestamp", timestamp);
		request.Headers.TryAddWithoutValidation("x-signature", WebullSigner.Sign(path, queryParameters, bodyString, Key.UnSecure(), Secret.UnSecure(), BaseAddress.Host, timestamp, nonce));
		request.Headers.TryAddWithoutValidation("x-signature-algorithm", "HMAC-SHA1");
		request.Headers.TryAddWithoutValidation("x-signature-version", "1.0");
		request.Headers.TryAddWithoutValidation("x-signature-nonce", nonce);
		request.Headers.TryAddWithoutValidation("x-version", "v2");
		if (!Token.IsEmpty())
			request.Headers.TryAddWithoutValidation("x-access-token", Token.UnSecure());
		if (bodyString is not null)
			request.Content = new StringContent(bodyString, Encoding.UTF8, "application/json");

		using var response = await _client.SendAsync(request, cancellationToken);
		var text = await response.Content.ReadAsStringAsync(cancellationToken);
		response.EnsureSuccessStatusCode();
		return text;
	}

	private async Task<T> Send<T>(HttpMethod method, string path, object query, object body, CancellationToken cancellationToken)
	{
		var text = await SendRaw(method, path, query, body, cancellationToken);
		return text.IsEmpty() ? default : JsonConvert.DeserializeObject<T>(text);
	}

	private async Task Send(HttpMethod method, string path, object query, object body, CancellationToken cancellationToken)
		=> await SendRaw(method, path, query, body, cancellationToken);
}
