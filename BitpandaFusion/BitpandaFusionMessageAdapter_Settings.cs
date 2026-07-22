namespace StockSharp.BitpandaFusion;

/// <summary>
/// The message adapter for Bitpanda Fusion.
/// </summary>
[MediaIcon(Media.MediaNames.bitpandafusion)]
[Doc("topics/api/connectors/crypto_exchanges/bitpanda_fusion.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitpandaFusionKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.Europe | MessageAdapterCategories.Free |
	MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles | MessageAdapterCategories.History |
	MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(BitpandaFusionOrderCondition))]
public partial class BitpandaFusionMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
{
	private static readonly Uri _defaultAddress =
		new("https://api.fusion.bitpanda.com/");

	/// <summary>
	/// Supported candle time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames
		=> BitpandaFusionExtensions.TimeFrames;

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Fusion REST API endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public Uri Address { get; set; } = _defaultAddress;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address ?? _defaultAddress);
	}

	/// <inheritdoc />
	public override string ToString() => base.ToString() + $": Key={Token.ToId()}";
}
