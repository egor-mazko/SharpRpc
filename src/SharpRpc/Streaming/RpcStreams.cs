﻿// Copyright © 2022 Soft-Fx. All rights reserved.
// Author: Andrei Hilevich
//
// This Source Code Form is subject to the terms of the Mozilla
// Public License, v. 2.0. If a copy of the MPL was not distributed
// with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SharpRpc.Disptaching;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpRpc.Streaming
{
    public static class RpcStreams
    {
        internal static IStreamWriterFixture<T> CreateWriter<T>(string callId, TxPipeline msgTransmitter, IStreamMessageFactory<T> factory,
            bool allowSending, StreamOptions options, IRpcLogger logger)
        {
            if (typeof(T) == typeof(byte))
                return (IStreamWriterFixture<T>)new BinaryStreamWriter(callId, msgTransmitter, factory, allowSending, options, logger);
            else
                return new ObjectStreamWriter<T>(callId, msgTransmitter, factory, allowSending, options, logger);
        }

        internal static IStreamReaderFixture<T> CreateReader<T>(string callId, TxPipeline tx, IStreamMessageFactory<T> factory, IRpcLogger logger)
        {
            if (typeof(T) == typeof(byte))
                return (IStreamReaderFixture<T>)new BinaryStreamReader(callId, tx, factory, logger);
            else
                return new ObjectStreamReader<T>(callId, tx, factory, logger);
        }

        public static IStreamBulkEnumerator<byte> GetBulkEnumerator(this StreamReader<byte> reader)
        {
            return GetBulkEnumerator(reader, CancellationToken.None);
        }

        public static IStreamBulkEnumerator<byte> GetBulkEnumerator(this StreamReader<byte> reader, CancellationToken cToken)
        {
            return ((BinaryStreamReader)reader).GetBulkEnumeratorInternal(cToken);
        }

#if NET5_0_OR_GREATER
        public static async ValueTask<RpcResult> WriteAllAsync(this StreamWriter<byte> writer, Stream stream)
#else
        public static async Task<RpcResult> WriteAllAsync(this StreamWriter<byte> writer, Stream stream)
#endif
        {
            var binWriter = (BinaryStreamWriter)writer;

            while (true)
            {
                int bytesRead = 0;

                try
                {
                    var startResult = await binWriter.StartBulkWrite().ConfigureAwait(false);

                    if (!startResult.IsOk)
                        return startResult;

                    var buffer = startResult.Value;
                    bytesRead = await stream.ReadAsync(buffer.Array, buffer.Offset, buffer.Count).ConfigureAwait(false);

                    if (bytesRead == 0)
                        return RpcResult.Ok;
                }
                finally
                {
                    binWriter.CommitBulkWrite(bytesRead);
                }
            }
        }

#if NET5_0_OR_GREATER
        public static async ValueTask ReadAllAsync(this StreamReader<byte> reader, Stream targetStream)
#else
        public static async Task ReadAllAsync(this StreamReader<byte> reader, Stream targetStream)
#endif
        {
            var binReader = (BinaryStreamReader)reader;
            var e = binReader.GetPageEnumerator();

            try
            {
                while (await e.MoveNextAsync().ConfigureAwait(false))
                {
                    var segment = e.Current;
                    await targetStream.WriteAsync(segment.Array, segment.Offset, segment.Count).ConfigureAwait(false);
                }
            }
            finally
            {
                await e.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
