using System.IO;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using System.Threading;
using System.Linq;
using System.Diagnostics;
using System.Buffers;

namespace MowerUpdater;

internal class FileDownloader
{
    const int BufferSize = 1024 * 512; // 512KB
    const int FlushBufferSize = 1024 * 1024 * 4; // 4MB
    const string ContentLengthStr = "Content-Length";
    const string EtagStr = "ETag";
    const string LastModifiedStr = "Last-Modified";

    bool? _allowRange = null;
    DateTime? _lastModified = null;
    string _etag = null;

    HttpClient _cli;
    string _url;
    Stream _dst;

    public int Retries { get; set; } = 5;
    public event Action<(long downloaded, long total)> ProgressUpdated;
    public event Action<(int retries, Exception ex)> OnFailed;

    public FileDownloader(HttpClient client, string url, Stream dest)
    {
        _cli = client;
        _url = url;
        _dst = dest;
    }

    public async Task DownloadAsync(CancellationToken token = default)
    {
        var retries = 0;
        var time_wait = 1000;
        while (true)
        {
            try
            {
                var new_download = await CheckIfRequireNewDownload(token);

                using var req = new HttpRequestMessage(HttpMethod.Get, _url);
                if (new_download)
                {
                    _dst.Position = 0;
                    _dst.SetLength(0);
                }
                else
                {
                    req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(_dst.Length, null);
                }

                using var resp = await _cli.GetAsync(_url, HttpCompletionOption.ResponseHeadersRead, token);
                resp.EnsureSuccessStatusCode();

                var content = resp.Content;
                int content_length = -1;
                if (content.Headers.Contains(ContentLengthStr))
                {
                    if (!int.TryParse(content.Headers.GetValues(ContentLengthStr).First(), out content_length))
                    {
                        content_length = -1;
                    }
                }

                using var stream = await content.ReadAsStreamAsync();

                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                var bytesRead = 1;
                var bytesBeforeFlush = 0;
                while (bytesRead > 0)
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, BufferSize);
                    await _dst.WriteAsync(buffer, 0, bytesRead);

                    bytesBeforeFlush += bytesRead;
                    if (bytesBeforeFlush >= FlushBufferSize)
                    {
                        bytesBeforeFlush = 0;
                        await _dst.FlushAsync();
                        ProgressUpdated?.Invoke((_dst.Length, content_length));
                    }
                }

                _dst.SetLength(_dst.Position);
                ProgressUpdated?.Invoke((_dst.Length, content_length));
                return;
            }
            catch (Exception ex)
            {
                OnFailed?.Invoke((retries, ex));
                if (retries++ >= Retries) throw ex;

                await Task.Delay(time_wait);
                time_wait += 1000;
            }
        }
    }

    async ValueTask<bool> CheckIfRequireNewDownload(CancellationToken token)
    {
        var old_lastModified = _lastModified;
        var old_etag = _etag;
        
        using var resp = await _cli.SendAsync(new HttpRequestMessage(HttpMethod.Head, _url), token);
        if (resp.IsSuccessStatusCode)
        {
            _allowRange = resp.Headers.AcceptRanges.Contains("bytes");

            // 理论上Contains方法应该不会返回异常才对, 但是在 NetFramework 4.7.1 就是会莫名奇妙返回日常, 只好注释掉
            //if (resp.Headers.Contains(LastModifiedStr))
            //{
            //    _lastModified = DateTime.Parse(resp.Headers.GetValues(LastModifiedStr).First());
            //}
            if (resp.Headers.Contains(EtagStr))
            {
                _etag = resp.Headers.ETag.Tag;
            }
            return !(_allowRange ?? false)  || old_lastModified != _lastModified || old_etag != _etag;
        }
        return true;
    }

    static public async Task EnsureDownloaded(HttpClient client, string url, string file, bool forceRedownload = false, CancellationToken token = default)
    {
        if (!File.Exists(file) || forceRedownload)
        {
            var downloading_file = file + ".downloading";
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            using (var fs = File.Open(downloading_file, FileMode.OpenOrCreate, FileAccess.Write))
            {
                var downloader = new FileDownloader(client, url, fs);
                await downloader.DownloadAsync(token);
            }
            File.Move(downloading_file, file);
        }
    }
}
