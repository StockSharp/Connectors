namespace StockSharp.Settrade;

/// <summary>Settrade account market.</summary>
[DataContract]
[Serializable]
public enum SettradeAccountTypes
{
	/// <summary>SET equity account.</summary>
	[EnumMember]
	Equity,

	/// <summary>TFEX derivatives account.</summary>
	[EnumMember]
	Derivatives,
}

/// <summary>Derivatives order position effect.</summary>
[DataContract]
[Serializable]
public enum SettradeOrderPositions
{
	/// <summary>Let Settrade select open or close.</summary>
	[EnumMember]
	Auto,

	/// <summary>Open a position.</summary>
	[EnumMember]
	Open,

	/// <summary>Close a position.</summary>
	[EnumMember]
	Close,
}

/// <summary>Settrade stop trigger condition.</summary>
[DataContract]
[Serializable]
public enum SettradeStopConditions
{
	/// <summary>No stop trigger.</summary>
	[EnumMember]
	None,

	/// <summary>Last price is greater than or equal to the stop price.</summary>
	[EnumMember]
	LastPaidOrHigher,

	/// <summary>Last price is less than or equal to the stop price.</summary>
	[EnumMember]
	LastPaidOrLower,

	/// <summary>Ask is greater than or equal to the stop price.</summary>
	[EnumMember]
	AskOrHigher,

	/// <summary>Ask is less than or equal to the stop price.</summary>
	[EnumMember]
	AskOrLower,

	/// <summary>Bid is greater than or equal to the stop price.</summary>
	[EnumMember]
	BidOrHigher,

	/// <summary>Bid is less than or equal to the stop price.</summary>
	[EnumMember]
	BidOrLower,
}

/// <summary>Settrade-specific order parameters.</summary>
[Serializable]
[DataContract]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SettradeKey)]
public sealed class SettradeOrderCondition : OrderCondition
{
	/// <summary>Submit an equity order as NVDR.</summary>
	[DataMember]
	public bool IsNvdr
	{
		get => (bool?)Parameters.TryGetValue(nameof(IsNvdr)) ?? false;
		set => Parameters[nameof(IsNvdr)] = value;
	}

	/// <summary>Visible iceberg quantity.</summary>
	[DataMember]
	public decimal? IcebergVolume
	{
		get => (decimal?)Parameters.TryGetValue(nameof(IcebergVolume));
		set => Parameters[nameof(IcebergVolume)] = value;
	}

	/// <summary>Derivatives position effect.</summary>
	[DataMember]
	public SettradeOrderPositions Position
	{
		get => (SettradeOrderPositions?)Parameters.TryGetValue(
			nameof(Position)) ?? SettradeOrderPositions.Auto;
		set => Parameters[nameof(Position)] = value;
	}

	/// <summary>Stop trigger condition.</summary>
	[DataMember]
	public SettradeStopConditions StopCondition
	{
		get => (SettradeStopConditions?)Parameters.TryGetValue(
			nameof(StopCondition)) ?? SettradeStopConditions.None;
		set => Parameters[nameof(StopCondition)] = value;
	}

	/// <summary>Stop trigger price.</summary>
	[DataMember]
	public decimal? StopPrice
	{
		get => (decimal?)Parameters.TryGetValue(nameof(StopPrice));
		set => Parameters[nameof(StopPrice)] = value;
	}

	/// <summary>Stop reference symbol.</summary>
	[DataMember]
	public string StopSymbol
	{
		get => (string)Parameters.TryGetValue(nameof(StopSymbol));
		set => Parameters[nameof(StopSymbol)] = value;
	}

	/// <summary>Optional Settrade trigger-session value.</summary>
	[DataMember]
	public string TriggerSession
	{
		get => (string)Parameters.TryGetValue(nameof(TriggerSession));
		set => Parameters[nameof(TriggerSession)] = value;
	}

	/// <summary>Allow Settrade order-screening warnings to be bypassed.</summary>
	[DataMember]
	public bool BypassWarning
	{
		get => (bool?)Parameters.TryGetValue(nameof(BypassWarning)) ??
			false;
		set => Parameters[nameof(BypassWarning)] = value;
	}

	/// <summary>Good-till-date expiration.</summary>
	[DataMember]
	public DateTime? ValidTillDate
	{
		get => (DateTime?)Parameters.TryGetValue(nameof(ValidTillDate));
		set => Parameters[nameof(ValidTillDate)] = value;
	}
}
