namespace StockSharp.IQFeed;

static class ExtendedMessageTypes
{
	public const MessageTypes System = (MessageTypes)(-3001);
	public const MessageTypes ListedMarket = (MessageTypes)(-3002);
	public const MessageTypes SecurityType = (MessageTypes)(-3003);
}

class IQFeedSystemMessage(IQFeed feed, string value) : Message(ExtendedMessageTypes.System)
{
    public IQFeed Feed { get; } = feed ?? throw new ArgumentNullException(nameof(feed));
    public string Value { get; } = value;

    public override Message Clone()
	{
		return new IQFeedSystemMessage(Feed, Value);
	}
}

class IQFeedSecurityTypeMessage(int id, string code, string name) : Message(ExtendedMessageTypes.SecurityType)
{
    public int Id { get; } = id;
    public string Code { get; } = code;
    public string Name { get; } = name;

    public override Message Clone()
	{
		return new IQFeedSecurityTypeMessage(Id, Code, Name);
	}
}

class IQFeedListedMarketMessage(int id, string code, string name, int parentId) : Message(ExtendedMessageTypes.ListedMarket)
{
    public int Id { get; } = id;
    public int ParentId { get; } = parentId;
    public string Code { get; } = code;
    public string Name { get; } = name;

    public override Message Clone() => new IQFeedListedMarketMessage(Id, Code, Name, ParentId);
}

class IQFeedMessage
{
	[AttributeUsage(AttributeTargets.Field)]
	private class StrValAttribute(string val) : Attribute
	{
        public string Val { get; } = val;
    }

	static readonly Dictionary<string, IQFeedMessage.MsgType> _msgTypes = [];

	static IQFeedMessage()
	{
		foreach (var mt in Enumerator.GetValues<IQFeedMessage.MsgType>())
		{
			var attr = mt.GetAttributeOfType<IQFeedMessage.StrValAttribute>();
			if(attr != null)
				_msgTypes.Add(attr.Val, mt);
		}
	}

	public const long InvalidRequestId = -1;
	private const string _noDataStr = "!NO_DATA!";

	public enum MsgType
	{
		Unknown,

		[StrVal("LS")] LookupSymbols, // "symbols" here means not only securities, but markets, security types, sic/niac codes, etc.
		[StrVal("LH")] LookupHistory,
		[StrVal("LM")] LookupMarketSummary,
		[StrVal("LN")] LookupNews,

		[StrVal("BH")] StreamCandleHistory,
		[StrVal("BC")] StreamCandleCompleted,
		[StrVal("BU")] StreamCandleUpdated,

		[StrVal("T")] Time,
		[StrVal("S")] System,
					  SystemClearDepth,
					  SystemStats,

		[StrVal("N")] News,

		[StrVal("F")] Fundamental,
		[StrVal("P")] L1Summary,
		[StrVal("Q")] L1Update,

		[StrVal("0")] L2PriceLevel,

		[StrVal("3")] L2OrderAdd,
		[StrVal("4")] L2OrderUpdate,
		[StrVal("5")] L2OrderDelete,
		[StrVal("6")] L2OrderSummary,

		[StrVal("7")] L2PriceLevelSummary,
		[StrVal("8")] L2PriceLevelUpdate,
		[StrVal("9")] L2PriceLevelDelete,

		// This is a valid symbol but it has no depth. The message is in the format q,SYMBOL.
		// This message indicates that a watch has been established for the symbol and you
		// should receive data when it becomes available as long as you have not unwatched by that time.
		[StrVal("q")] NoDepthAvailableYet,

		[StrVal("n")]         NotFound,
		[StrVal(_noDataStr)]  NoData,
		[StrVal("E")]         Error,
		[StrVal("!ENDMSG!")]  End,
	}

	private readonly string[] _data;
	private readonly byte _offset;

	public MsgType Type { get; }
	public long RequestId { get; }

	public string Message { get; }

	public string this[int idx] => _data[idx + _offset];

	public string RejoinFrom(int startIdx)
	{
		if(startIdx == 0 && _offset == 0)
			return Message;

		var idx = Message.IndexOfNth(',', 0, _offset + startIdx);
		return idx < 0 ?
			string.Empty :
			Message[(idx + 1)..];
	}

	public IQFeedMessage(string responseLine)
	{
		static long TryGetRequestId(string s)
		{
			var len = s.Length;
			if(len < 3 || s[0] != '#' || s[len - 1] != '#')
				return InvalidRequestId;
			return s.Substring(1, len - 2).To<long>();
		}

		_data = responseLine.SplitByComma();

		Message = responseLine;

		if (_data.Length == 1)
		{
			RequestId = InvalidRequestId;
			return;
		}

		RequestId = TryGetRequestId(_data[_offset]);

		if (RequestId != InvalidRequestId)
			++_offset;

		if (!_msgTypes.TryGetValue(_data[_offset], out var mt))
		{
			Type = IQFeedMessage.MsgType.Unknown;
			return;
		}

		++_offset;
		Type = mt;

		if (Type == MsgType.System && RequestId == InvalidRequestId)
		{
			switch (_data[_offset])
			{
				case "CLEAR DEPTH":
					++_offset;
					Type = MsgType.SystemClearDepth;
					break;
				case "STATS":
					++_offset;
					Type = MsgType.SystemStats;
					break;
			}
		}
		else if (Type == MsgType.Error && _data[_offset] == _noDataStr)
		{
			Type = MsgType.NoData;
			++_offset;
		}
	}
}

static class IQFeedMessageHelper
{
	public static bool IsErrorMessage(this IQFeedMessage.MsgType mt)
	{
        return mt switch
        {
            IQFeedMessage.MsgType.NotFound or IQFeedMessage.MsgType.Error => true,
            _ => false,
        };
    }

	public static bool IsLastMessage(this IQFeedMessage.MsgType mt)
	{
        return mt switch
        {
            IQFeedMessage.MsgType.NotFound or IQFeedMessage.MsgType.Error or
			IQFeedMessage.MsgType.End or IQFeedMessage.MsgType.NoData
			=> true,
            _ => false,
        };
    }

	public static string CreateRequestId(this long numRequestId) => $"#{numRequestId}#";

	public static IQFeedMessage ParseResponseLine(this string responseLine) => new(responseLine);
}
