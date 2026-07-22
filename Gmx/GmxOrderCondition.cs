namespace StockSharp.Gmx;

/// <summary>GMX advanced order kinds.</summary>
[DataContract]
public enum GmxOrderKinds
{
	/// <summary>Regular market or limit order.</summary>
	[EnumMember]
	Regular,

	/// <summary>Stop-market increase order.</summary>
	[EnumMember]
	StopMarket,

	/// <summary>Take-profit decrease order.</summary>
	[EnumMember]
	TakeProfit,

	/// <summary>Stop-loss decrease order.</summary>
	[EnumMember]
	StopLoss,

	/// <summary>Time-weighted average price order.</summary>
	[EnumMember]
	Twap,
}

/// <summary>GMX order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.GmxKey)]
public class GmxOrderCondition : OrderCondition
{
	/// <summary>Advanced order kind.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.TypeKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 0)]
	public GmxOrderKinds OrderKind
	{
		get => (GmxOrderKinds?)Parameters.TryGetValue(nameof(OrderKind)) ??
			GmxOrderKinds.Regular;
		set => Parameters[nameof(OrderKind)] = value;
	}

	/// <summary>Conditional-order trigger price.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.StopPriceKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 1)]
	public decimal? TriggerPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TriggerPrice));
		set => Parameters[nameof(TriggerPrice)] = value;
	}

	/// <summary>Collateral token symbol for an increase or swap.</summary>
	[DataMember]
	[Display(
		Name = "Collateral token",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 2)]
	public string CollateralToken
	{
		get => (string)Parameters.TryGetValue(nameof(CollateralToken));
		set => Parameters[nameof(CollateralToken)] = value;
	}

	/// <summary>Collateral amount in token units.</summary>
	[DataMember]
	[Display(
		Name = "Collateral amount",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 3)]
	public decimal? CollateralAmount
	{
		get => (decimal?)Parameters.TryGetValue(nameof(CollateralAmount));
		set => Parameters[nameof(CollateralAmount)] = value;
	}

	/// <summary>Token received by a decrease or swap.</summary>
	[DataMember]
	[Display(
		Name = "Receive token",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 4)]
	public string ReceiveToken
	{
		get => (string)Parameters.TryGetValue(nameof(ReceiveToken));
		set => Parameters[nameof(ReceiveToken)] = value;
	}

	/// <summary>Take-profit sidecar trigger for an increase.</summary>
	[DataMember]
	[Display(
		Name = "Take-profit price",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 5)]
	public decimal? TakeProfitPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(TakeProfitPrice));
		set => Parameters[nameof(TakeProfitPrice)] = value;
	}

	/// <summary>Stop-loss sidecar trigger for an increase.</summary>
	[DataMember]
	[Display(
		Name = "Stop-loss price",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 6)]
	public decimal? StopLossPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopLossPrice));
		set => Parameters[nameof(StopLossPrice)] = value;
	}

	/// <summary>Preserve leverage when decreasing a position.</summary>
	[DataMember]
	[Display(
		Name = "Keep leverage",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 7)]
	public bool IsKeepLeverage
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsKeepLeverage)) ?? false;
		set => Parameters[nameof(IsKeepLeverage)] = value;
	}

	/// <summary>Automatically cancel an unexecutable order.</summary>
	[DataMember]
	[Display(
		Name = "Auto cancel",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 8)]
	public bool IsAutoCancel
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsAutoCancel)) ?? true;
		set => Parameters[nameof(IsAutoCancel)] = value;
	}

	/// <summary>Per-order slippage in percent.</summary>
	[DataMember]
	[Display(
		ResourceType = typeof(LocalizedStrings),
		Name = LocalizedStrings.SlippageKey,
		GroupName = LocalizedStrings.TransactionKey,
		Order = 9)]
	public decimal? Slippage
	{
		get => (decimal?)Parameters.TryGetValue(nameof(Slippage));
		set => Parameters[nameof(Slippage)] = value;
	}

	/// <summary>TWAP duration.</summary>
	[DataMember]
	[Display(
		Name = "TWAP duration",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 10)]
	public TimeSpan TwapDuration
	{
		get => (TimeSpan?)Parameters.TryGetValue(nameof(TwapDuration)) ??
			TimeSpan.FromMinutes(10);
		set => Parameters[nameof(TwapDuration)] = value;
	}

	/// <summary>Number of TWAP parts.</summary>
	[DataMember]
	[Display(
		Name = "TWAP parts",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 11)]
	public int TwapParts
	{
		get => (int?)Parameters.TryGetValue(nameof(TwapParts)) ?? 2;
		set => Parameters[nameof(TwapParts)] = value;
	}

	/// <summary>Optional gas-payment token symbol.</summary>
	[DataMember]
	[Display(
		Name = "Gas token",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 12)]
	public string GasPaymentToken
	{
		get => (string)Parameters.TryGetValue(nameof(GasPaymentToken));
		set => Parameters[nameof(GasPaymentToken)] = value;
	}

	/// <summary>Optional GMX referral code.</summary>
	[DataMember]
	[Display(
		Name = "Referral code",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 13)]
	public string ReferralCode
	{
		get => (string)Parameters.TryGetValue(nameof(ReferralCode));
		set => Parameters[nameof(ReferralCode)] = value;
	}

	/// <summary>Optional UI-fee receiver address.</summary>
	[DataMember]
	[Display(
		Name = "UI fee receiver",
		GroupName = LocalizedStrings.TransactionKey,
		Order = 14)]
	public string UiFeeReceiver
	{
		get => (string)Parameters.TryGetValue(nameof(UiFeeReceiver));
		set => Parameters[nameof(UiFeeReceiver)] = value;
	}
}
