namespace StockSharp.Bitmart;

using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;
using System.Security;

using Ecng.ComponentModel;

/// <summary>
/// Sections.
/// </summary>
[DataContract]
[Serializable]
public enum BitmartSections
{
	/// <summary>
	/// Spot.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotKey,
		Description = LocalizedStrings.SpotSectionKey
	)]
	Spot,

	/// <summary>
	/// Futures.
	/// </summary>
	[EnumMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesKey,
		Description = LocalizedStrings.FuturesSectionKey
	)]
	Futures,
}

/// <summary>
/// The message adapter for <see cref="Bitmart"/>.
/// </summary>
[MediaIcon(Media.MediaNames.bitmart)]
[Doc("topics/api/connectors/crypto_exchanges/bitmart.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BitmartKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Ticks | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Transactions)]
public partial class BitmartMessageAdapter : MessageAdapter, IKeySecretAdapter, IAddressAdapter<string>
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.KeyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.SecretDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Section.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BoardKey,
		Description = LocalizedStrings.BoardKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public BitmartSections Section { get; set; }

	/// <summary>
	/// Memo.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.PassphraseKey,
		Description = LocalizedStrings.PassphraseKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public string Memo { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DomainAddressKey,
		Description = LocalizedStrings.DomainAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public string Address { get; set; } = "api-cloud-v2.bitmart.com";

	/// <summary>
	/// Spot public websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotPublicWsKey,
		Description = LocalizedStrings.SpotPublicWsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 5)]
	[BasicSetting]
	public string SpotPublicWsAddress { get; set; } = "wss://ws-manager-compress.bitmart.com/api?protocol=1.1";

	/// <summary>
	/// Spot private websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SpotPrivateWsKey,
		Description = LocalizedStrings.SpotPrivateWsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 6)]
	[BasicSetting]
	public string SpotPrivateWsAddress { get; set; } = "wss://ws-manager-compress.bitmart.com/user?protocol=1.1";

	/// <summary>
	/// Futures public websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesPublicWsKey,
		Description = LocalizedStrings.FuturesPublicWsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 7)]
	[BasicSetting]
	public string FuturesPublicWsAddress { get; set; } = "wss://openapi-ws-v2.bitmart.com/api?protocol=1.1";

	/// <summary>
	/// Futures private websocket endpoint.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.FuturesPrivateWsKey,
		Description = LocalizedStrings.FuturesPrivateWsDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 8)]
	[BasicSetting]
	public string FuturesPrivateWsAddress { get; set; } = "wss://openapi-ws-v2.bitmart.com/user?protocol=1.1";

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Memo), Memo)
			.Set(nameof(Section), Section)
			.Set(nameof(Address), Address)
			.Set(nameof(SpotPublicWsAddress), SpotPublicWsAddress)
			.Set(nameof(SpotPrivateWsAddress), SpotPrivateWsAddress)
			.Set(nameof(FuturesPublicWsAddress), FuturesPublicWsAddress)
			.Set(nameof(FuturesPrivateWsAddress), FuturesPrivateWsAddress)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Memo = storage.GetValue<string>(nameof(Memo));
		Section = storage.GetValue(nameof(Section), Section);
		Address = storage.GetValue(nameof(Address), Address);
		SpotPublicWsAddress = storage.GetValue(nameof(SpotPublicWsAddress), SpotPublicWsAddress);
		SpotPrivateWsAddress = storage.GetValue(nameof(SpotPrivateWsAddress), SpotPrivateWsAddress);
		FuturesPublicWsAddress = storage.GetValue(nameof(FuturesPublicWsAddress), FuturesPublicWsAddress);
		FuturesPrivateWsAddress = storage.GetValue(nameof(FuturesPrivateWsAddress), FuturesPrivateWsAddress);
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return base.ToString() + $": {Section}" + LocalizedStrings.Key + " = " + Key.ToId();
	}
}