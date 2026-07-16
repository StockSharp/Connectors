namespace StockSharp.MiraeSharekhan;

/// <summary>Mirae Asset Sharekhan specific order condition.</summary>
[DataContract]
[Serializable]
public class MiraeSharekhanOrderCondition : OrderCondition
{
	private const string _product = "Product";
	private const string _rmsCode = "RmsCode";
	private const string _triggerPrice = "TriggerPrice";
	private const string _disclosedVolume = "DisclosedVolume";
	private const string _isAfterHours = "IsAfterHours";
	private const string _instrumentType = "InstrumentType";
	private const string _strikePrice = "StrikePrice";
	private const string _optionType = "OptionType";
	private const string _expiryDate = "ExpiryDate";

	/// <summary>Order product.</summary>
	[DataMember]
	public MiraeSharekhanProducts? Product
	{
		get => Parameters.TryGetValue(_product)?.To<MiraeSharekhanProducts?>();
		set => Parameters[_product] = value;
	}

	/// <summary>Native risk-management code. The standard cash value is <c>ANY</c>.</summary>
	[DataMember]
	public string RmsCode
	{
		get => Parameters.TryGetValue(_rmsCode)?.ToString();
		set => Parameters[_rmsCode] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	public decimal? TriggerPrice
	{
		get => Parameters.TryGetValue(_triggerPrice)?.To<decimal?>();
		set => Parameters[_triggerPrice] = value;
	}

	/// <summary>Publicly disclosed volume.</summary>
	[DataMember]
	public decimal? DisclosedVolume
	{
		get => Parameters.TryGetValue(_disclosedVolume)?.To<decimal?>();
		set => Parameters[_disclosedVolume] = value;
	}

	/// <summary>Whether to submit the order for the after-hours session.</summary>
	[DataMember]
	public bool IsAfterHours
	{
		get => Parameters.TryGetValue(_isAfterHours)?.To<bool>() == true;
		set => Parameters[_isAfterHours] = value;
	}

	/// <summary>Native derivative instrument type.</summary>
	[DataMember]
	public MiraeSharekhanInstrumentTypes? InstrumentType
	{
		get => Parameters.TryGetValue(_instrumentType)?.To<MiraeSharekhanInstrumentTypes?>();
		set => Parameters[_instrumentType] = value;
	}

	/// <summary>Derivative strike price.</summary>
	[DataMember]
	public decimal? StrikePrice
	{
		get => Parameters.TryGetValue(_strikePrice)?.To<decimal?>();
		set => Parameters[_strikePrice] = value;
	}

	/// <summary>Derivative option type.</summary>
	[DataMember]
	public OptionTypes? OptionType
	{
		get => Parameters.TryGetValue(_optionType)?.To<OptionTypes?>();
		set => Parameters[_optionType] = value;
	}

	/// <summary>Derivative expiration date.</summary>
	[DataMember]
	public DateTime? ExpiryDate
	{
		get => Parameters.TryGetValue(_expiryDate)?.To<DateTime?>();
		set => Parameters[_expiryDate] = value;
	}
}
