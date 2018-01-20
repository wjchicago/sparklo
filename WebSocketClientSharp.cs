using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Binance.Api.WebSocket.Events;
using Microsoft.Extensions.Logging;
using WebSocketSharp;

namespace Binance.Api.WebSocket
{
    public sealed class WebSocketClientSharp : IWebSocketClient
    {
        #region Public Events

        public event EventHandler<EventArgs> Open;

        public event EventHandler<WebSocketClientEventArgs> Message;

        public event EventHandler<EventArgs> Close;

        #endregion Public Events

        #region Public Properties

        public bool IsStreaming { get; private set; }

        #endregion Public Properties

        #region Private Constants

        private const int ReceiveBufferSize = 16 * 1024;

        #endregion Private Constants

        #region Private Fields

        private readonly ILogger<WebSocketClient> _logger;
        private WebSocketSharp.WebSocket _webSocket;

        #endregion Private Fields

        #region Constructors

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="logger"></param>
        public WebSocketClientSharp(ILogger<WebSocketClient> logger = null)
        {
            _logger = logger;
        }

        #endregion Constructors

        #region Public Methods

        public async Task StreamAsync(Uri uri, CancellationToken token)
        {
            Throw.IfNull(uri, nameof(uri));

            if (!token.CanBeCanceled)
                throw new ArgumentException("Token must be capable of being in the canceled state.", nameof(token));

            token.ThrowIfCancellationRequested();

            IsStreaming = true;

            _webSocket = new WebSocketSharp.WebSocket(uri.ToString());
            _webSocket.EmitOnPing = true;
            _webSocket.WaitTime = TimeSpan.FromSeconds(30);

            _webSocket.OnMessage += WebSocket_OnMessage;
            _webSocket.OnError += WebSocket_OnError;
            _webSocket.OnClose += WebSocket_OnClose;

            try
            {
                try
                {
                    _webSocket.Connect();

                    if (_webSocket.ReadyState == WebSocketState.Open)
                        RaiseOpenEvent();
                }
                catch (OperationCanceledException) { }
                catch (Exception e)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _logger?.LogError(e, $"{nameof(WebSocketClient)}.{nameof(StreamAsync)}: WebSocket connect exception.");
                        throw;
                    }
                }


            }
            finally
            {
                IsStreaming = false;

                // NOTE: WebSocketState.CloseSent should not be encountered since CloseOutputAsync is not used.
                if (_webSocket.ReadyState == WebSocketState.Open || _webSocket.ReadyState == WebSocketState.Closing)
                {
                    _webSocket.Close();
                }

                RaiseCloseEvent();
            }
        }

        private void WebSocket_OnClose(object sender, CloseEventArgs e)
        {
            _logger.LogDebug("Close request received");
            _webSocket?.Close();
            IsStreaming = false;
            RaiseCloseEvent();
        }

        private void WebSocket_OnError(object sender, ErrorEventArgs e)
        {
            _logger.LogError(e.Message);
            _logger.LogError(e.Exception?.StackTrace);
            _webSocket?.Close();
            IsStreaming = false;
            RaiseCloseEvent();
        }

        private void WebSocket_OnMessage(object sender, MessageEventArgs e)
        {
            if (e.IsBinary)
            {
                _logger?.LogWarning($"{nameof(WebSocketClient)}.{nameof(StreamAsync)}: Received unsupported binary message type.");
                return;
            }

            var msg = Encoding.UTF8.GetString(e.RawData);
            _logger?.LogDebug("Received message: "+msg);

            if (!string.IsNullOrWhiteSpace(msg))
            {
                RaiseMessageEvent(new WebSocketClientEventArgs(msg));
            }
            else
            {
                _logger?.LogWarning($"{nameof(WebSocketClient)}.{nameof(StreamAsync)}: Received empty JSON message.");
            }
        }

        #endregion Public Methods

        #region Private Methods

        /// <summary>
        /// Raise open event.
        /// </summary>
        private void RaiseOpenEvent()
        {
            try { Open?.Invoke(this, EventArgs.Empty); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _logger?.LogError(e, $"{nameof(WebSocketClient)}: Unhandled open event handler exception.");
            }
        }

        /// <summary>
        /// Raise message event.
        /// </summary>
        /// <param name="args"></param>
        private void RaiseMessageEvent(WebSocketClientEventArgs args)
        {
            try { Message?.Invoke(this, args); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _logger?.LogError(e, $"{nameof(WebSocketClient)}: Unhandled message event handler exception.");
            }
        }

        /// <summary>
        /// Raise close event.
        /// </summary>
        private void RaiseCloseEvent()
        {
            try { Close?.Invoke(this, EventArgs.Empty); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _logger?.LogError(e, $"{nameof(WebSocketClient)}: Unhandled close event handler exception.");
            }
        }

        #endregion Private Methods
    }
}
