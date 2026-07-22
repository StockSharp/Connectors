namespace StockSharp.RavenPack;

/// <summary>The message adapter for RavenPack news analytics APIs.</summary>
[MediaIcon(Media.MediaNames.ravenpack)]
[Doc("topics/api/connectors/stock_market/ravenpack.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.RavenPackKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Paid |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.News)]
public partial class RavenPackMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
{
	private RavenPackProducts _product = RavenPackProducts.Edge;
	private Uri _address = RavenPackProducts.Edge.GetApiAddress();
	private Uri _feedAddress = RavenPackProducts.Edge.GetFeedAddress();

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>RavenPack product selected for the dataset.</summary>
	[Display(
		Name = "Product",
		Description = "RavenPack product selected for the dataset.",
		GroupName = "Connection",
		Order = 1)]
	[BasicSetting]
	public RavenPackProducts Product
	{
		get => _product;
		set
		{
			if (_product == value)
				return;
			var isDefaultAddress = _address == _product.GetApiAddress();
			var isDefaultFeedAddress = _feedAddress == _product.GetFeedAddress();
			_product = value;
			if (isDefaultAddress)
				_address = value.GetApiAddress();
			if (isDefaultFeedAddress)
				_feedAddress = value.GetFeedAddress();
		}
	}

	/// <summary>Identifier of a RavenPack granular dataset.</summary>
	[Display(
		Name = "Dataset ID",
		Description = "Identifier of a RavenPack granular dataset used for history and the feed.",
		GroupName = "Connection",
		Order = 2)]
	[BasicSetting]
	public string DatasetId { get; set; }

	/// <summary>RavenPack REST API base address.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.ServerAddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	[BasicSetting]
	public Uri Address
	{
		get => _address;
		set => _address = value;
	}

	/// <summary>RavenPack real-time JSON-lines feed base address.</summary>
	[Display(
		Name = "Feed address",
		Description = "RavenPack real-time JSON-lines feed base address.",
		GroupName = "Connection",
		Order = 4)]
	[BasicSetting]
	public Uri FeedAddress
	{
		get => _feedAddress;
		set => _feedAddress = value;
	}

	/// <summary>Maximum records emitted by one historical query.</summary>
	[Display(
		Name = "History limit",
		Description = "Maximum records emitted by one synchronous JSON query (up to 10000).",
		GroupName = "Limits",
		Order = 5)]
	public int MaxRecords { get; set; } = 10000;

	/// <summary>Lookback used for a history-only request without a start time.</summary>
	[Display(
		Name = "Default history lookback",
		Description = "Lookback used for a history-only request without a start time.",
		GroupName = "History",
		Order = 6)]
	public TimeSpan DefaultHistoryLookback { get; set; } = TimeSpan.FromDays(1);

	/// <summary>Resolve licensed story URLs through the Document API.</summary>
	[Display(
		Name = "Resolve document URLs",
		Description = "Resolve each licensed story URL through the Document API.",
		GroupName = "News",
		Order = 7)]
	public bool IsResolveDocumentUrls { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Product), Product)
			.Set(nameof(DatasetId), DatasetId)
			.Set(nameof(Address), Address)
			.Set(nameof(FeedAddress), FeedAddress)
			.Set(nameof(MaxRecords), MaxRecords)
			.Set(nameof(DefaultHistoryLookback), DefaultHistoryLookback)
			.Set(nameof(IsResolveDocumentUrls), IsResolveDocumentUrls);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Product = storage.GetValue(nameof(Product), Product);
		DatasetId = storage.GetValue<string>(nameof(DatasetId));
		Address = storage.GetValue(nameof(Address), Address);
		FeedAddress = storage.GetValue(nameof(FeedAddress), FeedAddress);
		MaxRecords = storage.GetValue(nameof(MaxRecords), MaxRecords);
		DefaultHistoryLookback = storage.GetValue(nameof(DefaultHistoryLookback),
			DefaultHistoryLookback);
		IsResolveDocumentUrls = storage.GetValue(nameof(IsResolveDocumentUrls),
			IsResolveDocumentUrls);
	}
}
