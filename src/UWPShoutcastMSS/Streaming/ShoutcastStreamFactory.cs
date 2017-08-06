﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace UWPShoutcastMSS.Streaming
{
    public static class ShoutcastStreamFactory
    {
        internal struct ShoutcastStreamFactoryInternalConnectResult
        {
            internal StreamSocket socket;
            internal DataWriter socketWriter;
            internal DataReader socketReader;
            internal string httpResponse;
        }


        public const string DefaultUserAgent = "Shoutcast Player (http://github.com/Amrykid/UWPShoutcastMSS)";


        internal static async Task<ShoutcastStreamFactoryInternalConnectResult> ConnectInternalAsync(Uri serverUrl,
            ShoutcastStreamFactoryConnectionSettings settings)
        {
            //abstracted the connection bit to allow for reconnecting from the ShoutcastStream object.

            ShoutcastStreamFactoryInternalConnectResult result = new ShoutcastStreamFactoryInternalConnectResult();

            result.socket = new StreamSocket();

            await result.socket.ConnectAsync(new Windows.Networking.HostName(serverUrl.Host), serverUrl.Port.ToString());

            result.socketWriter = new DataWriter(result.socket.OutputStream);
            result.socketReader = new DataReader(result.socket.InputStream);

            //build a http request
            StringBuilder requestBuilder = new StringBuilder();
            requestBuilder.AppendLine("GET " + serverUrl.LocalPath + settings.RelativePath + " HTTP/1.1");
            requestBuilder.AppendLine("Icy-MetaData: 1");
            requestBuilder.AppendLine("Host: " + serverUrl.Host + (serverUrl.Port != 80 ? ":" + serverUrl.Port : ""));
            requestBuilder.AppendLine("Connection: Keep-Alive");
            requestBuilder.AppendLine("User-Agent: " + settings.UserAgent);
            requestBuilder.AppendLine();

            //send the http request
            result.socketWriter.WriteString(requestBuilder.ToString());
            await result.socketWriter.StoreAsync();
            await result.socketWriter.FlushAsync();

            //start reading the headers from the response
            string response = string.Empty;
            while (!response.EndsWith(Environment.NewLine + Environment.NewLine))
            //loop until we get the double line-ending signifying the end of the headers
            {
                await result.socketReader.LoadAsync(1);
                response += result.socketReader.ReadString(1);
            }

            result.httpResponse = response;

            return result;
        }

        public static Task<ShoutcastStream> ConnectAsync(Uri serverUrl)
        {
            return ConnectAsync(serverUrl, new ShoutcastStreamFactoryConnectionSettings()
            {
                UserAgent = DefaultUserAgent
            });
        }
        public static async Task<ShoutcastStream> ConnectAsync(Uri serverUrl,
            ShoutcastStreamFactoryConnectionSettings settings)
        {
            //http://www.smackfu.com/stuff/programming/shoutcast.html

            ShoutcastStream shoutStream = null;

            ShoutcastStreamFactoryInternalConnectResult result = await ConnectInternalAsync(serverUrl, settings);

            shoutStream = new ShoutcastStream(serverUrl, settings, result.socket, result.socketReader, result.socketWriter);

            string httpLine = result.httpResponse.Substring(0, result.httpResponse.IndexOf('\n')).Trim();

            if (string.IsNullOrWhiteSpace(httpLine)) throw new InvalidOperationException("httpLine is null or whitespace");

            var action = ParseHttpCode(httpLine, result.httpResponse, shoutStream);

            //todo handle when we get a text/html page.

            switch (action.ActionType)
            {
                case ConnectionActionType.Success:
                    var headers = ParseResponse(result.httpResponse, shoutStream);
                    await shoutStream.HandleHeadersAsync(headers);
                    return shoutStream;
                case ConnectionActionType.Fail:
                    throw action.ActionException;
                default:
                    throw new Exception("We weren't able to connect for some reason.");
            }
        }

        private static ConnectionAction ParseHttpCode(string httpLine, string response, ShoutcastStream shoutStream)
        {
            var bits = httpLine.Split(new char[] { ' ' }, 3);

            var protocolBit = bits[0].ToUpper(); //always 'HTTP' or 'ICY
            int statusCode = int.Parse(bits[1]);

            switch (protocolBit)
            {
                case "ICY":
                    {
                        switch (statusCode)
                        {
                            case 200: return ConnectionAction.FromSuccess();
                        }
                    }
                    break;
                default:
                    if (protocolBit.StartsWith("HTTP/"))
                    {
                        switch (statusCode)
                        {
                            case 200: return ConnectionAction.FromSuccess();
                            case 404: return ConnectionAction.FromFailure();
                        }
                    }
                    break;
            }

            return null;
        }

        private static KeyValuePair<string, string>[] ParseResponse(string response, ShoutcastStream shoutStream)
        {
            string[] responseSplitByLine = response.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            KeyValuePair<string, string>[] headers = ParseHttpResponseToKeyPairArray(responseSplitByLine);

            shoutStream.metadataInt = uint.Parse(headers.First(x => x.Key == "ICY-METAINT").Value);

            shoutStream.StationInfo.StationName = headers.First(x => x.Key == "ICY-NAME").Value;
            shoutStream.StationInfo.StationGenre = headers.First(x => x.Key == "ICY-GENRE").Value;

            if (headers.Any(x => x.Key.ToUpper() == "ICY-DESCRIPTION"))
                shoutStream.StationInfo.StationDescription = headers.First(x => x.Key.ToUpper() == "ICY-DESCRIPTION").Value;

            shoutStream.AudioInfo.BitRate = uint.Parse(headers.FirstOrDefault(x => x.Key == "ICY-BR").Value);

            switch (headers.First(x => x.Key == "CONTENT-TYPE").Value.ToLower().Trim())
            {
                case "audio/mpeg":
                    shoutStream.AudioInfo.AudioFormat = StreamAudioFormat.MP3;
                    break;
                case "audio/aac":
                    shoutStream.AudioInfo.AudioFormat = StreamAudioFormat.AAC;
                    break;
                case "audio/aacp":
                    shoutStream.AudioInfo.AudioFormat = StreamAudioFormat.AAC_ADTS;
                    break;
            }

            return headers;
        }

        private static KeyValuePair<string, string>[] ParseHttpResponseToKeyPairArray(string[] responseSplitByLine)
        {
            return responseSplitByLine.Where(line => line.Contains(":")).Select(line =>
            {
                string header = line.Substring(0, line.IndexOf(":"));
                string value = line.Substring(line.IndexOf(":") + 1);

                var pair = new KeyValuePair<string, string>(header.ToUpper(), value);

                return pair;
            }).ToArray();
        }

        internal class ConnectionAction
        {
            public ConnectionActionType ActionType { get; set; }
            public Uri ActionUrl { get; set; }
            public Exception ActionException { get; set; }

            public static ConnectionAction FromSuccess() { return new ConnectionAction() { ActionType = ConnectionActionType.Success }; }

            public static ConnectionAction FromFailure()
            {
                return FromFailure(null);
            }

            public static ConnectionAction FromFailure(Exception exception)
            {
                return new ConnectionAction() { ActionType = ConnectionActionType.Fail, ActionException = exception };
            }
        }
        internal enum ConnectionActionType
        {
            Fail = 0,
            Success = 1,
            Redirect = 2
        }
    }
}

