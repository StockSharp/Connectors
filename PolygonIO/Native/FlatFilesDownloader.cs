namespace StockSharp.PolygonIO.Native;

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

class FlatFilesDownloader : BaseLogReceiver
{
	private static readonly DataType _tf1Day = TimeSpan.FromDays(1).TimeFrame().Immutable();
	private static readonly DataType _tf1Min = TimeSpan.FromMinutes(1).TimeFrame().Immutable();

	private readonly IAmazonS3 _s3;
	private readonly string _bucket;

	public FlatFilesDownloader(
		string accessKeyId,
		string secretAccessKey,
		string serviceUrl,
		string bucket)
	{
		_bucket = bucket.ThrowIfEmpty(nameof(bucket));

		_s3 = new AmazonS3Client(
			new BasicAWSCredentials(accessKeyId, secretAccessKey),
			new AmazonS3Config
			{
				ServiceURL = serviceUrl.ThrowIfEmpty(nameof(serviceUrl)),
				ForcePathStyle = true,
				UseHttp = false
			}
		);
	}

	protected override void DisposeManaged()
	{
		_s3.Dispose();

		base.DisposeManaged();
	}

	public async Task<bool> DownloadDayAsync(
		SecurityTypes section,
		DataType dataType,
		DateTime dateUtc,
		Func<string, Stream, CancellationToken, Task> handler,
		CancellationToken ct)
	{
		if (handler is null)
			throw new ArgumentNullException(nameof(handler));

		var key = BuildObjectKey(section, dataType, dateUtc);
		var fileName = Path.GetFileName(key);

		if (!await ExistsAsync(key, ct))
		{
			this.AddWarningLog("[MISS] No object: s3://{0}/{1}", _bucket, key);
			return false;
		}

		this.AddInfoLog("[GET] s3://{0}/{1}", _bucket, key);

		var req = new GetObjectRequest { BucketName = _bucket, Key = key };
		using var resp = await _s3.GetObjectAsync(req, ct);
		this.AddDebugLog("[STREAM] {0} ({1} bytes)", fileName, resp.Headers.ContentLength);
		await handler(key, resp.ResponseStream, ct);
		return true;
	}

	private async Task<bool> ExistsAsync(string key, CancellationToken ct)
	{
		try
		{
			var meta = await _s3.GetObjectMetadataAsync(_bucket, key, ct);
			return meta.HttpStatusCode == HttpStatusCode.OK;
		}
		catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
		{
			return false;
		}
	}

	private static string BuildObjectKey(SecurityTypes section, DataType dataType, DateTime dateUtc)
	{
		var prefix = GetDatasetPrefix(section, dataType);
		return $"{prefix}/{dateUtc:yyyy}/{dateUtc:MM}/{dateUtc:yyyy-MM-dd}.csv.gz";
	}

	private static string GetDatasetPrefix(SecurityTypes section, DataType dataType)
	{
		var isStocks = section == SecurityTypes.Stock;
		var isIndices = section == SecurityTypes.Index;
		var isForex = section == SecurityTypes.Currency;
		var isCrypto = section == SecurityTypes.CryptoCurrency;

		if (!isStocks && !isIndices && !isForex && !isCrypto)
			throw new NotSupportedException($"Section {section} is not supported by Polygon flat files.");

		// map data type
		if (dataType == DataType.Ticks)
		{
			if (isStocks) return "us_stocks_sip/trades_v1";
			if (isIndices) return "us_indices/values_v1"; // Values are ticks for indices
			if (isForex) return "fx/quotes_v1"; // FX has quotes as tick-like stream
			if (isCrypto) return "crypto/trades_v1";
		}
		else if (dataType == DataType.Level1)
		{
			if (isStocks) return "us_stocks_sip/quotes_v1";
			if (isForex) return "fx/quotes_v1";
			if (isCrypto) return "crypto/quotes_v1";
			if (isIndices) throw new NotSupportedException("Indices quotes flat files are not available.");
		}
		else if (dataType == _tf1Min)
		{
			if (isStocks) return "us_stocks_sip/minute_aggs_v1";
			if (isIndices) return "us_indices/minute_aggs_v1";
			if (isForex) return "fx/minute_aggs_v1";
			if (isCrypto) return "crypto/minute_aggs_v1";
		}
		else if (dataType == _tf1Day)
		{
			if (isStocks) return "us_stocks_sip/daily_aggs_v1";
			if (isIndices) return "us_indices/daily_aggs_v1";
			if (isForex) return "fx/daily_aggs_v1";
			if (isCrypto) return "crypto/daily_aggs_v1";
		}

		throw new NotSupportedException($"DataType {dataType} is not supported by Polygon flat files in section {section}.");
	}
}