namespace StockSharp.InteractiveBrokers;

/// <summary>
/// Shares exclusions types.
/// </summary>
public enum ScannerFilterStockExcludes
{
	/// <summary>
	/// Not to exclude anything.
	/// </summary>
	All,

	/// <summary>
	/// To exclude <see cref="Etf"/>.
	/// </summary>
	Stock,

	/// <summary>
	/// Only the Exchange-traded fund.
	/// </summary>
	Etf
}

/// <summary>
/// Scanner filter types.
/// </summary>
public enum ScannerFilterTypes
{
	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_OPT_VOL_PUT_CALL_RATIO")]
	LowOptVolPutCallRatio,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_OPT_IMP_VOLAT_OVER_HIST")]
	HighOptImpVolatOverHist,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_OPT_IMP_VOLAT_OVER_HIST")]
	LowOptImpVolatOverHist,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_OPT_IMP_VOLAT")]
	HighOptImpVolat,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_OPT_IMP_VOLAT_GAIN")]
	TopOptImpVolatGain,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_OPT_IMP_VOLAT_LOSE")]
	TopOptImpVolatLose,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_OPT_VOLUME_PUT_CALL_RATIO")]
	HighOptVolumePutCallRatio,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_OPT_VOLUME_PUT_CALL_RATIO")]
	LowOptVolumePutCallRatio,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("OPT_VOLUME_MOST_ACTIVE")]
	OptVolumeMostActive,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HOT_BY_OPT_VOLUME")]
	HotByOptVolume,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_OPT_OPEN_INTEREST_PUT_CALL_RATIO")]
	HighOptOpenInterestPutCallRatio,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_OPT_OPEN_INTEREST_PUT_CALL_RATIO")]
	LowOptOpenInterestPutCallRatio,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_PERC_GAIN")]
	TopPercGain,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("MOST_ACTIVE")]
	MostActive,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_PERC_LOSE")]
	TopPercLose,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HOT_BY_VOLUME")]
	HotByVolume,

	//[NativeValue("TOP_PERC_GAIN")]
	//TOP_PERC_GAIN,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HOT_BY_PRICE")]
	HotByPrice,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_TRADE_COUNT")]
	TopTradeCount,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_TRADE_RATE")]
	TopTradeRate,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_PRICE_RANGE")]
	TopPriceRange,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HOT_BY_PRICE_RANGE")]
	HotByPriceRange,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_VOLUME_RATE")]
	TopVolumeRate,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_OPT_IMP_VOLAT")]
	LowOptImpVolat,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("OPT_OPEN_INTEREST_MOST_ACTIVE")]
	OptOpenInterestMostActive,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("NOT_OPEN")]
	NotOpen,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HALTED")]
	Halted,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_OPEN_PERC_GAIN")]
	TopOpenPercGain,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("TOP_OPEN_PERC_LOSE")]
	TopOpenPercLose,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_OPEN_GAP")]
	HighOpenGap,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_OPEN_GAP")]
	LowOpenGap,

	//[NativeValue("LOW_OPT_IMP_VOLAT")]
	//LOW_OPT_IMP_VOLAT,

	//[NativeValue("TOP_OPT_IMP_VOLAT_GAIN")]
	//TOP_OPT_IMP_VOLAT_GAIN,

	//[NativeValue("TOP_OPT_IMP_VOLAT_LOSE")]
	//TOP_OPT_IMP_VOLAT_LOSE,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_VS_13W_HL")]
	HighVs13WHl,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_VS_13W_HL")]
	LowVs13WHl,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_VS_26W_HL")]
	HighVs26WHl,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_VS_26W_HL")]
	LowVs26WHl,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_VS_52W_HL")]
	HighVs52WHl,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_VS_52W_HL")]
	LowVs52WHl,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("HIGH_SYNTH_BID_REV_NAT_YIELD")]
	HighSynthBidRevNatYield,

	/// <summary>
	/// 
	/// </summary>
	[NativeValue("LOW_SYNTH_BID_REV_NAT_YIELD")]
	LowSynthBidRevNatYield,
}

/// <summary>
/// Filter settings of the scanner starting via <see cref="ScannerMarketDataMessage"/>.
/// </summary>
public class ScannerFilter
{
	/// <summary>
	/// Initializes a new instance of the <see cref="ScannerFilter"/>.
	/// </summary>
	public ScannerFilter()
	{
	}

	/// <summary>
	/// The number of strings in the query.
	/// </summary>
	public int? RowCount { get; set; }

	/// <summary>
	/// Security type.
	/// </summary>
	public string SecurityType { get; set; }

	/// <summary>
	/// Exchange board.
	/// </summary>
	public string BoardCode { get; set; }

	/// <summary>
	/// Scan code.
	/// </summary>
	public ScannerFilterTypes? ScanCode { get; set; }

	/// <summary>
	/// The upper limit of the instrument market price.
	/// </summary>
	public decimal? AbovePrice { get; set; }

	/// <summary>
	/// The lower limit of the instrument market price.
	/// </summary>
	public decimal? BelowPrice { get; set; }

	/// <summary>
	/// The upper limit of the instrument trading volume.
	/// </summary>
	public int? AboveVolume { get; set; }

	/// <summary>
	/// The upper limit of the option trading volume.
	/// </summary>
	public int? AverageOptionVolumeAbove { get; set; }

	/// <summary>
	/// The upper limit of capitalization.
	/// </summary>
	public decimal? MarketCapAbove { get; set; }

	/// <summary>
	/// The lower limit of capitalization.
	/// </summary>
	public decimal? MarketCapBelow { get; set; }

	/// <summary>
	/// The upper limit of the Moody rating.
	/// </summary>
	public string MoodyRatingAbove { get; set; }

	/// <summary>
	/// The lower limit of the Moody rating.
	/// </summary>
	public string MoodyRatingBelow { get; set; }

	/// <summary>
	/// The upper limit of the SP rating.
	/// </summary>
	public string SpRatingAbove { get; set; }

	/// <summary>
	/// The lower limit of the SP rating.
	/// </summary>
	public string SpRatingBelow { get; set; }

	/// <summary>
	/// The upper limit of the instrument maturity date.
	/// </summary>
	public DateTime? MaturityDateAbove { get; set; }

	/// <summary>
	/// The lower limit of the instrument maturity date.
	/// </summary>
	public DateTime? MaturityDateBelow { get; set; }

	/// <summary>
	/// The upper limit of the coupon rate.
	/// </summary>
	public decimal? CouponRateAbove { get; set; }

	/// <summary>
	/// The lower limit of the coupon rate.
	/// </summary>
	public decimal? CouponRateBelow { get; set; }

	/// <summary>
	/// To exclude convertible bonds.
	/// </summary>
	public bool? ExcludeConvertibleBonds { get; set; }

	/// <summary>
	/// Extended settings. For more details see http://www.interactivebrokers.com/en/software/tws/usersguidebook/technicalanalytics/market_scanner_types.htm.
	/// </summary>
	public string ScannerSettingPairs { get; set; }

	/// <summary>
	/// The shares exclusions type.
	/// </summary>
	public ScannerFilterStockExcludes StockTypeExclude { get; set; }
}