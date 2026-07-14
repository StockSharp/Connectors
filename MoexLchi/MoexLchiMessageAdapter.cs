namespace StockSharp.MoexLchi;

/// <summary>
/// The message adapter for <see cref="MoexLchi"/>.
/// </summary>
[MediaIcon(Media.MediaNames.moexlchi)]
[Doc("topics/api/connectors/russia/lci.html")]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.LchiKey,
	Description = LocalizedStrings.MarketDataConnectorKey,
	GroupName = LocalizedStrings.RussiaKey)]
[MessageAdapterCategory(MessageAdapterCategories.Russia | MessageAdapterCategories.History |
	MessageAdapterCategories.Free | MessageAdapterCategories.OrderLog)]
public partial class MoexLchiMessageAdapter : HistoricalMessageAdapter
{
	private readonly SynchronizedDictionary<DateTime, CompetitionYear> _competitions = [];

	static MoexLchiMessageAdapter()
	{
		const int beginYear = 2006;
		const int endYear = 2015;

		_allYears = new DateTime[endYear - beginYear + 1];

		for (var i = 0; i < _allYears.Length; i++)
			_allYears[i] = new DateTime(beginYear + i, 1, 1);
	}

	private static readonly DateTime[] _allYears;

	/// <summary>
	/// All years when the contest held.
	/// </summary>
	public static IEnumerable<DateTime> AllYears => _allYears;

	/// <summary>
	/// Initializes a new instance of the <see cref="MoexLchiMessageAdapter"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public MoexLchiMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		this.AddSupportedMessage(MessageTypes.MarketData, true);

		this.AddSupportedMarketDataType(DataType.OrderLog);
	}

	/// <inheritdoc />
	protected override ValueTask ResetAsync(ResetMessage message, CancellationToken cancellationToken)
	{
		_competitions.Clear();

		return SendOutMessageAsync(new ResetMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask ConnectAsync(ConnectMessage message, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new ConnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override ValueTask DisconnectAsync(DisconnectMessage message, CancellationToken cancellationToken)
	{
		return SendOutMessageAsync(new DisconnectMessage(), cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask OnOrderLogSubscriptionAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (mdMsg == null)
			throw new ArgumentNullException(nameof(mdMsg));

		await SendSubscriptionReplyAsync(mdMsg.TransactionId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		var from = mdMsg.From;
		var to = mdMsg.To;

		if (from == null)
		{
			if (to == null)
			{
				await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
				return;
			}

			from = to;
		}

		if (to == null)
			to = DateTime.UtcNow;

		var years = from.Value.Range(to.Value, TimeSpan.FromDays(1));

		foreach (var year in years)
		{
			var comp = Get(year);

			var members = mdMsg.Class.IsEmpty() ? await comp.GetMembers(cancellationToken) : [mdMsg.Class];

			foreach (var member in members)
			{
				foreach (var day in await comp.GetDays(cancellationToken))
				{
					if (from > day)
						continue;

					if (to < day)
						break;

					foreach (var trade in await comp.GetTradesAsync(member, day, cancellationToken))
					{
						trade.OriginalTransactionId = mdMsg.TransactionId;
						await SendOutMessageAsync(trade, cancellationToken);
					}
				}
			}
		}

		await SendSubscriptionFinishedAsync(mdMsg.TransactionId, cancellationToken);
	}

	private CompetitionYear Get(DateTime year)
	{
		return _competitions.SafeAdd(new DateTime(year.Year, 1, 1), key => new CompetitionYear(key));
	}
}