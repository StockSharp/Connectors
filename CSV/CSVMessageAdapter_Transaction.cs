namespace StockSharp.CSV;

using Ecng.IO;

public partial class CSVMessageAdapter
{
	private ImportSettings PortfolioImportSettings => Settings.FirstOrDefault(s => s.DataType == DataType.PositionChanges);
	private ImportSettings TransactionImportSettings => Settings.FirstOrDefault(s => s.DataType == DataType.Transactions);

	/// <inheritdoc />
	protected override async ValueTask PortfolioLookupAsync(PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		if (lookupMsg == null)
			throw new ArgumentNullException(nameof(lookupMsg));

		var transId = lookupMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!lookupMsg.IsSubscribe)
			return;

		var settings = PortfolioImportSettings;

		if (settings == null)
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
			return;
		}

		await ImportPortfolios(settings, lookupMsg, cancellationToken);

		if (!lookupMsg.IsHistoryOnly() && !TryAddNextProcessing(settings, lookupMsg))
			await SendSubscriptionFinishedAsync(transId, cancellationToken);
		else
			await SendSubscriptionResultAsync(lookupMsg, cancellationToken);
	}

	private async ValueTask ImportPortfolios(ImportSettings settings, PortfolioLookupMessage lookupMsg, CancellationToken cancellationToken)
	{
		var files = settings.GetFiles(FileSystem).ToArray();

		if (files.Length <= 0)
			return;

		var from = lookupMsg.From;

		var parser = CreateParser(settings);

		var portfolios = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);

		foreach (var file in files)
		{
			var needBreak = false;

			using var stream = FileSystem.OpenRead(file);

			await foreach (var msg in parser.Parse(stream).WithEnforcedCancellation(cancellationToken))
			{
				var posChangeMsg = (PositionChangeMessage)msg;

				if (portfolios.Add(posChangeMsg.PortfolioName))
				{
					await SendOutMessageAsync(new PortfolioMessage
					{
						PortfolioName = posChangeMsg.PortfolioName,
						OriginalTransactionId = lookupMsg.TransactionId
					}, cancellationToken);
				}

				if (lookupMsg.SecurityId != default && lookupMsg.SecurityId != posChangeMsg.SecurityId)
					continue;

				if (lookupMsg.From != null && lookupMsg.From.Value > posChangeMsg.ServerTime)
					continue;

				if (lookupMsg.To != null && lookupMsg.To < posChangeMsg.ServerTime)
				{
					needBreak = true;
					break;
				}

				if (from < posChangeMsg.ServerTime)
					from = posChangeMsg.ServerTime;

				posChangeMsg.OriginalTransactionId = lookupMsg.TransactionId;

				await SendOutMessageAsync(posChangeMsg, cancellationToken);

				if (lookupMsg.Count != null)
				{
					if (--lookupMsg.Count == 0)
					{
						needBreak = true;
						break;
					}
				}
			}

			if (needBreak)
				break;
		}

		lookupMsg.From = from;
	}

	/// <inheritdoc />
	protected override async ValueTask OrderStatusAsync(OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		if (statusMsg == null)
			throw new ArgumentNullException(nameof(statusMsg));

		var transId = statusMsg.TransactionId;

		await SendSubscriptionReplyAsync(transId, cancellationToken);

		if (!statusMsg.IsSubscribe)
			return;

		var settings = TransactionImportSettings;

		if (settings == null)
		{
			await SendSubscriptionNotSupportedAsync(transId, cancellationToken);
			return;
		}

		await ImportTransactions(settings, statusMsg, cancellationToken);

		if (!statusMsg.IsHistoryOnly() && !TryAddNextProcessing(settings, statusMsg))
			await SendSubscriptionFinishedAsync(transId, cancellationToken);
		else
			await SendSubscriptionResultAsync(statusMsg, cancellationToken);
	}

	private async ValueTask ImportTransactions(ImportSettings settings, OrderStatusMessage statusMsg, CancellationToken cancellationToken)
	{
		var files = settings.GetFiles(FileSystem).ToArray();

		if (files.Length <= 0)
			return;

		var from = statusMsg.From;

		var parser = CreateParser(settings);

		foreach (var file in files)
		{
			var needBreak = false;

			using var stream = FileSystem.OpenRead(file);

			await foreach (var msg in parser.Parse(stream).WithEnforcedCancellation(cancellationToken))
			{
				var transMsg = (ExecutionMessage)msg;

				if (statusMsg.SecurityId != default && statusMsg.SecurityId != transMsg.SecurityId)
					continue;

				if (statusMsg.From != null && statusMsg.From.Value > transMsg.ServerTime)
					continue;

				if (statusMsg.To != null && statusMsg.To < transMsg.ServerTime)
				{
					needBreak = true;
					break;
				}

				if (from < transMsg.ServerTime)
					from = transMsg.ServerTime;

				transMsg.OriginalTransactionId = statusMsg.TransactionId;
				await SendOutMessageAsync(transMsg, cancellationToken);

				if (statusMsg.Count != null)
				{
					if (--statusMsg.Count == 0)
					{
						needBreak = true;
						break;
					}
				}
			}

			if (needBreak)
				break;
		}

		statusMsg.From = from;
	}
}