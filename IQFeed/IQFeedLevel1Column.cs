namespace StockSharp.IQFeed;

/// <summary>
/// The column describing the Level1 data flow.
/// </summary>
public class IQFeedLevel1Column
{
	internal IQFeedLevel1Column(string name, Type type)
	{
		if (name.IsEmpty())
			throw new ArgumentNullException(nameof(name));

		Name = name;
		Type = type ?? throw new ArgumentNullException(nameof(type));

		Field = DefaultField;
	}

	internal IQFeedLevel1Column(string name, Type type, string format)
		: this(name, type)
	{
		if (format.IsEmpty())
			throw new ArgumentNullException(nameof(format));

		Format = format;
	}

	internal bool IsMandatory { get; init; }

	/// <summary>
	/// Column name.
	/// </summary>
	public string Name { get; private set; }

	/// <summary>
	/// Data type.
	/// </summary>
	public Type Type { get; }

	/// <summary>
	/// The data format (if <see cref="Type"/> equals to <see cref="DateTime"/> or <see cref="TimeSpan"/>).
	/// </summary>
	public string Format { get; private set; }

	internal const Level1Fields DefaultField = (Level1Fields)(-1);

	internal Level1Fields Field { get; set; }

	internal void UpdateName(string name, string format)
	{
		Name = name;
		Format = format;
	}

	internal bool TrySetMessageField(Level1ChangeMessage msg, Func<object> getValue, ref Level1UpdateContentFlags flags)
	{
		bool addChange()
		{
			var value = getValue();
			if(value == null)
				return false;

			if (Field == DefaultField)
			{
				//msg.AddValue(Name, value);
			}
			else
				msg.Changes.Add(Field, value);

			return true;
		}

		switch (Field)
		{
			case Level1Fields.OpenPrice:        return flags.Open && addChange();
			case Level1Fields.HighPrice:        return flags.High && addChange();
			case Level1Fields.LowPrice:         return flags.Low && addChange();
			case Level1Fields.ClosePrice:       return flags.Close && addChange();

			case Level1Fields.LastTradePrice:
			case Level1Fields.LastTradeId:
			case Level1Fields.LastTradeTime:
			case Level1Fields.LastTradeVolume:  return flags.IsTradeInfo && addChange();

			case Level1Fields.BestBidPrice:
			case Level1Fields.BestBidTime:
			case Level1Fields.BestBidVolume:    return flags.Bid && addChange();

			case Level1Fields.BestAskPrice:
			case Level1Fields.BestAskTime:
			case Level1Fields.BestAskVolume:    return flags.Ask && addChange();

			case Level1Fields.SettlementPrice:  return flags.Settlement && addChange();

			case Level1Fields.Volume:           return flags.Volume && addChange();

			default:
				return addChange();
		}
	}

	/// <inheritdoc />
	public override string ToString()
	{
		return Name;
	}
}

readonly struct Level1UpdateContentFlags
{
	public Level1UpdateContentFlags(string contents)
	{
		LastQualifiedTrade   = false;
		ExtendedTrade        = false;
		OtherTrade           = false;
		Bid                  = false;
		Ask                  = false;
		Open                 = false;
		High                 = false;
		Low                  = false;
		Close                = false;
		Settlement           = false;
		Volume               = false;

		if(contents.IsEmptyOrWhiteSpace())
			return;

		// http://www.iqfeed.net/dev/api/docs/Level1UpdateSummaryMessage.cfm
		foreach (var c in contents)
		{
			switch (c)
			{
				case 'C': LastQualifiedTrade  = true; break;
				case 'E': ExtendedTrade       = true; break;
				case 'O': OtherTrade          = true; break;
				case 'b': Bid                 = true; break;
				case 'a': Ask                 = true; break;
				case 'o': Open                = true; break;
				case 'h': High                = true; break;
				case 'l': Low                 = true; break;
				case 'c': Close               = true; break;
				case 's': Settlement          = true; break;
				case 'v': Volume              = true; break;

				default:
					throw new ArgumentOutOfRangeException($"unexpected contents char '{c}'");
			}
		}
	}

	public bool IsTradeInfo => LastQualifiedTrade;

	public readonly bool LastQualifiedTrade;
	public readonly bool ExtendedTrade;
	public readonly bool OtherTrade;
	public readonly bool Bid;
	public readonly bool Ask;
	public readonly bool Open;
	public readonly bool High;
	public readonly bool Low;
	public readonly bool Close;
	public readonly bool Settlement;
	public readonly bool Volume;
}
