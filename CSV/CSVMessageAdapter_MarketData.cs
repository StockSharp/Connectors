namespace StockSharp.CSV;

using Ecng.IO;

public partial class CSVMessageAdapter
{
	private ImportSettings SecurityImportSettings => Settings.FirstOrDefault(s => s.DataType == DataType.Securities);

	/// <inheritdoc />
	protected override async ValueTask SecurityLookupAsync(SecurityLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var settings = SecurityImportSettings;

		if (settings == null)
		{
			await SendSubscriptionNotSupportedAsync(lookupMsg.TransactionId, cancellationToken);
			return;
		}

		var fileNames = settings.GetFiles(FileSystem).ToArray();

		var parser = CreateParser(settings);

		foreach (var fileName in fileNames)
		{
			using var file = FileSystem.OpenRead(fileName);

			await foreach (var msg in parser.Parse(file).WithEnforcedCancellation(cancellationToken))
			{
				var secMsg = (SecurityMessage)msg;

				if (!secMsg.IsMatch(lookupMsg))
					continue;

				secMsg.OriginalTransactionId = lookupMsg.TransactionId;

				await SendOutMessageAsync(secMsg, cancellationToken);
			}
		}

		await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	/// <inheritdoc />
	protected override async ValueTask MarketDataAsync(MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		if (mdMsg == null)
			throw new ArgumentNullException(nameof(mdMsg));

		var settings = Settings.FirstOrDefault(s => s.DataType == mdMsg.DataType2);

		var transId = mdMsg.TransactionId;

		if (settings == null)
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
			return;
		}

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!mdMsg.IsSubscribe)
			return;

		await ImportMarketData(settings, mdMsg, cancellationToken);

		if (!mdMsg.IsHistoryOnly() && !TryAddNextProcessing(settings, mdMsg))
			await SendSubscriptionFinishedAsync(transId, cancellationToken);
		else
			await SendSubscriptionResultAsync(mdMsg, cancellationToken);
	}

	private async ValueTask ImportMarketData(ImportSettings settings, MarketDataMessage mdMsg, CancellationToken cancellationToken)
	{
		var fileNames = settings.GetFiles(FileSystem).ToArray();

		var from = mdMsg.From;

		var parser = CreateParser(settings);

		foreach (var fileName in fileNames)
		{
			var needBreak = false;

			using var file = FileSystem.OpenRead(fileName);

			await foreach (var message in parser.Parse(file).WithEnforcedCancellation(cancellationToken))
			{
				if (message is ISecurityIdMessage secIdMsg)
				{
					if (mdMsg.SecurityId != default && secIdMsg.SecurityId != mdMsg.SecurityId)
						continue;
				}

				if (message is IServerTimeMessage serverTimeMsg)
				{
					var time = serverTimeMsg.ServerTime;

					if (mdMsg.From != null && mdMsg.From.Value > time)
						continue;

					if (mdMsg.To != null && mdMsg.To < time)
					{
						needBreak = true;
						break;
					}

					if (from < time)
						from = time;
				}

				if (message is IOriginalTransactionIdMessage originTransId)
				{
					originTransId.OriginalTransactionId = mdMsg.TransactionId;
				}

				await SendOutMessageAsync(message, cancellationToken);

				if (mdMsg.Count != null)
				{
					if (--mdMsg.Count == 0)
					{
						needBreak = true;
						break;
					}
				}
			}

			if (needBreak)
				break;
		}

		mdMsg.From = from;
	}
}