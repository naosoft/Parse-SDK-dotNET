using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetHttpClient = System.Net.Http.HttpClient;

namespace Parse.Common.Internal
{
    public class HttpClient : IHttpClient
    {
        static HashSet<string> HttpContentHeaders = new HashSet<string> { { "Allow" }, { "Content-Disposition" }, { "Content-Encoding" }, { "Content-Language" }, { "Content-Length" }, { "Content-Location" }, { "Content-MD5" }, { "Content-Range" }, { "Content-Type" }, { "Expires" }, { "Last-Modified" } };

        public HttpClient() : this(new NetHttpClient { }) { }

        public HttpClient(NetHttpClient client) => this.client = client;

        NetHttpClient client;

        public Task<Tuple<HttpStatusCode, string>> ExecuteAsync(HttpRequest httpRequest, IProgress<ParseUploadProgressEventArgs> uploadProgress, IProgress<ParseDownloadProgressEventArgs> downloadProgress, CancellationToken cancellationToken)
        {
            uploadProgress = uploadProgress ?? new Progress<ParseUploadProgressEventArgs> { };
            downloadProgress = downloadProgress ?? new Progress<ParseDownloadProgressEventArgs> { };

            HttpRequestMessage message = new HttpRequestMessage(new HttpMethod(httpRequest.Method), httpRequest.Uri);
            
            Stream data = httpRequest.Data == null && httpRequest.Method.ToLower().Equals("post") ? new MemoryStream(new byte[0]) : httpRequest.Data;
            if (data != null)
                message.Content = new StreamContent(data);

            if (httpRequest.Headers != null)
            {
                foreach (KeyValuePair<string, string> header in httpRequest.Headers)
                {
                    if (HttpContentHeaders.Contains(header.Key))
                        message.Content.Headers.Add(header.Key, header.Value);
                    else
                        message.Headers.Add(header.Key, header.Value);
                }
            }

            message.Headers.Add("Cache-Control", "no-cache");
            message.Headers.IfModifiedSince = DateTimeOffset.UtcNow;

            // TODO: Investigate progress here, maybe there's something we're missing in order to support this.
            uploadProgress.Report(new ParseUploadProgressEventArgs { Progress = 0 });

            return client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ContinueWith(httpMessageTask =>
            {
                HttpResponseMessage response = httpMessageTask.Result;
                uploadProgress.Report(new ParseUploadProgressEventArgs { Progress = 1 });

                return response.Content.ReadAsStreamAsync().ContinueWith(streamTask =>
                {
                    MemoryStream resultStream = new MemoryStream { };
                    Stream responseStream = streamTask.Result;

                    int bufferSize = 4096;
                    byte[] buffer = new byte[bufferSize];
                    int bytesRead = 0;
                    long totalLength = -1;
                    long readSoFar = 0;

                    try { totalLength = responseStream.Length; }
                    catch { };

                    return InternalExtensions.WhileAsync(() => responseStream.ReadAsync(buffer, 0, bufferSize, cancellationToken).OnSuccess(readTask => (bytesRead = readTask.Result) > 0), () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        return resultStream.WriteAsync(buffer, 0, bytesRead, cancellationToken).OnSuccess(_ =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            readSoFar += bytesRead;

                            if (totalLength > -1)
                                downloadProgress.Report(new ParseDownloadProgressEventArgs { Progress = 1.0 * readSoFar / totalLength });
                        });
                    }).ContinueWith(_ =>
                    {
                        responseStream.Dispose();
                        return _;
                    }).Unwrap().OnSuccess(_ =>
                    {
                        // If getting stream size is not supported, then report download only once.
                        if (totalLength == -1)
                            downloadProgress.Report(new ParseDownloadProgressEventArgs { Progress = 1.0 });
                        byte[] resultAsArray = resultStream.ToArray();
                        resultStream.Dispose();
                        // Assume UTF-8 encoding.
                        return new Tuple<HttpStatusCode, string>(response.StatusCode, Encoding.UTF8.GetString(resultAsArray, 0, resultAsArray.Length));
                    });
                });
            }).Unwrap().Unwrap();
        }
    }
}
