namespace StockSharp.Public;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for the Public.com API.
/// </summary>
[MediaIcon(Media.MediaNames.publicdotcom)]
[Doc("topics/api/connectors/stock_market/public.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.PublicComKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Options | MessageAdapterCategories.Stock | MessageAdapterCategories.Crypto | MessageAdapterCategories.Level1)]
[OrderCondition(typeof(PublicOrderCondition))]
public partial class PublicMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Polling interval for quotes, positions, and orders.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.IntervalKey,
		Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
