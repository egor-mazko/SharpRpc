﻿// Copyright © 2021 Soft-Fx. All rights reserved.
// Author: Andrei Hilevich
//
// This Source Code Form is subject to the terms of the Mozilla
// Public License, v. 2.0. If a copy of the MPL was not distributed
// with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpRpc
{
    public interface IStreamContext : CallContext
    {
        //string CallId { get; }
        Task Close(Channel ch);
    }

    public class ServiceStreamingCallContext<TInItem, TOutItem> : IStreamContext, MessageDispatcherCore.IInteropOperation
    {
        private readonly CancellationTokenSource _cancelSrc;

        public ServiceStreamingCallContext(IOpenStreamRequest request, Channel ch, IStreamMessageFactory<TInItem> inFactory, IStreamMessageFactory<TOutItem> outFactory)
        {
            RequestMessage = request;
            CallId = request.CallId;

            //System.Diagnostics.Debug.WriteLine("RX " + CallId + " RQ " + request.GetType().Name);

            if ((request.Options & RequestOptions.CancellationEnabled) != 0)
            {
                _cancelSrc = new CancellationTokenSource();
                CancellationToken = _cancelSrc.Token;
            }
            else
                CancellationToken = CancellationToken.None;

            if (inFactory != null)
                InputStream = new PagingStreamReader<TInItem>(CallId, ch.Tx, inFactory);

            if (outFactory != null)
                OutputStream = new PagingStreamWriter<TOutItem>(CallId, ch, outFactory, true, new StreamOptions(request));

            ch.Dispatcher.RegisterCallObject(request.CallId, this);
        }

        public string CallId { get; }
        public PagingStreamReader<TInItem> InputStream { get; }
        public PagingStreamWriter<TOutItem> OutputStream { get; }
        public IRequestMessage RequestMessage { get; }
        public CancellationToken CancellationToken { get; }

        public void StartCancellation() { }

        public async Task Close(Channel ch)
        {
            //System.Diagnostics.Debug.WriteLine("CLOSE " + CallId);

            InputStream?.Abort();

            if (OutputStream != null)
                await OutputStream.CompleteAsync();

            ch.Dispatcher.UnregisterCallObject(CallId);
        }

        RpcResult MessageDispatcherCore.IInteropOperation.Complete(IResponseMessage respMessage)
        {
            return new RpcResult(RpcRetCode.ProtocolViolation, "");
        }

        void MessageDispatcherCore.IInteropOperation.Fail(RpcResult result)
        {
        }

        void MessageDispatcherCore.IInteropOperation.Fail(IRequestFaultMessage faultMessage)
        {
        }

        RpcResult MessageDispatcherCore.IInteropOperation.Update(IInteropMessage auxMessage)
        {
            //System.Diagnostics.Debug.WriteLine("RX " + CallId + " A.MSG " + auxMessage.GetType().Name);

            if (auxMessage is IStreamPage<TInItem> page)
            {
                InputStream.OnRx(page);
                return RpcResult.Ok;
            }
            else if (auxMessage is IStreamCompletionMessage compl)
            {
                InputStream.OnRx(compl);
                return RpcResult.Ok;
            }
            else if (auxMessage is IStreamPageAck ack)
            {
                OutputStream.OnRx(ack);
                return RpcResult.Ok;
            }
            else if (auxMessage is ICancelRequestMessage)
            {
                _cancelSrc?.Cancel();
                InputStream?.Abort();
            }

            return new RpcResult(RpcRetCode.ProtocolViolation, "");
        }
    }
}
