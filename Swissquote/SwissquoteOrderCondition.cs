namespace StockSharp.Swissquote;

/// <summary>Swissquote OpenWealth order condition.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SwissQuoteKey)]
public class SwissquoteOrderCondition : OrderCondition, IStopLossOrderCondition
{
	/// <summary>Stop activation price.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.StopPriceKey,
		Description = LocalizedStrings.StopPriceDescKey, GroupName = LocalizedStrings.StopLossKey, Order = 0)]
	public decimal? StopPrice
	{
		get => Parameters.TryGetValue(nameof(StopPrice))?.To<decimal?>();
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>Instrument identifier override sent to OpenWealth.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.SecurityIdKey,
		Description = LocalizedStrings.SecurityIdKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public string InstrumentIdentification
	{
		get => Parameters.TryGetValue(nameof(InstrumentIdentification))?.ToString();
		set => Parameters[nameof(InstrumentIdentification)] = value;
	}

	/// <summary>Instrument identifier type.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.TypeKey,
		Description = LocalizedStrings.TypeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public SwissquoteInstrumentIdentificationTypes InstrumentIdentificationType
	{
		get => Parameters.TryGetValue(nameof(InstrumentIdentificationType))?.To<SwissquoteInstrumentIdentificationTypes>()
			?? SwissquoteInstrumentIdentificationTypes.Auto;
		set => Parameters[nameof(InstrumentIdentificationType)] = value;
	}

	/// <summary>ISO 10383 Market Identifier Code.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.MarketKey,
		Description = LocalizedStrings.MarketKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public string MarketIdentificationCode
	{
		get => Parameters.TryGetValue(nameof(MarketIdentificationCode))?.ToString();
		set => Parameters[nameof(MarketIdentificationCode)] = value;
	}

	/// <summary>Instrument/listing currency.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.CurrencyKey,
		Description = LocalizedStrings.CurrencyDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 4)]
	public string Currency
	{
		get => Parameters.TryGetValue(nameof(Currency))?.ToString();
		set => Parameters[nameof(Currency)] = value;
	}

	/// <summary>Cash account currency. Leave empty for digital assets.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.PortfolioCurrencyKey,
		Description = LocalizedStrings.CurrencyDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 5)]
	public string CashAccountCurrency
	{
		get => Parameters.TryGetValue(nameof(CashAccountCurrency))?.ToString();
		set => Parameters[nameof(CashAccountCurrency)] = value;
	}

	/// <summary>Quantity representation.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.VolumeKey,
		Description = LocalizedStrings.VolumeKey + LocalizedStrings.Dot,
		GroupName = LocalizedStrings.GeneralKey, Order = 6)]
	public SwissquoteQuantityTypes QuantityType
	{
		get => Parameters.TryGetValue(nameof(QuantityType))?.To<SwissquoteQuantityTypes>()
			?? SwissquoteQuantityTypes.UnitsNumber;
		set => Parameters[nameof(QuantityType)] = value;
	}

	/// <summary>Whether the instrument is a Swissquote digital asset.</summary>
	[DataMember]
	public bool IsDigitalAsset
	{
		get => Parameters.TryGetValue(nameof(IsDigitalAsset))?.To<bool>() == true;
		set => Parameters[nameof(IsDigitalAsset)] = value;
	}

	/// <summary>Whether a derivative order opens a position.</summary>
	[DataMember]
	public bool IsOpenPosition
	{
		get => Parameters.TryGetValue(nameof(IsOpenPosition))?.To<bool>() == true;
		set => Parameters[nameof(IsOpenPosition)] = value;
	}

	/// <summary>Underlying symbol for an option or future.</summary>
	[DataMember]
	public string UnderlyingSymbol
	{
		get => Parameters.TryGetValue(nameof(UnderlyingSymbol))?.ToString();
		set => Parameters[nameof(UnderlyingSymbol)] = value;
	}

	/// <summary>Option type.</summary>
	[DataMember]
	public OptionTypes? OptionType
	{
		get => Parameters.TryGetValue(nameof(OptionType))?.To<OptionTypes?>();
		set => Parameters[nameof(OptionType)] = value;
	}

	/// <summary>Option exercise style.</summary>
	[DataMember]
	public SwissquoteOptionStyles? OptionStyle
	{
		get => Parameters.TryGetValue(nameof(OptionStyle))?.To<SwissquoteOptionStyles?>();
		set => Parameters[nameof(OptionStyle)] = value;
	}

	/// <summary>Option expiration cycle.</summary>
	[DataMember]
	public SwissquoteOptionExpirationTypes? OptionExpirationType
	{
		get => Parameters.TryGetValue(nameof(OptionExpirationType))?.To<SwissquoteOptionExpirationTypes?>();
		set => Parameters[nameof(OptionExpirationType)] = value;
	}

	/// <summary>Strike price.</summary>
	[DataMember]
	public decimal? StrikePrice
	{
		get => Parameters.TryGetValue(nameof(StrikePrice))?.To<decimal?>();
		set => Parameters[nameof(StrikePrice)] = value;
	}

	/// <summary>Maturity date.</summary>
	[DataMember]
	public DateTime? MaturityDate
	{
		get => Parameters.TryGetValue(nameof(MaturityDate))?.To<DateTime?>();
		set => Parameters[nameof(MaturityDate)] = value;
	}

	/// <summary>Contract multiplier.</summary>
	[DataMember]
	public decimal? Multiplier
	{
		get => Parameters.TryGetValue(nameof(Multiplier))?.To<decimal?>();
		set => Parameters[nameof(Multiplier)] = value;
	}

	decimal? IStopLossOrderCondition.ClosePositionPrice { get; set; }

	decimal? IStopLossOrderCondition.ActivationPrice
	{
		get => StopPrice;
		set => StopPrice = value;
	}

	bool IStopLossOrderCondition.IsTrailing
	{
		get => false;
		set { }
	}
}
