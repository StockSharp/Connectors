namespace StockSharp.Databento;

/// <summary>The message adapter for Databento market data.</summary>
[MediaIcon(Media.MediaNames.databento)]
[Doc("topics/api/connectors/stock_market/databento.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DatabentoKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.OrderLog | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles |
	MessageAdapterCategories.Stock | MessageAdapterCategories.Futures | MessageAdapterCategories.Options |
	MessageAdapterCategories.FX | MessageAdapterCategories.Commodities)]
public partial class DatabentoMessageAdapter : MessageAdapter
{
	private string _dataset = "GLBX.MDP3";
	private string _liveAddress = string.Empty;
	private string _historicalAddress = "https://hist.databento.com/v0/timeseries.get_range";

	/// <summary>Databento API key.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.KeyKey,
		Description = LocalizedStrings.DatabentoKeyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Key { get; set; }

	/// <summary>Databento dataset code.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DatabentoDatasetKey,
		Description = LocalizedStrings.DatabentoDatasetDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 1)]
	[BasicSetting]
	public string Dataset
	{
		get => _dataset;
		set => _dataset = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>Optional live gateway override. Empty derives the official gateway from <see cref="Dataset"/>.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DatabentoLiveAddressKey,
		Description = LocalizedStrings.DatabentoLiveAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 2)]
	public string LiveAddress
	{
		get => _liveAddress;
		set => _liveAddress = value ?? string.Empty;
	}

	/// <summary>Historical timeseries endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DatabentoHistoricalAddressKey,
		Description = LocalizedStrings.DatabentoHistoricalAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 3)]
	public string HistoricalAddress
	{
		get => _historicalAddress;
		set => _historicalAddress = value.ThrowIfEmpty(nameof(value));
	}

	/// <summary>Input symbology used for subscriptions and history requests.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.DatabentoSymbologyKey,
		Description = LocalizedStrings.DatabentoSymbologyDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 4)]
	[BasicSetting]
	public DatabentoSymbologyTypes Symbology { get; set; } = DatabentoSymbologyTypes.RawSymbol;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Key), Key)
			.Set(nameof(Dataset), Dataset)
			.Set(nameof(LiveAddress), LiveAddress)
			.Set(nameof(HistoricalAddress), HistoricalAddress)
			.Set(nameof(Symbology), Symbology);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Key = storage.GetValue<SecureString>(nameof(Key));
		Dataset = storage.GetValue(nameof(Dataset), Dataset);
		LiveAddress = storage.GetValue(nameof(LiveAddress), LiveAddress);
		HistoricalAddress = storage.GetValue(nameof(HistoricalAddress), HistoricalAddress);
		Symbology = storage.GetValue(nameof(Symbology), Symbology);
	}
}
