namespace StockSharp.OrderlyNetwork;

public partial class OrderlyNetworkMessageAdapter
{
	private const string _defaultRestEndpoint = "https://api.orderly.org";
	private const string _defaultPublicWebSocketEndpoint =
		"wss://ws-evm.orderly.org/ws/stream";
	private const string _defaultPrivateWebSocketEndpoint =
		"wss://ws-private-evm.orderly.org/v2/ws/private/stream";

	/// <summary>Orderly account identifier.</summary>
	[Display(Name = "Orderly account ID",
		Description = "Registered Orderly account identifier.",
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string AccountId { get; set; }

	/// <summary>Base58-encoded ED25519 Orderly secret.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>REST API endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey, Order = 0)]
	[BasicSetting]
	public string RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Public market-data WebSocket endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.WebSocketKey,
		Description = LocalizedStrings.WsEndpointKey,
		GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 0)]
	[BasicSetting]
	public string PublicWebSocketEndpoint { get; set; } =
		_defaultPublicWebSocketEndpoint;

	/// <summary>Private account-data WebSocket endpoint.</summary>
	[Display(Name = "Private WebSocket",
		Description = "Orderly private WebSocket endpoint.",
		GroupName = LocalizedStrings.WebSocketAddressesKey, Order = 1)]
	public string PrivateWebSocketEndpoint { get; set; } =
		_defaultPrivateWebSocketEndpoint;

	private int _historyLimit = 500;

	/// <summary>Maximum rows requested from a history endpoint.</summary>
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CountKey,
		GroupName = LocalizedStrings.HistoryKey, Order = 0)]
	public int HistoryLimit
	{
		get => _historyLimit;
		set => _historyLimit = value is >= 1 and <= 500
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value,
				"Orderly Network history limit must be between 1 and 500.");
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Secret), Secret)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint)
			.Set(nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint)
			.Set(nameof(HistoryLimit), HistoryLimit);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		AccountId = storage.GetValue<string>(nameof(AccountId))?.Trim();
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		RestEndpoint = NormalizeEndpoint(storage.GetValue(nameof(RestEndpoint),
			RestEndpoint), false, nameof(RestEndpoint));
		PublicWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PublicWebSocketEndpoint), PublicWebSocketEndpoint), true,
			nameof(PublicWebSocketEndpoint));
		PrivateWebSocketEndpoint = NormalizeEndpoint(storage.GetValue(
			nameof(PrivateWebSocketEndpoint), PrivateWebSocketEndpoint), true,
			nameof(PrivateWebSocketEndpoint));
		HistoryLimit = storage.GetValue(nameof(HistoryLimit), HistoryLimit);
	}

	private static string NormalizeEndpoint(string endpoint, bool isWebSocket,
		string parameterName)
	{
		endpoint = endpoint.ThrowIfEmpty(parameterName).Trim().TrimEnd('/');
		if (!endpoint.Contains("://", StringComparison.Ordinal))
			endpoint = (isWebSocket ? "wss://" : "https://") +
				endpoint.TrimStart('/');
		if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
			(isWebSocket
				? uri.Scheme is not ("ws" or "wss")
				: uri.Scheme is not ("http" or "https")) ||
			!uri.UserInfo.IsEmpty() ||
			(uri.Scheme is "http" or "ws" && !uri.IsLoopback))
			throw new ArgumentException(isWebSocket
				? "Orderly Network WebSocket endpoint must use WSS, except locally."
				: "Orderly Network REST endpoint must use HTTPS, except locally.",
				parameterName);
		return endpoint;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + (AccountId.IsEmpty()
			? ": Public REST"
			: Secret.IsEmpty() ? ": Read-only" : ": Trading");
}
