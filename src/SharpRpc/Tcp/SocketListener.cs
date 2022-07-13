﻿// Copyright © 2022 Soft-Fx. All rights reserved.
// Author: Andrei Hilevich
//
// This Source Code Form is subject to the terms of the Mozilla
// Public License, v. 2.0. If a copy of the MPL was not distributed
// with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SharpRpc.Tcp
{
    internal class SocketListener
    {
        private readonly LoggerFacade _logger;
        private readonly string _logId;
        private readonly Socket _socket;
        private readonly ServerEndpoint _endpoint;
        private volatile bool _stopFlag;
        private Task _listenerTask;
        private readonly Action<ByteTransport> _onConnect;
        private readonly TcpServerSecurity _security;

        public SocketListener(Socket socket, ServerEndpoint endpoint, TcpServerSecurity security, Action<ByteTransport> onConnect)
        {
            _socket = socket ?? throw new ArgumentNullException(nameof(socket));
            _endpoint = endpoint ?? throw new ArgumentNullException(nameof(_endpoint));
            _security = security ?? throw new ArgumentNullException(nameof(security));
            _logId = endpoint.Name;
            _logger = endpoint.LoggerAdapter;
            _onConnect = onConnect ?? throw new ArgumentNullException(nameof(onConnect));
        }

        public void Start(EndPoint socketEndpoint)
        {
            _security.Init();

            _socket.Bind(socketEndpoint);
            _socket.Listen(100);

            _listenerTask = AcceptLoop();
        }

        public async Task Stop()
        {
            _stopFlag = true;
            _socket.Close();
            await _listenerTask;
        }

        private async Task AcceptLoop()
        {
            while (!_stopFlag)
            {
                Socket socket;

                try
                {
                    socket = await _socket.AcceptAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var socketEx = ex as SocketException;

                    if (!_stopFlag || socketEx == null || socketEx.SocketErrorCode != SocketError.OperationAborted)
                        _logger.Error(_logId, ex.Message);

                    continue;
                }

                try
                {
                    var transport = await _security.SecureTransport(socket, _endpoint);

                    _onConnect(transport);
                }
                catch (Exception ex)
                {
                    _logger.Error(_logId, ex.Message);

                    try
                    {
                        await socket.DisconnectAsync(_endpoint.TaskQueue);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}