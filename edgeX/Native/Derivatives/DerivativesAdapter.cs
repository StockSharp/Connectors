namespace StockSharp.EdgeX.Native.Derivatives;

using StockSharp.EdgeX.Native.Common;
using StockSharp.EdgeX.Native.Derivatives.Model;

sealed class DerivativesAdapter : EdgeXSectionAdapter
{
	public DerivativesAdapter(SecureString key, SecureString secret, string clearingAccount, SecureString passphrase, string restEndpoint, string wsEndpoint, string privateWsEndpoint, WorkingTime workingTime)
		: base(key, secret, clearingAccount, passphrase, BoardCodes.EdgeXDerivatives, SecurityTypes.Future, "Derivatives", false, true, restEndpoint, wsEndpoint, privateWsEndpoint, workingTime)
	{
	}

	protected override IEnumerable<Contract> EnumerateSectionContracts()
		=> ContractsById.Values.Where(static c => c.EnableDisplay != false && c.EnableTrade != false);
}
