namespace StockSharp.MtNewswires;

/// <summary>The message adapter for MT Newswires distributed by viaNexus.</summary>
[MediaIcon(Media.MediaNames.mtnewswires)]
[Doc("topics/api/connectors/stock_market/mt_newswires.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MtNewswiresKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.News)]
public partial class MtNewswiresMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
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

	/// <summary>viaNexus API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://api.blueskyapi.com/v1/");

	/// <summary>viaNexus data-source identifier.</summary>
	[Display(
		Name = "Data source",
		Description = "viaNexus data-source identifier containing the licensed dataset.",
		GroupName = "Connection",
		Order = 2)]
	[BasicSetting]
	public string DataSource { get; set; } = "EDGE";

	/// <summary>MT Newswires dataset identifier.</summary>
	[Display(
		Name = "Dataset ID",
		Description = "Licensed viaNexus MT Newswires dataset identifier.",
		GroupName = "Connection",
		Order = 3)]
	[BasicSetting]
	public string DatasetId { get; set; } = "MT_NEWSWIRES_Global";

	/// <summary>Interval between latest-record polls.</summary>
	[Display(
		Name = "Polling interval",
		Description = "Interval between latest-record polls for live news.",
		GroupName = "News",
		Order = 4)]
	public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

	/// <summary>Maximum number of latest records requested by one live poll.</summary>
	[Display(
		Name = "Polling batch size",
		Description = "Maximum latest records requested by one live poll.",
		GroupName = "Limits",
		Order = 5)]
	public int PollingBatchSize { get; set; } = 100;

	/// <summary>Maximum records emitted by one historical request.</summary>
	[Display(
		Name = "News limit",
		Description = "Maximum records emitted by one historical request.",
		GroupName = "Limits",
		Order = 6)]
	public int MaxNewsItems { get; set; } = 10000;

	/// <summary>Lookback used for history without an explicit start time.</summary>
	[Display(
		Name = "Default history lookback",
		Description = "Lookback used for history without an explicit start time.",
		GroupName = "History",
		Order = 7)]
	public TimeSpan DefaultHistoryLookback { get; set; } = TimeSpan.FromDays(1);

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(DataSource), DataSource)
			.Set(nameof(DatasetId), DatasetId)
			.Set(nameof(PollingInterval), PollingInterval)
			.Set(nameof(PollingBatchSize), PollingBatchSize)
			.Set(nameof(MaxNewsItems), MaxNewsItems)
			.Set(nameof(DefaultHistoryLookback), DefaultHistoryLookback);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		DataSource = storage.GetValue(nameof(DataSource), DataSource);
		DatasetId = storage.GetValue(nameof(DatasetId), DatasetId);
		PollingInterval = storage.GetValue(nameof(PollingInterval), PollingInterval);
		PollingBatchSize = storage.GetValue(nameof(PollingBatchSize), PollingBatchSize);
		MaxNewsItems = storage.GetValue(nameof(MaxNewsItems), MaxNewsItems);
		DefaultHistoryLookback = storage.GetValue(nameof(DefaultHistoryLookback),
			DefaultHistoryLookback);
	}
}
