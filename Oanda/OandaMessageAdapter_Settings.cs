namespace StockSharp.Oanda;

/// <summary>
/// The messages adapter for OANDA.
/// </summary>
[MediaIcon(Media.MediaNames.oanda)]
[Doc("topics/api/connectors/forex/oanda.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.OandaKey,
	Description = LocalizedStrings.ForexConnectorKey,
	GroupName = LocalizedStrings.ForexKey)]
[MessageAdapterCategory(MessageAdapterCategories.FX | MessageAdapterCategories.RealTime |
	MessageAdapterCategories.Free | MessageAdapterCategories.History | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.Level1 | MessageAdapterCategories.Candles | MessageAdapterCategories.Transactions)]
[OrderCondition(typeof(OandaOrderCondition))]
public partial class OandaMessageAdapter : MessageAdapter, ITokenAdapter, IDemoAdapter
{
	/// <summary>
	/// Default value for <see cref="MessageAdapter.HeartbeatInterval"/>.
	/// </summary>
	public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(60);

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DemoKey,
		Description = LocalizedStrings.DemoTradingConnectKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	[BasicSetting]
	public bool IsDemo { get; set; }

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>
	/// Compression.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.CompressionKey,
		Description = LocalizedStrings.CompressionKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public bool UseCompression { get; set; }

	/// <summary>
	/// Write log messages only for transaction stream.
	/// </summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.OnlyTransactionsKey,
		Description = LocalizedStrings.OnlyTransactionsLogKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public bool LogOnlyTransactions { get; set; }

	private static readonly HashSet<TimeSpan> _timeFrames = new(new[]
	{
		TimeSpan.FromSeconds(5),
		TimeSpan.FromSeconds(10),
		TimeSpan.FromSeconds(15),
		TimeSpan.FromSeconds(30),
		TimeSpan.FromMinutes(1),
		TimeSpan.FromMinutes(2),
		TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5),
		TimeSpan.FromMinutes(10),
		TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30),
		TimeSpan.FromHours(1),
		TimeSpan.FromHours(2),
		TimeSpan.FromHours(3),
		TimeSpan.FromHours(4),
		TimeSpan.FromHours(6),
		TimeSpan.FromHours(8),
		TimeSpan.FromHours(12),
		TimeSpan.FromDays(1),
		TimeSpan.FromDays(7),
		TimeSpan.FromTicks(TimeHelper.TicksPerMonth)
	});

	/// <summary>
	/// Possible time-frames.
	/// </summary>
	public static IEnumerable<TimeSpan> AllTimeFrames => _timeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);

		storage
			.Set(nameof(IsDemo), IsDemo)
			.Set(nameof(Token), Token)
			.Set(nameof(UseCompression), UseCompression)
			.Set(nameof(LogOnlyTransactions), LogOnlyTransactions)
		;
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);

		IsDemo = storage.GetValue<bool>(nameof(IsDemo));
		Token = storage.GetValue<SecureString>(nameof(Token));
		UseCompression = storage.GetValue<bool>(nameof(UseCompression));
		LogOnlyTransactions = storage.GetValue<bool>(nameof(LogOnlyTransactions));
	}
}