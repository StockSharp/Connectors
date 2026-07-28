namespace StockSharp.SimFin;

static class SimFinMessageTypes
{
	public const MessageTypes Fundamental = (MessageTypes)(-5011);
}

/// <summary>Custom SimFin data types.</summary>
public static class SimFinDataTypes
{
	/// <summary>Normalized financial-statement observations.</summary>
	public static readonly DataType Fundamentals =
		DataType.Create<SimFinFundamentalMessage>(true)
			.SetName("SimFin fundamentals")
			.Immutable();
}

/// <summary>A normalized SimFin financial-statement observation.</summary>
public class SimFinFundamentalMessage :
	BaseSubscriptionIdMessage<SimFinFundamentalMessage>,
	ISecurityIdMessage,
	IServerTimeMessage
{
	/// <summary>Initialize a fundamental message.</summary>
	public SimFinFundamentalMessage()
		: base(SimFinMessageTypes.Fundamental)
	{
	}

	/// <inheritdoc />
	public SecurityId SecurityId { get; set; }

	/// <inheritdoc />
	public DateTime ServerTime { get; set; }

	/// <summary>Statement type, such as pl, bs, cf, or derived.</summary>
	public string Statement { get; set; }

	/// <summary>Financial metric name.</summary>
	public string Metric { get; set; }

	/// <summary>Reported value preserved as text.</summary>
	public string RawValue { get; set; }

	/// <summary>Numeric value when representable as decimal.</summary>
	public decimal? Value { get; set; }

	/// <summary>Reporting currency.</summary>
	public string Currency { get; set; }

	/// <summary>Fiscal year.</summary>
	public int? FiscalYear { get; set; }

	/// <summary>Fiscal period.</summary>
	public string FiscalPeriod { get; set; }

	/// <summary>Report date.</summary>
	public DateTime? ReportDate { get; set; }

	/// <summary>Publication date.</summary>
	public DateTime? PublishDate { get; set; }

	/// <summary>Source filing reference.</summary>
	public string Source { get; set; }

	/// <summary>Whether the value was restated.</summary>
	public bool? Restated { get; set; }

	/// <inheritdoc />
	public override DataType DataType => SimFinDataTypes.Fundamentals;

	/// <inheritdoc />
	public override Message Clone()
	{
		var copy = new SimFinFundamentalMessage
		{
			SecurityId = SecurityId,
			ServerTime = ServerTime,
			Statement = Statement,
			Metric = Metric,
			RawValue = RawValue,
			Value = Value,
			Currency = Currency,
			FiscalYear = FiscalYear,
			FiscalPeriod = FiscalPeriod,
			ReportDate = ReportDate,
			PublishDate = PublishDate,
			Source = Source,
			Restated = Restated,
		};
		CopyTo(copy);
		return copy;
	}
}
