namespace StockSharp.CQG.Native;

static class CqgExtensions
{
	public static readonly TimeSpan[] TimeFrames =
	[
		TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3),
		TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15),
		TimeSpan.FromMinutes(30), TimeSpan.FromHours(1), TimeSpan.FromHours(2),
		TimeSpan.FromHours(4), TimeSpan.FromDays(1), TimeSpan.FromDays(7),
	];

	public static string ToCqgSymbol(this SecurityId securityId)
		=> (securityId.Native as string).IsEmpty(securityId.SecurityCode)
			.ThrowIfEmpty("CQG symbol");

	public static SecurityId ToSecurityId(this ContractMetadata metadata)
		=> new()
		{
			SecurityCode = metadata.ContractSymbol.IsEmpty(metadata.DialectContractSymbol),
			BoardCode = metadata.Mic.IsEmpty("CQG"),
		};

	public static SecurityTypes ToSecurityType(this ContractMetadata metadata)
		=> metadata.CfiCode.IsEmpty() ? SecurityTypes.Future : char.ToUpperInvariant(metadata.CfiCode[0]) switch
		{
			'E' => SecurityTypes.Stock,
			'D' => SecurityTypes.Bond,
			'C' => SecurityTypes.Fund,
			'O' => SecurityTypes.Option,
			'F' => SecurityTypes.Future,
			'I' => SecurityTypes.Index,
			_ => SecurityTypes.Future,
		};

	public static decimal ToDecimal(this CqgDecimal value)
	{
		if (value == null)
			return 0;
		var result = (decimal)value.Significand;
		if (value.Exponent > 0)
		{
			for (var i = 0; i < value.Exponent; i++)
				result *= 10;
		}
		else
		{
			for (var i = 0; i > value.Exponent; i--)
				result /= 10;
		}
		return result;
	}

	public static CqgDecimal ToCqgDecimal(this decimal value)
	{
		var bits = decimal.GetBits(value);
		var scale = (bits[3] >> 16) & 0x7F;
		var unscaled = value;
		for (var i = 0; i < scale; i++)
			unscaled *= 10;
		return new()
		{
			Significand = checked((long)decimal.Truncate(unscaled)),
			Exponent = -scale,
		};
	}

	public static long ToScaledPrice(this decimal value, double scale)
	{
		if (scale <= 0)
			throw new InvalidOperationException("CQG contract returned a non-positive price scale.");
		return checked((long)Math.Round(value / (decimal)scale, MidpointRounding.AwayFromZero));
	}

	public static decimal ToPrice(this long value, double scale)
		=> value * (decimal)scale;

	public static (uint unit, uint count) ToBarUnit(this TimeSpan timeFrame)
	{
		if (timeFrame.TotalMinutes is >= 1 and < 60 && timeFrame.TotalMinutes == Math.Truncate(timeFrame.TotalMinutes))
			return (8, (uint)timeFrame.TotalMinutes);
		if (timeFrame.TotalHours is >= 1 and < 24 && timeFrame.TotalHours == Math.Truncate(timeFrame.TotalHours))
			return (7, (uint)timeFrame.TotalHours);
		if (timeFrame == TimeSpan.FromDays(1))
			return (6, 1);
		if (timeFrame == TimeSpan.FromDays(7))
			return (5, 1);
		throw new ArgumentOutOfRangeException(nameof(timeFrame), timeFrame, "CQG supports whole-minute, whole-hour, daily, and weekly bars.");
	}
}
