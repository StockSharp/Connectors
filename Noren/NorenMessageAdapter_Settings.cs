namespace StockSharp.Noren;

/// <summary>
/// Base message adapter for brokers using the Noren protocol.
/// </summary>
public abstract partial class NorenMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <summary>User identifier.</summary>
	public string UserId { get; set; }

	/// <summary>Trading account identifier.</summary>
	public string AccountId { get; set; }

	/// <inheritdoc />
	public SecureString Token { get; set; }

	/// <summary>Default order product.</summary>
	public NorenProducts DefaultProduct { get; set; } = NorenProducts.Delivery;

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	public int ReconnectAttempts { get; set; } = 10;

	/// <summary>REST API endpoint.</summary>
	public string RestEndpoint { get; set; }

	/// <summary>Instrument file endpoint template.</summary>
	public string InstrumentEndpointTemplate { get; set; }

	/// <summary>WebSocket endpoint.</summary>
	public string WebSocketEndpoint { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserId), UserId)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Token), Token)
			.Set(nameof(DefaultProduct), DefaultProduct)
			.Set(nameof(ReconnectAttempts), ReconnectAttempts)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(InstrumentEndpointTemplate), InstrumentEndpointTemplate)
			.Set(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		UserId = storage.GetValue<string>(nameof(UserId));
		AccountId = storage.GetValue<string>(nameof(AccountId));
		Token = storage.GetValue<SecureString>(nameof(Token));
		var product = storage.GetValue<object>(nameof(DefaultProduct));
		if (product is NorenProducts norenProduct)
			DefaultProduct = norenProduct;
		else if (product != null)
		{
			DefaultProduct = Enum.TryParse<NorenProducts>(
				product.ToString(), true, out norenProduct)
					? norenProduct
					: product.To<NorenProducts>();
		}
		ReconnectAttempts = storage.GetValue(nameof(ReconnectAttempts), ReconnectAttempts);
		RestEndpoint = storage.GetValue(nameof(RestEndpoint), RestEndpoint);
		InstrumentEndpointTemplate = storage.GetValue(nameof(InstrumentEndpointTemplate), InstrumentEndpointTemplate);
		WebSocketEndpoint = storage.GetValue(nameof(WebSocketEndpoint), WebSocketEndpoint);
	}
}
