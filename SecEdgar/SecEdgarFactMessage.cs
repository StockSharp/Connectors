namespace StockSharp.SecEdgar;

static class SecEdgarMessageTypes
{
	public const MessageTypes Fact = (MessageTypes)(-5010);
}

/// <summary>Custom SEC EDGAR data types.</summary>
public static class SecEdgarDataTypes
{
	/// <summary>Extracted XBRL company facts.</summary>
	public static readonly DataType CompanyFacts =
		DataType.Create<SecEdgarFactMessage>(true)
			.SetName("SEC company facts")
			.Immutable();
}

/// <summary>An extracted XBRL company fact.</summary>
public class SecEdgarFactMessage :
	BaseSubscriptionIdMessage<SecEdgarFactMessage>,
	ISecurityIdMessage,
	IServerTimeMessage
{
	/// <summary>Initialize a company fact message.</summary>
	public SecEdgarFactMessage()
		: base(SecEdgarMessageTypes.Fact)
	{
	}

	/// <inheritdoc />
	public SecurityId SecurityId { get; set; }

	/// <inheritdoc />
	public DateTime ServerTime { get; set; }

	/// <summary>XBRL taxonomy.</summary>
	public string Taxonomy { get; set; }

	/// <summary>XBRL concept name.</summary>
	public string Concept { get; set; }

	/// <summary>Human-readable concept label.</summary>
	public string Label { get; set; }

	/// <summary>Concept description.</summary>
	public string Description { get; set; }

	/// <summary>Reported unit.</summary>
	public string Unit { get; set; }

	/// <summary>Reported value preserved as text.</summary>
	public string Value { get; set; }

	/// <summary>Numeric value when representable as decimal.</summary>
	public decimal? NumericValue { get; set; }

	/// <summary>Observation period start.</summary>
	public DateTime? StartDate { get; set; }

	/// <summary>Observation period end.</summary>
	public DateTime? EndDate { get; set; }

	/// <summary>EDGAR accession number.</summary>
	public string AccessionNumber { get; set; }

	/// <summary>Fiscal year.</summary>
	public int? FiscalYear { get; set; }

	/// <summary>Fiscal period.</summary>
	public string FiscalPeriod { get; set; }

	/// <summary>Filing form.</summary>
	public string Form { get; set; }

	/// <summary>SEC XBRL frame.</summary>
	public string Frame { get; set; }

	/// <inheritdoc />
	public override DataType DataType =>
		SecEdgarDataTypes.CompanyFacts;

	/// <inheritdoc />
	public override Message Clone()
	{
		var copy = new SecEdgarFactMessage
		{
			SecurityId = SecurityId,
			ServerTime = ServerTime,
			Taxonomy = Taxonomy,
			Concept = Concept,
			Label = Label,
			Description = Description,
			Unit = Unit,
			Value = Value,
			NumericValue = NumericValue,
			StartDate = StartDate,
			EndDate = EndDate,
			AccessionNumber = AccessionNumber,
			FiscalYear = FiscalYear,
			FiscalPeriod = FiscalPeriod,
			Form = Form,
			Frame = Frame,
		};
		CopyTo(copy);
		return copy;
	}
}
