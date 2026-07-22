namespace StockSharp.NasdaqDataLink;

/// <summary>The message adapter for Nasdaq Data Link time-series API.</summary>
[MediaIcon(Media.MediaNames.nasdaq)]
[Doc("topics/api/connectors/stock_market/nasdaq_data_link.html")]
[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.NasdaqDataLinkKey,
	Description = LocalizedStrings.MarketDataConnectorKey, GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.History |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures |
	MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Commodities | MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class NasdaqDataLinkMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<Uri>
{
	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.TokenKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Time-series API base address.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.AddressKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public Uri Address { get; set; } = new("https://data.nasdaq.com/api/v3/");

	/// <summary>Optional database code used to narrow catalog lookup.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CodeKey,
		Description = LocalizedStrings.CodeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 2)]
	public string DatabaseCode { get; set; }

	/// <summary>StockSharp security type assigned to returned data series.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 3)]
	public SecurityTypes SecurityType { get; set; } = SecurityTypes.Index;

	/// <summary>Optional currency assigned to returned data series.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 4)]
	public CurrencyTypes? Currency { get; set; }

	/// <summary>Optional scalar value column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ValueKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 5)]
	public string ValueColumn { get; set; }

	/// <summary>Optional open-price column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OpenPriceKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 6)]
	public string OpenColumn { get; set; }

	/// <summary>Optional high-price column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.HighPriceKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 7)]
	public string HighColumn { get; set; }

	/// <summary>Optional low-price column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LowPriceKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 8)]
	public string LowColumn { get; set; }

	/// <summary>Optional close-price column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.ClosingPriceKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 9)]
	public string CloseColumn { get; set; }

	/// <summary>Optional volume column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 10)]
	public string VolumeColumn { get; set; }

	/// <summary>Optional open-interest column override.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.OpenInterestKey,
		Description = LocalizedStrings.FieldKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.MarketDataKey, Order = 11)]
	public string OpenInterestColumn { get; set; }

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(Address), Address)
			.Set(nameof(DatabaseCode), DatabaseCode)
			.Set(nameof(SecurityType), SecurityType)
			.Set(nameof(Currency), Currency)
			.Set(nameof(ValueColumn), ValueColumn)
			.Set(nameof(OpenColumn), OpenColumn)
			.Set(nameof(HighColumn), HighColumn)
			.Set(nameof(LowColumn), LowColumn)
			.Set(nameof(CloseColumn), CloseColumn)
			.Set(nameof(VolumeColumn), VolumeColumn)
			.Set(nameof(OpenInterestColumn), OpenInterestColumn);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		Address = storage.GetValue(nameof(Address), Address);
		DatabaseCode = storage.GetValue<string>(nameof(DatabaseCode));
		SecurityType = storage.GetValue(nameof(SecurityType), SecurityType);
		Currency = storage.GetValue<CurrencyTypes?>(nameof(Currency));
		ValueColumn = storage.GetValue<string>(nameof(ValueColumn));
		OpenColumn = storage.GetValue<string>(nameof(OpenColumn));
		HighColumn = storage.GetValue<string>(nameof(HighColumn));
		LowColumn = storage.GetValue<string>(nameof(LowColumn));
		CloseColumn = storage.GetValue<string>(nameof(CloseColumn));
		VolumeColumn = storage.GetValue<string>(nameof(VolumeColumn));
		OpenInterestColumn = storage.GetValue<string>(nameof(OpenInterestColumn));
	}
}
