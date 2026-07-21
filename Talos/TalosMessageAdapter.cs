namespace StockSharp.Talos;

/// <summary>The message adapter for Talos institutional FIX sessions.</summary>
[MediaIcon(Media.MediaNames.talos)]
[Doc("topics/api/connectors/crypto_exchanges/talos.html")]
[Display(ResourceType = typeof(LocalizedStrings),
	Name = LocalizedStrings.TalosKey,
	Description = LocalizedStrings.CryptoConnectorKey,
	GroupName = LocalizedStrings.CryptocurrencyKey)]
[MessageAdapterCategory(MessageAdapterCategories.Crypto |
	MessageAdapterCategories.RealTime | MessageAdapterCategories.Paid |
	MessageAdapterCategories.Transactions)]
public sealed class TalosMessageAdapter : FixMessageAdapter
{
	/// <summary>Initializes the adapter.</summary>
	public TalosMessageAdapter(IdGenerator transactionIdGenerator)
		: base(transactionIdGenerator)
	{
		Dialect = typeof(TalosFixDialect);
		Address = new DnsEndPoint("localhost", 0);
		ExchangeBoard = BoardCodes.Talos;
		HeartbeatInterval = TimeSpan.FromSeconds(30);
		SslProtocol = SslProtocols.Tls12;
		ValidateRemoteCertificates = true;
		CheckCertificateRevocation = true;
		SupportUnknownExecutions = true;
		OverrideExecIdByNative = true;
	}

	/// <inheritdoc />
	public override string[] AssociatedBoards => [BoardCodes.Talos];

	/// <inheritdoc />
	protected override bool ValidateSecurityId(SecurityId securityId)
		=> securityId.BoardCode.IsEmpty() ||
			securityId.BoardCode.EqualsIgnoreCase(BoardCodes.Talos) ||
			securityId.IsAssociated(BoardCodes.Talos);

	/// <inheritdoc />
	protected override bool OnSendInMessage(Message message)
	{
		if (message.Type == MessageTypes.Connect)
			ValidateConfiguration();
		return base.OnSendInMessage(message);
	}

	private void ValidateConfiguration()
	{
		if (Address is IPEndPoint { Port: <= 0 } or
			DnsEndPoint { Port: <= 0 })
			throw new InvalidOperationException(
				"Set the FIX endpoint issued during Talos onboarding.");
		if (FixDialect is not TalosFixDialect)
			throw new InvalidOperationException(
				"The Talos adapter requires the Talos FIX dialect.");
		if (!FixDialect.Version.EqualsIgnoreCase(FixVersions.Fix44))
			throw new InvalidOperationException(
				"The Talos connector uses the FIX 4.4 session profile.");
		_ = SenderCompId.ThrowIfEmpty(nameof(SenderCompId));
		_ = TargetCompId.ThrowIfEmpty(nameof(TargetCompId));
	}

	/// <inheritdoc />
	public override IMessageAdapter Clone()
	{
		var storage = new SettingsStorage();
		Save(storage);
		var clone = new TalosMessageAdapter(TransactionIdGenerator);
		clone.Load(storage);
		return clone;
	}

	/// <inheritdoc />
	public override string ToString()
		=> base.ToString() + $": {Address}";
}
