namespace StockSharp.Noren;

/// <summary>
/// Base message adapter for brokers using the Noren protocol.
/// </summary>
public abstract partial class NorenMessageAdapter : MessageAdapter, ITokenAdapter
{
	private const string _defaultProductKey = "DefaultProduct";

	/// <summary>User identifier.</summary>
	public abstract string UserId { get; set; }

	/// <summary>Trading account identifier.</summary>
	public abstract string AccountId { get; set; }

	/// <inheritdoc />
	public abstract SecureString Token { get; set; }

	/// <summary>Default product represented by the shared Noren protocol.</summary>
	protected NorenProducts DefaultNorenProduct { get; set; } = NorenProducts.Delivery;

	/// <summary>Maximum number of streaming reconnect attempts.</summary>
	public abstract int ReconnectAttempts { get; set; }

	/// <summary>REST API endpoint.</summary>
	public abstract string RestEndpoint { get; set; }

	/// <summary>Instrument file endpoint template.</summary>
	public abstract string InstrumentEndpointTemplate { get; set; }

	/// <summary>WebSocket endpoint.</summary>
	public abstract string WebSocketEndpoint { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(UserId), UserId)
			.Set(nameof(AccountId), AccountId)
			.Set(nameof(Token), Token)
			.Set(_defaultProductKey, DefaultNorenProduct)
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
		var product = storage.GetValue<object>(_defaultProductKey);
		if (product is NorenProducts norenProduct)
			DefaultNorenProduct = norenProduct;
		else if (product != null)
		{
			DefaultNorenProduct = Enum.TryParse<NorenProducts>(
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
