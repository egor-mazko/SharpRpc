﻿// Copyright © 2021 Soft-Fx. All rights reserved.
// Author: Andrei Hilevich
//
// This Source Code Form is subject to the terms of the Mozilla
// Public License, v. 2.0. If a copy of the MPL was not distributed
// with this file, You can obtain one at http://mozilla.org/MPL/2.0/.

using SharpRpc.Lib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SharpRpc
{
#if NET5_0_OR_GREATER
    internal partial class TxBuffer : MessageWriter, System.Buffers.IBufferWriter<byte>
#else
    internal partial class TxBuffer : MessageWriter
#endif
    {
        private readonly object _lockObj;
        private readonly StreamProxy _streamProxy;
        private readonly Queue<ArraySegment<byte>> _completeSegments = new Queue<ArraySegment<byte>>();
        private readonly MemoryManager _memManager;
        private readonly int _minAllocSize = 64;
        private readonly MessageMarker _marker;
        private readonly SlimAwaitable<ArraySegment<byte>> _dequeueWaitHandle;
        private bool _isDequeueAwaited;
        //private readonly Action _dataArrivedEvent;
        private ArraySegment<byte> _dequeuedSegment;
        private bool _isClosed;
        private byte[] _xlBuffer;
        private readonly int _segmentSize;
        private readonly int _maxAllocSize;

        public TxBuffer(object lockObj, int segmentSize, TaskFactory tFactory)
        {
            _lockObj = lockObj;

            _segmentSize = segmentSize;
            _maxAllocSize = segmentSize - MessageHeader.HeaderSize;

            _dequeueWaitHandle = new SlimAwaitable<ArraySegment<byte>>(lockObj, tFactory);

            //if (segmentSize > ushort.MaxValue)
            //    throw new ArgumentException("Segment size must be less than " + ushort.MaxValue + ".");

            //_dataArrivedEvent = dataArrivedCallback;

            _memManager = new PoolBasedManager(segmentSize);
            //_minAllocSize = minSizeHint;
            _streamProxy = new StreamProxy(this);

            _marker = new MessageMarker(this);

            AllocNewSegment();
        }

        public bool IsCurrentSegmentLocked { get; private set; }
        public bool IsDataAvailable => IsCurrentDataAvailable || HasCompletedSegments;

        public long DataSize { get; private set; }
        public int CurrentWriteSize { get; private set; }

        private byte[] CurrentSegment { get; set; }
        private int CurrentOffset { get; set; }

        public event Action<TxBuffer> SpaceFreed;

        private bool IsCurrentDataAvailable => !IsCurrentSegmentLocked && CurrentOffset > 0;
        private bool HasCompletedSegments => _completeSegments.Count > 0;
        private int RemainingSegmentSpace => _segmentSize - CurrentOffset;

        public event Action OnDequeue;

        // should be called under lock
        public void Lock()
        {
            Debug.Assert(Monitor.IsEntered(_lockObj));
            //Debug.Assert(!IsCurrentSegmentLocked);

            //lock (_lockObj)
            IsCurrentSegmentLocked = true;
        }

        public void StartMessageWrite(bool isSimplEncoding)
        {
            Debug.Assert(!Monitor.IsEntered(_lockObj));
            Debug.Assert(IsCurrentSegmentLocked);

            _marker.OnMessageStart(isSimplEncoding);
        }

        public void EndMessageWrite()
        {
            _marker.OnMessageEnd();

            lock (_lockObj)
            {
                Debug.Assert(IsCurrentSegmentLocked);

                IsCurrentSegmentLocked = false;
                ApplyCurrentDataSize();
                SignalDataAvailable();
            }
        }

        // should be called under lock
        public void Close()
        {
            _isClosed = true;

            if (_isDequeueAwaited)
            {
                _dequeueWaitHandle.SetCompleted(new ArraySegment<byte>());
                _isDequeueAwaited = false;
            }
        }

        public SlimAwaitable<ArraySegment<byte>> DequeueNext()
        {
            Debug.Assert(!Monitor.IsEntered(_lockObj));

            lock (_lockObj)
            {
                if (_dequeuedSegment.Array != null)
                {
                    _memManager.FreeSegment(_dequeuedSegment);
                    _dequeuedSegment = new ArraySegment<byte>();
                }

                var hasCurrentData = IsCurrentDataAvailable;

                _dequeueWaitHandle.Reset();

                //Debug.Assert(!_isDequeueAwaited);

                if (HasCompletedSegments || hasCurrentData)
                {
                    var result = Dequeue();
                    SpaceFreed?.Invoke(this);
                    _dequeueWaitHandle.SetCompleted(result);
                }
                else if (_isClosed)
                {
                    _dequeueWaitHandle.SetCompleted(new ArraySegment<byte>());
                }
                else
                    _isDequeueAwaited = true;

                return _dequeueWaitHandle;
            }
        }

        private void SignalDataAvailable()
        {
            if (_isDequeueAwaited)
            {
                _isDequeueAwaited = false;
                _dequeueWaitHandle.SetCompleted(Dequeue(), true);
            }
        }

        private ArraySegment<byte> Dequeue()
        {
            Debug.Assert(Monitor.IsEntered(_lockObj), "This method must be called under lock!");

            if (_completeSegments.Count == 0)
                CompleteCurrentSegment();

            _dequeuedSegment = _completeSegments.Dequeue();
            DataSize -= _dequeuedSegment.Count;

            OnDequeue?.Invoke();

            return _dequeuedSegment;
        }

        #region IBufferWriter implementation

        public void Advance(int count)
        {
            if (_xlBuffer != null)
                ConsumeXlBuffer(count);
            else
                MoveOffset(count);
        }

#if NET5_0_OR_GREATER
        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            //if (_xlBuffer != null)
            //    throw new Exception("Advance() must be called before another memory request!");

            if (IsXlSize(sizeHint))
            {
                AllocateXlBuffer(sizeHint);
                return new Memory<byte>(_xlBuffer);
            }
            else
            {
                EnsureSpace(sizeHint);
                return new Memory<byte>(CurrentSegment, CurrentOffset, RemainingSegmentSpace);
            }
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            if (IsXlSize(sizeHint))
            {
                AllocateXlBuffer(sizeHint);
                return new Span<byte>(_xlBuffer);
            }
            else
            {
                EnsureSpace(sizeHint);
                return new Span<byte>(CurrentSegment, CurrentOffset, RemainingSegmentSpace);
            }
        }
#endif
        #endregion

        public void AdvanceWriteBuffer(int count)
        {
            Advance(count);
        }

        public ArraySegment<byte> AllocateWriteBuffer(int sizeHint = 0)
        {
            EnsureSpace(sizeHint);
            return new ArraySegment<byte>(CurrentSegment, CurrentOffset, RemainingSegmentSpace);
        }

        private bool IsXlSize(int sizeHint)
        {
            return sizeHint > _maxAllocSize;
        }

        private void AllocateXlBuffer(int size)
        {
            _xlBuffer = new byte[size];
        }

        private void EnsureSpace(int size)
        {
            if (size <= _minAllocSize)
                size = _minAllocSize;

            var remSpace = RemainingSegmentSpace;

            if (_marker.IsHeaderWritePending)
                remSpace -= MessageHeader.HeaderSize;

            if (remSpace < size)
            {
                lock (_lockObj)
                {
                    ApplyCurrentDataSize();
                    CompleteCurrentSegment();
                    SignalDataAvailable();
                }
            }

            _marker.OnAlloc();
        }

        private void CompleteCurrentSegment()
        {
            _marker.OnSegmentClose();
            _completeSegments.Enqueue(new ArraySegment<byte>(CurrentSegment, 0, CurrentOffset));
            AllocNewSegment();
        }

        private void AllocNewSegment()
        {
            CurrentSegment = _memManager.AllocateSegment();
            CurrentOffset = 0;
        }

        private void Write(byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                EnsureSpace(_minAllocSize);

                var spaceLeft = RemainingSegmentSpace;
                var copySize = Math.Min(spaceLeft, count);
                Buffer.BlockCopy(buffer, offset, CurrentSegment, CurrentOffset, copySize);

                MoveOffset(copySize);
                offset += copySize;
                count -= copySize;
            }
        }

        private void MoveOffset(int size)
        {
            CurrentOffset += size;
            CurrentWriteSize += size;
            //System.Diagnostics.Debug.WriteLine("DataSize=" + DataSize + " +" + size);
        }

        private void ApplyCurrentDataSize()
        {
            Debug.Assert(Monitor.IsEntered(_lockObj), "This method must be called under lock!");

            DataSize += CurrentWriteSize;
            CurrentWriteSize = 0;
        }

        private void ConsumeXlBuffer(int count)
        {
            Write(_xlBuffer, 0, count);
            _xlBuffer = null;
        }

        #region MessageWriter implementation

#if NET5_0_OR_GREATER
        System.Buffers.IBufferWriter<byte> MessageWriter.ByteBuffer => this;
#endif
        System.IO.Stream MessageWriter.ByteStream => _streamProxy;
        
        #endregion
    }
}
