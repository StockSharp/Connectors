namespace StockSharp.Deriv;

/// <summary>Deriv contract types.</summary>
[DataContract]
public enum DerivContractTypes
{
	/// <summary>Ends higher than the barrier.</summary>
	[EnumMember(Value = "HIGHER")]
	Higher,
	/// <summary>Ends lower than the barrier.</summary>
	[EnumMember(Value = "LOWER")]
	Lower,
	/// <summary>Up multiplier contract.</summary>
	[EnumMember(Value = "MULTUP")]
	MultiplierUp,
	/// <summary>Down multiplier contract.</summary>
	[EnumMember(Value = "MULTDOWN")]
	MultiplierDown,
	/// <summary>Ends outside the barriers.</summary>
	[EnumMember(Value = "UPORDOWN")]
	UpOrDown,
	/// <summary>Ends between the barriers.</summary>
	[EnumMember(Value = "EXPIRYRANGE")]
	ExpiryRange,
	/// <summary>Touches the barrier.</summary>
	[EnumMember(Value = "ONETOUCH")]
	OneTouch,
	/// <summary>European call contract.</summary>
	[EnumMember(Value = "CALLE")]
	CallEnd,
	/// <summary>Asian down contract.</summary>
	[EnumMember(Value = "ASIAND")]
	AsianDown,
	/// <summary>European expiry-range contract.</summary>
	[EnumMember(Value = "EXPIRYRANGEE")]
	ExpiryRangeEnd,
	/// <summary>Last digit differs.</summary>
	[EnumMember(Value = "DIGITDIFF")]
	DigitDiffers,
	/// <summary>Last digit matches.</summary>
	[EnumMember(Value = "DIGITMATCH")]
	DigitMatches,
	/// <summary>Last digit is over the selected digit.</summary>
	[EnumMember(Value = "DIGITOVER")]
	DigitOver,
	/// <summary>European put contract.</summary>
	[EnumMember(Value = "PUTE")]
	PutEnd,
	/// <summary>Last digit is under the selected digit.</summary>
	[EnumMember(Value = "DIGITUNDER")]
	DigitUnder,
	/// <summary>Does not touch the barrier.</summary>
	[EnumMember(Value = "NOTOUCH")]
	NoTouch,
	/// <summary>Rise contract.</summary>
	[EnumMember(Value = "CALL")]
	Call,
	/// <summary>Stays between the barriers.</summary>
	[EnumMember(Value = "RANGE")]
	Range,
	/// <summary>Last digit is odd.</summary>
	[EnumMember(Value = "DIGITODD")]
	DigitOdd,
	/// <summary>Fall contract.</summary>
	[EnumMember(Value = "PUT")]
	Put,
	/// <summary>Asian up contract.</summary>
	[EnumMember(Value = "ASIANU")]
	AsianUp,
	/// <summary>European expiry-miss contract.</summary>
	[EnumMember(Value = "EXPIRYMISSE")]
	ExpiryMissEnd,
	/// <summary>Ends outside the expiry range.</summary>
	[EnumMember(Value = "EXPIRYMISS")]
	ExpiryMiss,
	/// <summary>Last digit is even.</summary>
	[EnumMember(Value = "DIGITEVEN")]
	DigitEven,
	/// <summary>Selected tick is the highest.</summary>
	[EnumMember(Value = "TICKHIGH")]
	TickHigh,
	/// <summary>Selected tick is the lowest.</summary>
	[EnumMember(Value = "TICKLOW")]
	TickLow,
	/// <summary>Reset call contract.</summary>
	[EnumMember(Value = "RESETCALL")]
	ResetCall,
	/// <summary>Reset put contract.</summary>
	[EnumMember(Value = "RESETPUT")]
	ResetPut,
	/// <summary>High tick run contract.</summary>
	[EnumMember(Value = "RUNHIGH")]
	RunHigh,
	/// <summary>Low tick run contract.</summary>
	[EnumMember(Value = "RUNLOW")]
	RunLow,
	/// <summary>Accumulator contract.</summary>
	[EnumMember(Value = "ACCU")]
	Accumulator,
	/// <summary>Vanilla long call.</summary>
	[EnumMember(Value = "VANILLALONGCALL")]
	VanillaLongCall,
	/// <summary>Vanilla long put.</summary>
	[EnumMember(Value = "VANILLALONGPUT")]
	VanillaLongPut,
	/// <summary>Long turbo contract.</summary>
	[EnumMember(Value = "TURBOSLONG")]
	TurbosLong,
	/// <summary>Short turbo contract.</summary>
	[EnumMember(Value = "TURBOSSHORT")]
	TurbosShort,
}

/// <summary>How the contract amount is interpreted.</summary>
[DataContract]
public enum DerivBasisTypes
{
	/// <summary>The amount is the stake.</summary>
	[EnumMember(Value = "stake")]
	Stake,
	/// <summary>The amount is the payout.</summary>
	[EnumMember(Value = "payout")]
	Payout,
}

/// <summary>Deriv contract duration units.</summary>
[DataContract]
public enum DerivDurationUnits
{
	/// <summary>Seconds.</summary>
	[EnumMember(Value = "s")]
	Seconds,
	/// <summary>Minutes.</summary>
	[EnumMember(Value = "m")]
	Minutes,
	/// <summary>Hours.</summary>
	[EnumMember(Value = "h")]
	Hours,
	/// <summary>Days.</summary>
	[EnumMember(Value = "d")]
	Days,
	/// <summary>Ticks.</summary>
	[EnumMember(Value = "t")]
	Ticks,
}
