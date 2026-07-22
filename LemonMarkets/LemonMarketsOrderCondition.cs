namespace StockSharp.LemonMarkets;

/// <summary>Additional parameters for lemon.markets orders.</summary>
[DataContract]
[Serializable]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LemonMarketsKey)]
public sealed class LemonMarketsOrderCondition : OrderCondition
{
	private decimal? _feeAmount;
	private decimal? _feePercent;
	private string _securitiesAccountId;
	private bool? _isAppropriatenessConsentAccepted;

	/// <summary>Fixed partner fee in EUR. Mutually exclusive with <see cref="FeePercent"/>.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LemonMarketsFeeAmountKey,
		Description = LocalizedStrings.LemonMarketsFeeAmountDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 0)]
	public decimal? FeeAmount
	{
		get => _feeAmount;
		set
		{
			_feeAmount = value;
			Parameters[nameof(FeeAmount)] = value;
		}
	}

	/// <summary>Relative partner fee in percent. Mutually exclusive with <see cref="FeeAmount"/>.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings), Name = LocalizedStrings.LemonMarketsFeePercentKey,
		Description = LocalizedStrings.LemonMarketsFeePercentDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 1)]
	public decimal? FeePercent
	{
		get => _feePercent;
		set
		{
			_feePercent = value;
			Parameters[nameof(FeePercent)] = value;
		}
	}

	/// <summary>Securities-account identifier used for execution and settlement.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsSecuritiesAccountKey,
		Description = LocalizedStrings.LemonMarketsSecuritiesAccountDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 2)]
	public string SecuritiesAccountId
	{
		get => _securitiesAccountId;
		set
		{
			_securitiesAccountId = value;
			Parameters[nameof(SecuritiesAccountId)] = value;
		}
	}

	/// <summary>Consent to execute an order that requires an appropriateness acknowledgement.</summary>
	[DataMember]
	[Display(ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.LemonMarketsAppropriatenessConsentKey,
		Description = LocalizedStrings.LemonMarketsAppropriatenessConsentDescKey,
		GroupName = LocalizedStrings.GeneralKey, Order = 3)]
	public bool? IsAppropriatenessConsentAccepted
	{
		get => _isAppropriatenessConsentAccepted;
		set
		{
			_isAppropriatenessConsentAccepted = value;
			Parameters[nameof(IsAppropriatenessConsentAccepted)] = value;
		}
	}
}
