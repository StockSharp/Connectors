namespace StockSharp.Webull;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// The message adapter for Webull OpenAPI.
/// </summary>
[MediaIcon(Media.MediaNames.webull)]
[Doc("topics/api/connectors/stock_market/webull.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.WebullKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.RealTime | MessageAdapterCategories.Transactions | MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth | MessageAdapterCategories.Stock)]
public partial class WebullMessageAdapter : MessageAdapter, IKeySecretAdapter, ITokenAdapter, IDemoAdapter
{
	/// <inheritdoc />
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <inheritdoc />
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>
	/// Access token.
	/// </summary>
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Trading account identifier.
	/// </summary>
	[BasicSetting]
	public string Account { get; set; }

	/// <inheritdoc />
	[BasicSetting]
	public bool IsDemo { get; set; }

	private Uri BaseAddress => new(IsDemo ? "https://api.sandbox.webull.com/" : "https://api.webull.com/");

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(Token), Token)
			.Set(nameof(Account), Account)
			.Set(nameof(IsDemo), IsDemo);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		Token = storage.GetValue<SecureString>(nameof(Token));
		Account = storage.GetValue<string>(nameof(Account));
		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
	}
}
