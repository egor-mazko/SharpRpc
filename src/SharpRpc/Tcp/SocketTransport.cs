﻿// Copyright © 2022 Soft-Fx. All rights reserved.
// Author: Andrei Hilevich
//
// This Source Code Form is subject to the terms of the Mozilla
// Public License, v. 2.0. If a copy of the MPL was not distributed
// with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SharpRpc.Tcp;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SharpRpc
{
    internal class SocketTransport : ByteTransport
    {
        private readonly Socket _socket;
        private readonly TaskFactory _taskFactory;
        //private readonly bool _isServer;
        
        public SocketTransport(Socket socket, TaskFactory taskQueue)
        {
            _socket = socket;
            _taskFactory = taskQueue;
        }

        internal Socket Socket => _socket;

        public override void Init(Channel channel)
        {
            //_channelId = channel.Id;
        }

#if NET5_0_OR_GREATER
        public override ValueTask<int> Receive(ArraySegment<byte> buffer, CancellationToken cToken)
        {
            return new ValueTask<int>(_socket.ReceiveAsync(buffer, SocketFlags.None));
        }

        public override ValueTask Send(ArraySegment<byte> data, CancellationToken cToken)
        {
            return new ValueTask(_socket.SendAsync(data, SocketFlags.None));
        }
#else
        protected override Task<int> ReceiveInternal(ArraySegment<byte> buffer, CancellationToken cToken)
        {
            //return _stream.ReadAsync(buffer.Array, buffer.Offset, buffer.Count, cToken);
            return _socket.ReceiveAsync(buffer, SocketFlags.None);
        }

        protected override Task SendInternal (ArraySegment<byte> data, CancellationToken cToken)
        {
            //return _stream.WriteAsync(data.Array, data.Offset, data.Count, cToken);
            return _socket.SendAsync(data, SocketFlags.None);
        }
#endif

        public override RpcResult TranslateException(Exception ex)
        {
            return ToRpcResult(ex);
        }

        public static RpcResult ToRpcResult(Exception ex)
        {
            var socketEx = ex as SocketException ?? ex.InnerException as SocketException;

            if (socketEx != null)
            {
                switch (socketEx.SocketErrorCode)
                {
                    case SocketError.TimedOut: return new RpcResult(RpcRetCode.ConnectionTimeout, socketEx.Message);
                    case SocketError.Shutdown: return new RpcResult(RpcRetCode.ConnectionAbortedByPeer, socketEx.Message);
                    case SocketError.OperationAborted: return new RpcResult(RpcRetCode.OperationCanceled, socketEx.Message);
                    case SocketError.ConnectionAborted: return new RpcResult(RpcRetCode.OperationCanceled, ex.Message);
                    case SocketError.ConnectionReset: return new RpcResult(RpcRetCode.ConnectionAbortedByPeer, ex.Message);
                    case SocketError.HostNotFound: return new RpcResult(RpcRetCode.HostNotFound, ex.Message);
                    case SocketError.HostUnreachable: return new RpcResult(RpcRetCode.HostUnreachable, ex.Message);
                    case SocketError.ConnectionRefused: return new RpcResult(RpcRetCode.ConnectionRefused, ex.Message);
                    default: return new RpcResult(RpcRetCode.OtherConnectionError, ex.Message);
                }
            }
            else if (ex is Win32Exception w32ex && w32ex.Source == "System.Net.Security")
            {
                return new RpcResult(RpcRetCode.SecurityError, ex.Message);
            }
            else if (ex is ObjectDisposedException || ex.InnerException is ObjectDisposedException)
            {
                return new RpcResult(RpcRetCode.OperationCanceled, ex.Message);
            }

            return new RpcResult(RpcRetCode.OtherConnectionError, "An unexpected exception is occurred in TcpTransport: " + ex.Message);
        }

#if NET5_0_OR_GREATER
        public override async Task Shutdown()
#else
        protected override async Task ShutdownInternal()
#endif
        {
            try
            {
                await _socket.DisconnectAsync(_taskFactory);
            }
            catch (Exception)
            {
                // TO DO : log
            }

            //try
            //{
            //    await _socket.DisconnectAsync(_taskFactory);
            //}
            //catch (Exception)
            //{
            //    // TO DO : log
            //}
        }

#if NET5_0_OR_GREATER
        public override void Dispose()
#else
        protected override void DisposeInternal()
#endif
        {
            _socket.Dispose();
        }

        public override TransportInfo GetInfo() => CreateInfobject(_socket);

        internal static TcpConnectionInfo CreateInfobject(Socket socket)
        {
            return new TcpConnectionInfo(socket.RemoteEndPoint as IPEndPoint, socket.LocalEndPoint as IPEndPoint);
        }
    }
}
