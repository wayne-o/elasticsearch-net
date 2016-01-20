﻿#if DOTNETCORE
using System;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.DecompressionMethods;

namespace Elasticsearch.Net
{
	internal class WebProxy : IWebProxy
	{
		private readonly Uri _uri;

		public WebProxy(Uri uri) { _uri = uri; }

		public ICredentials Credentials { get; set; }

		public Uri GetProxy(Uri destination) => _uri;

		public bool IsBypassed(Uri host) => host.IsLoopback;
	}

	public class HttpConnection : IConnection
	{
		private readonly object _lock = new object();
		private readonly ConcurrentDictionary<int, HttpClient> _clients = new ConcurrentDictionary<int, HttpClient>();
		private string DefaultContentType => "application/json";

		public HttpConnection() { }
		
		private HttpClient GetClient(RequestData requestData)
		{
			var hashCode = requestData.GetHashCode();
			HttpClient client;
			if (this._clients.TryGetValue(hashCode, out client)) return client;
			lock(_lock)
			{
				if (this._clients.TryGetValue(hashCode, out client)) return client;

				var handler = new HttpClientHandler();
				handler.AutomaticDecompression = requestData.HttpCompression ? GZip | Deflate : None; 
				client = new HttpClient(handler, false);
				client.Timeout = requestData.RequestTimeout;
				//TODO add headers
				//client.DefaultRequestHeaders = 
				this._clients.TryAdd(hashCode, client);
				return client;
			}

		}

		public virtual ElasticsearchResponse<TReturn> Request<TReturn>(RequestData requestData) where TReturn : class
		{
			var client = this.GetClient(requestData);
			var builder = new ResponseBuilder<TReturn>(requestData);
			try
			{
				var requestMessage = CreateHttpRequestMessage(requestData);
				var response = client.SendAsync(requestMessage, requestData.CancellationToken).GetAwaiter().GetResult();
				builder.StatusCode = (int)response.StatusCode;

				if (response.Content != null)
					builder.Stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
			}
			catch (HttpRequestException e)
			{
				HandleException(builder, e);
			}

			return builder.ToResponse();
		}

		public virtual async Task<ElasticsearchResponse<TReturn>> RequestAsync<TReturn>(RequestData requestData) where TReturn : class
		{
			var client = this.GetClient(requestData);
			var builder = new ResponseBuilder<TReturn>(requestData);
			try
			{
				var requestMessage = CreateHttpRequestMessage(requestData);
				var response = await client.SendAsync(requestMessage, requestData.CancellationToken);
				builder.StatusCode = (int)response.StatusCode;

				if (response.Content != null)
					builder.Stream = await response.Content.ReadAsStreamAsync();
			}
			catch (HttpRequestException e)
			{
				HandleException(builder, e);
			}

			return await builder.ToResponseAsync();
		}

		private void HandleException<TReturn>(ResponseBuilder<TReturn> builder, HttpRequestException exception)
			where TReturn : class
		{
			builder.Exception = exception;

			// TODO: Figure out what to do here
			//var response = exception. as HttpWebResponse;
			//if (response != null)
			//{
			//	builder.StatusCode = (int)response.StatusCode;
			//	builder.Stream = response.GetResponseStream();
			//}
		}

		private static HttpRequestMessage CreateHttpRequestMessage(RequestData requestData)
		{
			var method = ConvertHttpMethod(requestData.Method);
			var requestMessage = new HttpRequestMessage(method, requestData.Uri);

			foreach(string key in requestData.Headers)
			{
				requestMessage.Headers.TryAddWithoutValidation(key, requestData.Headers.GetValues(key));
			}

			requestMessage.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(requestData.ContentType));

			var data = requestData.PostData;

			if (data != null)
			{
				var stream = requestData.MemoryStreamFactory.Create();

				if (requestData.HttpCompression)
				{
					using (var zipStream = new GZipStream(stream, CompressionMode.Compress))
						data.Write(zipStream, requestData.ConnectionSettings);

					requestMessage.Headers.Add("Content-Encoding", "gzip");
					requestMessage.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
					requestMessage.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));
				}
				else
					data.Write(stream, requestData.ConnectionSettings);

				stream.Position = 0;
				requestMessage.Content = new StreamContent(stream);
			}
			else
			{
				// Set content in order to set a Content-Type header
				requestMessage.Content = new ByteArrayContent(new byte[0]);
			}

			requestMessage.Content.Headers.ContentType = new MediaTypeHeaderValue(requestData.ContentType);

			if (!string.IsNullOrWhiteSpace(requestData.Uri.UserInfo))
			{
				var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(requestMessage.RequestUri.UserInfo));
				requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
			}

			return requestMessage;
		}

		private static System.Net.Http.HttpMethod ConvertHttpMethod(HttpMethod httpMethod)
		{
			switch (httpMethod)
			{
				case HttpMethod.GET: return System.Net.Http.HttpMethod.Get;
				case HttpMethod.POST: return System.Net.Http.HttpMethod.Post;
				case HttpMethod.PUT: return System.Net.Http.HttpMethod.Put;
				case HttpMethod.DELETE: return System.Net.Http.HttpMethod.Delete;
				case HttpMethod.HEAD: return System.Net.Http.HttpMethod.Head;
				default:
					throw new ArgumentException("Invalid value for HttpMethod", nameof(httpMethod));
			}
		}

		void IDisposable.Dispose() => this.DisposeManagedResources();

		protected virtual void DisposeManagedResources()
		{
			foreach(var c in _clients)
				c.Value.Dispose();
		}
	}
}
#endif
