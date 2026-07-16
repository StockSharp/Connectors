namespace StockSharp.DxFeed;

using System.ComponentModel.DataAnnotations;

/// <summary>The message adapter for the dxFeed dxLink WebSocket API.</summary>
[MediaIcon(Media.MediaNames.dxfeed)]
[Doc("topics/api/connectors/stock_market/dxfeed.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.DxFeedKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.MarketDataKey)]
[MessageAdapterCategory(MessageAdapterCategories.US | MessageAdapterCategories.Europe |
	MessageAdapterCategories.Asia | MessageAdapterCategories.RealTime | MessageAdapterCategories.History |
	MessageAdapterCategories.Paid | MessageAdapterCategories.Level1 | MessageAdapterCategories.MarketDepth |
	MessageAdapterCategories.OrderLog | MessageAdapterCategories.Ticks | MessageAdapterCategories.Candles | MessageAdapterCategories.Stock |
	MessageAdapterCategories.Futures | MessageAdapterCategories.Options | MessageAdapterCategories.FX |
	MessageAdapterCategories.Crypto | MessageAdapterCategories.Commodities)]
public partial class DxFeedMessageAdapter : MessageAdapter, ITokenAdapter, IAddressAdapter<string>
{
	private string _address = "wss://demo.dxfeed.com/dxlink-ws";
	private TimeSpan _aggregationPeriod;
	private int _marketDepthLevels = 20;
	private string _marketDepthSources = "NTV";

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.AddressKey,
		Description = LocalizedStrings.DxFeedAddressDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 0)]
	[BasicSetting]
	public string Address
	{
		get => _address;
		set => _address = value.ThrowIfEmpty(nameof(value));
	}

	/// <inheritdoc />
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.DxFeedTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 1)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>Requested server-side aggregation period.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DxFeedAggregationPeriodKey,
		Description = LocalizedStrings.DxFeedAggregationPeriodDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 2)]
	public TimeSpan AggregationPeriod
	{
		get => _aggregationPeriod;
		set
		{
			if (value < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);
			_aggregationPeriod = value;
		}
	}

	/// <summary>Maximum number of DOM levels requested from dxLink.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DepthKey,
		Description = LocalizedStrings.DxFeedDepthLevelsDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 3)]
	public int MarketDepthLevels
	{
		get => _marketDepthLevels;
		set => _marketDepthLevels = value > 0
			? value
			: throw new ArgumentOutOfRangeException(nameof(value), value, LocalizedStrings.InvalidValue);
	}

	/// <summary>Comma-separated dxFeed order sources used by the DOM service.</summary>
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.DxFeedDepthSourcesKey,
		Description = LocalizedStrings.DxFeedDepthSourcesDescKey,
		GroupName = LocalizedStrings.ConnectionKey, Order = 4)]
	public string MarketDepthSources
	{
		get => _marketDepthSources;
		set => _marketDepthSources = value.ThrowIfEmpty(nameof(value));
	}

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Address), Address)
			.Set(nameof(Token), Token)
			.Set(nameof(AggregationPeriod), AggregationPeriod)
			.Set(nameof(MarketDepthLevels), MarketDepthLevels)
			.Set(nameof(MarketDepthSources), MarketDepthSources);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Address = storage.GetValue(nameof(Address), Address);
		Token = storage.GetValue<SecureString>(nameof(Token));
		AggregationPeriod = storage.GetValue(nameof(AggregationPeriod), AggregationPeriod);
		MarketDepthLevels = storage.GetValue(nameof(MarketDepthLevels), MarketDepthLevels);
		MarketDepthSources = storage.GetValue(nameof(MarketDepthSources), MarketDepthSources);
	}
}
