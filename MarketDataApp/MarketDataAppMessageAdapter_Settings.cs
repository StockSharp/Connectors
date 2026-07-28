namespace StockSharp.MarketDataApp;

/// <summary>The message adapter for MarketData.app.</summary>
[MediaIcon(Media.MediaNames.marketdataapp)]
[Doc("topics/api/connectors/stock_market/marketdataapp.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.MarketDataDotAppKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.AmericaKey)]
[MessageAdapterCategory(
	MessageAdapterCategories.US |
	MessageAdapterCategories.Stock |
	MessageAdapterCategories.Options |
	MessageAdapterCategories.RealTime |
	MessageAdapterCategories.History |
	MessageAdapterCategories.Free |
	MessageAdapterCategories.Paid |
	MessageAdapterCategories.Level1 |
	MessageAdapterCategories.Candles)]
public partial class MarketDataAppMessageAdapter :
	MessageAdapter,
	ITokenAdapter
{
	private static readonly Uri _defaultRestEndpoint =
		new("https://api.marketdata.app/v1/");

	/// <inheritdoc />
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TokenKey,
		Description = LocalizedStrings.MarketDataAppTokenDescKey,
		GroupName = LocalizedStrings.ConnectionKey,
		Order = 0)]
	[BasicSetting]
	public SecureString Token { get; set; }

	/// <summary>MarketData.app REST API root endpoint.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.RestEndpointKey,
		Description = LocalizedStrings.MarketDataAppRestEndpointDescKey,
		GroupName = LocalizedStrings.AddressesKey,
		Order = 0)]
	[BasicSetting]
	public Uri RestEndpoint { get; set; } = _defaultRestEndpoint;

	/// <summary>Whether extended-hours stock data is requested.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.ExtendedHoursKey,
		Description = LocalizedStrings.MarketDataAppExtendedHoursDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 0)]
	public bool ExtendedHours { get; set; }

	/// <summary>Whether stock candles are adjusted for splits.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.AdjustedCandlesKey,
		Description = LocalizedStrings.MarketDataAppAdjustSplitsDescKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 1)]
	public bool AdjustSplits { get; set; } = true;

	private int _maximumOptionContracts = 1000;

	/// <summary>Maximum option contracts returned by one lookup.</summary>
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.MaximumItemsKey,
		Description = LocalizedStrings.MaximumItemsKey,
		GroupName = LocalizedStrings.MarketDataLabelKey,
		Order = 2)]
	public int MaximumOptionContracts
	{
		get => _maximumOptionContracts;
		set => _maximumOptionContracts = value is >= 1 and <= 10000
			? value
			: throw new ArgumentOutOfRangeException(nameof(value),
				value, "Maximum option contract count must be " +
					"between 1 and 10000.");
	}

	/// <summary>Supported MarketData.app candle time frames.</summary>
	public static IEnumerable<TimeSpan> AllTimeFrames =>
		MarketDataAppExtensions.TimeFrames;

	/// <inheritdoc />
	public override void Save(SettingsStorage storage)
	{
		base.Save(storage);
		storage
			.Set(nameof(Token), Token)
			.Set(nameof(RestEndpoint), RestEndpoint)
			.Set(nameof(ExtendedHours), ExtendedHours)
			.Set(nameof(AdjustSplits), AdjustSplits)
			.Set(nameof(MaximumOptionContracts),
				MaximumOptionContracts);
	}

	/// <inheritdoc />
	public override void Load(SettingsStorage storage)
	{
		base.Load(storage);
		Token = storage.GetValue<SecureString>(nameof(Token));
		RestEndpoint = storage.GetValue(nameof(RestEndpoint),
			_defaultRestEndpoint);
		ExtendedHours = storage.GetValue(nameof(ExtendedHours),
			false);
		AdjustSplits = storage.GetValue(nameof(AdjustSplits), true);
		MaximumOptionContracts = storage.GetValue(
			nameof(MaximumOptionContracts), 1000);
	}
}
