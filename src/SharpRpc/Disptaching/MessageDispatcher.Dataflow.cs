﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace SharpRpc
{
    partial class MessageDispatcher
    {
        //private class Dataflow : MessageDispatcher
        //{
        //    private readonly ActionBlock<IMessage> _dataflowBlock;

        //    public Dataflow(int degreeOfParallelism)
        //    {
        //        var options = new ExecutionDataflowBlockOptions();
        //        options.MaxDegreeOfParallelism = degreeOfParallelism;

        //        _dataflowBlock = new ActionBlock<IMessage>(InvokeHandlerAsync, options);
        //    }

        //    public override bool SuportsBatching => false;

        //    public override void OnMessage(IMessage message)
        //    {
        //        _dataflowBlock.Post(message);
        //    }

        //    public override void OnMessages(IEnumerable<IMessage> messages)
        //    {
        //        throw new NotImplementedException();
        //    }

        //    public override Task Close(bool dropTheQueue)
        //    {
        //        _dataflowBlock.Complete();
        //        return _dataflowBlock.Completion;
        //    }

        //    private Task InvokeHandlerAsync(IMessage msg)
        //    {
        //        try
        //        {
        //            var retVal = MessageHandler.ProcessMessage(msg);

        //            if (!retVal.IsCompletedSuccessfully)
        //                return retVal.AsTask().ContinueWith(t =>
        //                {
        //                    if (t.IsFaulted)
        //                        OnError(RpcRetCode.MessageHandlerFailure, t.Exception.Message);
        //                });
        //            else
        //                return Task.CompletedTask;
        //        }
        //        catch (Exception ex)
        //        {
        //            OnError(RpcRetCode.MessageHandlerFailure, ex.Message);
        //            return Task.CompletedTask;
        //        }
        //    }
        //}
    }
}