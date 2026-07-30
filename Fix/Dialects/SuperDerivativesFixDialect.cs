namespace StockSharp.Fix.Dialects;

/// <summary>
/// SuperDerivatives FIX protocol dialect.
/// </summary>
[MediaIcon(Media.MediaNames.superderivatives)]
[Display(
	ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.SuperDerivativesKey,
	GroupName = LocalizedStrings.StockKey)]
public class SuperDerivativesFixDialect : BaseFixDialect
{
	// ReSharper disable InconsistentNaming
	private static class SuperDerivativesFixTags
	{
		public const FixTags MDObjectType = (FixTags)20000;
		public const FixTags TenorDays = (FixTags)20001;
		public const FixTags NoTenors = (FixTags)20002;
		public const FixTags NoStrikeCoordinates = (FixTags)20003;
		public const FixTags NoVolValues = (FixTags)20004;
		public const FixTags SwaptionTenor = (FixTags)20005;
		public const FixTags StrikeAbsolute = (FixTags)20006;
		public const FixTags StrikeDelta = (FixTags)20007;
		public const FixTags StrikePercent = (FixTags)20008;
		public const FixTags RefSpot = (FixTags)20009;
		public const FixTags NoMDEntries = (FixTags)20010;
		public const FixTags StrikeGroupSeparator = (FixTags)20011;
		public const FixTags ForwardRate = (FixTags)20012;
		public const FixTags DepoBase = (FixTags)20013;
		public const FixTags DepoTerm = (FixTags)20014;
		public const FixTags ATMVol = (FixTags)20015;
		public const FixTags RR = (FixTags)20016;
		public const FixTags BFly = (FixTags)20017;
		public const FixTags DiscountFactor = (FixTags)20018;
		public const FixTags ZeroRate = (FixTags)20019;
		public const FixTags ForwardPoints = (FixTags)20020;
		public const FixTags OptionExpiry = (FixTags)20022;
		public const FixTags FutureContract = (FixTags)20024;
		public const FixTags FutureLastTrade = (FixTags)20025;
		public const FixTags FutureDelivery = (FixTags)20026;
		public const FixTags ForwardRateBid = (FixTags)20027;
		public const FixTags ForwardRateAsk = (FixTags)20028;
		public const FixTags ContractType = (FixTags)20029;
		public const FixTags TenorName = (FixTags)20030;
		public const FixTags NormalVol = (FixTags)20031;
		public const FixTags StrikeRelative = (FixTags)20032;
		public const FixTags YieldRate = (FixTags)20033;
		public const FixTags RollingContract = (FixTags)20041;
		public const FixTags SWPTenors = (FixTags)20043;
		public const FixTags CdsParSpread = (FixTags)20051;
		public const FixTags CdsUpfrontFee = (FixTags)20052;
		public const FixTags CdsFlatSpread = (FixTags)20053;
		public const FixTags CdsDefaultProbability = (FixTags)20054;
		public const FixTags CdiSpread = (FixTags)20055;
		public const FixTags CdiPrice = (FixTags)20056;
		public const FixTags CdiImpliedDefaultProbability = (FixTags)20057;
		public const FixTags CdiLastQuoteTime = (FixTags)20058;
		public const FixTags ISIN = (FixTags)20100;
		public const FixTags CUSIP = (FixTags)20101;
		public const FixTags Accrued = (FixTags)20102;
		public const FixTags AccrualCurrency = (FixTags)20103;
		public const FixTags AccrualDays = (FixTags)20104;
		public const FixTags Settlement = (FixTags)20105;
		public const FixTags BondCurrency = (FixTags)20106;
	}

	private static class SuperDerivativesFixMessages
	{
		public const string MarketDataRefresh = "UX";
	}
	// ReSharper restore InconsistentNaming

