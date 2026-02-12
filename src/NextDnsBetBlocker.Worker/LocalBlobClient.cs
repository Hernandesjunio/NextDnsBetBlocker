namespace NextDnsBetBlocker.Worker;

using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.IO;

public class LocalBlobContainerClient : BlobContainerClient
{
    private readonly string _localPath;

    public LocalBlobContainerClient(string localPath) : base(new Uri("https://local/blob"))
    {
        _localPath = localPath;
        Directory.CreateDirectory(localPath);
    }

    public override BlobClient GetBlobClient(string blobName)
    {
        return new LocalBlobClient(Path.Combine(_localPath, blobName));
    }
}

public class LocalBlobClient : BlobClient
{
    private readonly string _filePath;

    public LocalBlobClient(string filePath) : base(new Uri($"https://local/{Path.GetFileName(filePath)}"))
    {
        _filePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
    }

    public override async Task<Response<BlobDownloadInfo>> DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
            throw new FileNotFoundException($"Blob not found: {_filePath}");

        var content = await File.ReadAllBytesAsync(_filePath, cancellationToken);
        var stream = new MemoryStream(content);

        var downloadInfo = BlobsModelFactory.BlobDownloadInfo(
            content: stream,
            contentLength: content.Length);

        return Response.FromValue(downloadInfo, new MockResponse());
    }

    public override async Task<Response<BlobContentInfo>> UploadAsync(
        Stream data,
        bool overwrite = false,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath) && !overwrite)
            throw new InvalidOperationException("Blob already exists");

        using var fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Write);
        await data.CopyToAsync(fileStream, cancellationToken);

        var contentInfo = BlobsModelFactory.BlobContentInfo(
            eTag: new Azure.ETag("local"),
            lastModified: DateTimeOffset.UtcNow,
            contentHash: null,
            encryptionKeySha256: null,
            encryptionScope: null,
            versionId: null,
            blobSequenceNumber: 0);

        return Response.FromValue(contentInfo, new MockResponse());
    }

    private class MockResponse : Response
    {
        public override int Status => 200;
        public override string ReasonPhrase => "OK";
        public override Stream ContentStream { get; set; } = Stream.Null;
        public override string ClientRequestId { get; set; } = string.Empty;

        public override void Dispose() { }

        protected override bool TryGetHeader(string name, out string value) 
        { 
            value = null; 
            return false; 
        }

        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => Enumerable.Empty<HttpHeader>();
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) 
        { 
            values = null; 
            return false; 
        }
    }
}
