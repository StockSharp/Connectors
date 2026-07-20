using System.Reflection;
using System.Runtime.InteropServices;

namespace StockSharp.ApexOmni.Native;

static class ApexOmniZkLinkNative
{
	internal const string LibraryName = "zklink_sdk";

	[StructLayout(LayoutKind.Sequential)]
	internal struct RustBuffer
	{
		public int Capacity;
		public int Length;
		public nint Data;
	}

	[StructLayout(LayoutKind.Sequential)]
	internal struct CallStatus
	{
		public byte Code;
		public RustBuffer ErrorBuffer;
	}

	static ApexOmniZkLinkNative()
	{
		NativeLibrary.SetDllImportResolver(typeof(ApexOmniZkLinkNative).Assembly,
			ResolveLibrary);
	}

	private static nint ResolveLibrary(string libraryName, Assembly assembly,
		DllImportSearchPath? searchPath)
	{
		_ = searchPath;
		if (!libraryName.Equals(LibraryName, StringComparison.Ordinal))
			return nint.Zero;

		var platform = OperatingSystem.IsWindows()
			? "win"
			: OperatingSystem.IsMacOS() ? "osx" : "linux";
		var architecture = RuntimeInformation.ProcessArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => RuntimeInformation.ProcessArchitecture.ToString()
				.ToLowerInvariant(),
		};
		var fileName = OperatingSystem.IsWindows()
			? LibraryName + ".dll"
			: OperatingSystem.IsMacOS()
				? "lib" + LibraryName + ".dylib"
				: "lib" + LibraryName + ".so";
		var assemblyDirectory = Path.GetDirectoryName(assembly.Location) ??
			AppContext.BaseDirectory;
		var candidates = new[]
		{
			Path.Combine(AppContext.BaseDirectory, fileName),
			Path.Combine(AppContext.BaseDirectory, "runtimes",
				$"{platform}-{architecture}", "native", fileName),
			Path.Combine(assemblyDirectory, fileName),
			Path.Combine(assemblyDirectory, "runtimes",
				$"{platform}-{architecture}", "native", fileName),
		};
		foreach (var candidate in candidates)
			if (File.Exists(candidate) &&
				NativeLibrary.TryLoad(candidate, out var handle))
				return handle;
		return nint.Zero;
	}

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "ffi_zklink_sdk_rustbuffer_alloc")]
	internal static extern RustBuffer Allocate(int size, ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "ffi_zklink_sdk_rustbuffer_free")]
	internal static extern void Free(RustBuffer buffer, ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_fn_constructor_contract_new")]
	internal static extern nint CreateContract(RustBuffer builder,
		ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_fn_method_contract_get_bytes")]
	internal static extern RustBuffer GetContractBytes(nint contract,
		ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_fn_free_contract")]
	internal static extern void FreeContract(nint contract, ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_fn_constructor_zklinksigner_new_from_seed")]
	internal static extern nint CreateSigner(RustBuffer seed,
		ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_fn_method_zklinksigner_sign_musig")]
	internal static extern RustBuffer Sign(nint signer, RustBuffer message,
		ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_fn_free_zklinksigner")]
	internal static extern void FreeSigner(nint signer, ref CallStatus status);

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_checksum_constructor_contract_new")]
	internal static extern ushort ContractConstructorChecksum();

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_checksum_method_contract_get_bytes")]
	internal static extern ushort ContractBytesChecksum();

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_checksum_constructor_zklinksigner_new_from_seed")]
	internal static extern ushort SignerConstructorChecksum();

	[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl,
		EntryPoint = "uniffi_zklink_sdk_checksum_method_zklinksigner_sign_musig")]
	internal static extern ushort SignChecksum();
}