	/// <summary>
	/// Initializes a new instance of the <see cref="SuperDerivativesFixDialect"/>.
	/// </summary>
	/// <param name="transactionIdGenerator">Transaction id generator.</param>
	public SuperDerivativesFixDialect(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator, Encoding.UTF8, FixVersions.Fix50Sp2)
	{
		TimeStampParser = new FastDateTimeParser("yyyyMMdd-HH:mm:ss.fff");
		TimeParser = new FastTimeSpanParser("hh:mm:ss.fff");
		DateParser = new FastDateTimeParser("yyyyMMdd");

		HeartbeatInterval = TimeSpan.FromSeconds(30);

		//HasPosition = false;

		QuotesAsLevel1 = true;

		ExchangeBoard = "DGX";

		ClientVersion = ((int)ApplVerId.Fix50SP2).To<string>();
	}

#if !NO_LICENSE
	/// <inheritdoc />
	public override string FeatureName => "FIX_SUPERDERIVATIVES";
#endif

	/// <inheritdoc />
	public override IEnumerable<MessageTypeInfo> PossibleSupportedMessages { get; } =
	[
		FixMessageTypes.SeqReset.ToInfo(),
		FixMessageTypes.ResendRequest.ToInfo(),
	];

	/// <inheritdoc />
	public override async IAsyncEnumerable<DataType> GetSupportedMarketDataTypesAsync(SecurityId securityId, DateTime? from, DateTime? to)
	{
		yield return DataType.Level1;
	}

	/// <inheritdoc />
	protected override ValueTask<string> OnWriteAsync(IFixWriter writer, Message message, CancellationToken cancellationToken)
	{
		switch (message.Type)
		{
			case MessageTypes.Connect:
			{
				return WriteLogonRequestAsync(writer, (ConnectMessage)message, cancellationToken);
			}

			default:
				return base.OnWriteAsync(writer, message, cancellationToken);
		}
	}

	/// <inheritdoc />
	protected override SecurityTypes? GetSecurityType(string type)
	{
		switch (type)
		{
			case "FXSPOTFIX":
				return SecurityTypes.Stock;

			case "FXVOL": // FX ATM volatility rate
				return SecurityTypes.Index;

			case "FXRR25D": // FX 25 delta risk reversal
			case "FXRR10D": // FX 10 delta risk reversal
			case "FXBFLY25D": // FX 25 delta butterfly
			case "FXBFLY10D": // FX 10 delta butterfly
				return SecurityTypes.Option;

			case "FXFUT": // FX future contract rate
				return SecurityTypes.Future;

			case "FXDEPOFWDCURVE": // FX forward and deposit rate curve
			case "FXATMVOLCURVE": // FX ATM volatility curve
			case "FXATMRRBFLYCURVE": // FX ATM risk reversal and butterfly curve
			case "FXVS": // FX volatility surface
				return SecurityTypes.Option;

			// TODO

			default:
				return base.GetSecurityType(type);
		}
	}

	/// <inheritdoc />
	protected override async IAsyncEnumerable<Message> OnReadAsync(IFixReader reader, string msgType, [EnumeratorCancellation]CancellationToken cancellationToken)
	{
		switch (msgType)
		{
			case SuperDerivativesFixMessages.MarketDataRefresh:
			{
				// TODO

				var isOk = await reader.ReadMessageAsync((tag, cancellationToken) =>
					tag switch
					{
						_ => new(false),
					}, cancellationToken);

				if (!isOk)
					yield break;

				//var msg = new Level1ChangeMessage
				//{
				//	ServerTime = sendingTime
				//}
				//.TryAdd(Level1Fields.RealizedPnL, closedPnL, true)
				//.TryAdd(Level1Fields.UnrealizedPnL, openPnL, true)
				//.TryAdd(Level1Fields.CurrentValue, balance, true);

				//messageHandler(msg);
				break;
			}

			default:
			{
				await foreach (var msg in base.OnReadAsync(reader, msgType, cancellationToken))
					yield return msg;

				break;
			}
		}
	}
}