namespace StockSharp.Breeze;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for ICICI Direct Breeze API.</summary>
[MediaIcon(Media.MediaNames.breeze)]
[Doc("topics/api/connectors/stock_market/breeze.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.BreezeKey,
	Description = LocalizedStrings.StockConnectorKey,
	GroupName = LocalizedStrings.IndiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.RealTime | MessageAdapterCategories.Free |
	MessageAdapterCategories.Transactions | MessageAdapterCategories.History | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Ticks | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options)]
[OrderCondition(typeof(BreezeOrderCondition))]
public partial class BreezeMessageAdapter : MessageAdapter, IKeySecretAdapter
{
	private static readonly TimeSpan[] _timeFrames =
	[
		TimeSpan.FromSeconds(1),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromDays(1),
	];

	/// <summary>Possible time-frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <summary>Breeze application key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.BreezeApiKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Breeze application secret.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SecretKey,
		Description = LocalizedStrings.BreezeSecretKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Secret { get; set; }

	/// <summary>Daily API session generated after interactive login.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.BreezeApiSessionKey,
		Description = LocalizedStrings.BreezeApiSessionDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public SecureString ApiSession { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Secret), Secret)
			.Set(nameof(ApiSession), ApiSession);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Secret = storage.GetValue<SecureString>(nameof(Secret));
		ApiSession = storage.GetValue<SecureString>(nameof(ApiSession));
	}
}
