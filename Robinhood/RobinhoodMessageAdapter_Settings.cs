namespace StockSharp.Robinhood;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for the Robinhood Agentic Trading MCP API.
/// </summary>
[MediaIcon(Media.MediaNames.robinhood)]
[Doc("topics/api/connectors/stock_market/robinhood.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RobinhoodKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.Transactions | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Level1)]
[OrderCondition(typeof(RobinhoodOrderCondition))]
public partial class RobinhoodMessageAdapter : MessageAdapter, ITokenAdapter
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey, Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Robinhood MCP endpoint.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey, Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://agent.robinhood.com/mcp/trading");

	/// <summary>
	/// Polling interval for quotes, positions, and orders.
	/// </summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.IntervalKey, Description = LocalizedStrings.IntervalKey + LocalizedStrings.Dot, GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	[BasicSetting]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(PollingInterval), PollingInterval);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
	}
}
