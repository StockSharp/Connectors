namespace StockSharp.EdgeX.Native.Spot;

using StockSharp.EdgeX.Native.Common;
using StockSharp.EdgeX.Native.Derivatives.Model;

sealed class SpotAdapter : EdgeXSectionAdapter
{
	public SpotAdapter(SecureString key, SecureString secret, string clearingAccount, SecureString passphrase, string restEndpoint, string wsEndpoint, WorkingTime workingTime)
		: base(key, secret, clearingAccount, passphrase, BoardCodes.EdgeXSpot, SecurityTypes.CryptoCurrency, "Spot", true, false, restEndpoint, wsEndpoint, null, workingTime)
	{
	}

	protected override IEnumerable<Contract> EnumerateSectionContracts()
	{
		var emitted = new HashSet<string>(StringComparer.InvariantCultureIgnoreCase);
		var quote = CollateralCoin;

		foreach (var coin in CoinsById.Values)
		{
			if (coin?.Name.IsEmpty() != false)
				continue;

			if (coin.Name.EqualsIgnoreCase(quote))
				continue;

			var symbol = (coin.Name + quote).ToUpperInvariant();
			if (!ContractsBySymbol.TryGetValue(symbol, out var contract))
				continue;

			if (emitted.Add(contract.Id))
				yield return contract;
		}
	}
}
