﻿namespace NServiceBus.Pipeline
{
    using System;
    using System.Collections.Generic;
    using Audit;
    using Contexts;
    using DataBus;
    using MessageMutator;
    using NServiceBus.MessageMutator;
    using ObjectBuilder;
    using Sagas;
    using Unicast;
    using Unicast.Behaviors;
    using Unicast.Messages;
    using UnitOfWork;

    class PipelineFactory : IDisposable
    {
        public IBuilder RootBuilder { get; set; }
        public List<PipelineOverride> PipelineOverrides { get; set; }

        public void PreparePhysicalMessagePipelineContext(TransportMessage message, bool messageHandlingDisabled)
        {
            contextStacker.Push(new ReceivePhysicalMessageContext(CurrentContext, message, messageHandlingDisabled));
        }

        public void InvokeReceivePhysicalMessagePipeline()
        {
            var context = contextStacker.Current as ReceivePhysicalMessageContext;

            if(context == null)
            {
                throw new InvalidOperationException("Can't invoke the receive pipeline when the current context is: " + contextStacker.Current.GetType().Name);
            }

            var behaviorList = new BehaviorList<ReceivePhysicalMessageContext>();
            behaviorList.Add<ChildContainerBehavior>();
            behaviorList.Add<MessageHandlingLoggingBehavior>();
            behaviorList.Add<ImpersonateSenderBehavior>();
            behaviorList.Add<AuditBehavior>();
            behaviorList.Add<ForwardBehavior>();
            behaviorList.Add<UnitOfWorkBehavior>();
            behaviorList.Add<ApplyIncomingTransportMessageMutatorsBehavior>();
            behaviorList.Add<RaiseMessageReceivedBehavior>();
            behaviorList.Add<ExtractLogicalMessagesBehavior>();
            behaviorList.Add<CallbackInvocationBehavior>();

            foreach (var pipelineOverride in PipelineOverrides)
            {
                pipelineOverride.Override(behaviorList);
            }

            var pipeline = new BehaviorChain<ReceivePhysicalMessageContext>(behaviorList);
            pipeline.Invoke(context);
        }

        public void CompletePhysicalMessagePipelineContext()
        {
            contextStacker.Pop();
        }

        public void InvokeLogicalMessagePipeline(LogicalMessage message)
        {
            var behaviorList = new BehaviorList<ReceiveLogicalMessageContext>();
            behaviorList.Add<ApplyIncomingMessageMutatorsBehavior>();
            //todo: we'll make this optional as soon as we have a way to manipulate the pipeline
            behaviorList.Add<DataBusReceiveBehavior>();
            behaviorList.Add<LoadHandlersBehavior>();
            
            foreach (var pipelineOverride in PipelineOverrides)
            {
                pipelineOverride.Override(behaviorList);
            }

            var pipeline = new BehaviorChain<ReceiveLogicalMessageContext>(behaviorList);
            var context = new ReceiveLogicalMessageContext(CurrentContext, message);

            contextStacker.Push(context);

            pipeline.Invoke(context);

            contextStacker.Pop();
        }

        public HandlerInvocationContext InvokeHandlerPipeline(MessageHandler handler)
        {
            var behaviorList = new BehaviorList<HandlerInvocationContext>();
            behaviorList.Add<SagaPersistenceBehavior>();
            behaviorList.Add<InvokeHandlersBehavior>();

            foreach (var pipelineOverride in PipelineOverrides)
            {
                pipelineOverride.Override(behaviorList);
            }

            var pipeline = new BehaviorChain<HandlerInvocationContext>(behaviorList);
            var context = new HandlerInvocationContext(CurrentContext, handler);

            contextStacker.Push(context);

            pipeline.Invoke(context);

            contextStacker.Pop();

            return context;
        }

        public SendLogicalMessagesContext InvokeSendPipeline(SendOptions sendOptions, IEnumerable<LogicalMessage> messages)
        {
            var behaviorList = new BehaviorList<SendLogicalMessagesContext>();
            behaviorList.Add<MultiSendValidatorBehavior>();
            behaviorList.Add<MultiMessageBehavior>();
            behaviorList.Add<CreatePhysicalMessageBehavior>();

            foreach (var pipelineOverride in PipelineOverrides)
            {
                pipelineOverride.Override(behaviorList);
            }

            var pipeline = new BehaviorChain<SendLogicalMessagesContext>(behaviorList);
            var context = new SendLogicalMessagesContext(CurrentContext, sendOptions, messages);

            contextStacker.Push(context);

            pipeline.Invoke(context);

            contextStacker.Pop();

            return context;
        }

        public SendLogicalMessageContext InvokeSendPipeline(SendOptions sendOptions, LogicalMessage message)
        {
            var behaviorList = new BehaviorList<SendLogicalMessageContext>();

            behaviorList.Add<SendValidatorBehavior>();
            behaviorList.Add<SagaSendBehavior>();
            behaviorList.Add<MutateOutgoingMessageBehavior>();
            //todo: we'll make this optional as soon as we have a way to manipulate the pipeline
            behaviorList.Add<DataBusSendBehavior>();

            foreach (var pipelineOverride in PipelineOverrides)
            {
                pipelineOverride.Override(behaviorList);
            }

            var pipeline = new BehaviorChain<SendLogicalMessageContext>(behaviorList);
            var context = new SendLogicalMessageContext(CurrentContext, sendOptions, message);

            contextStacker.Push(context);

            pipeline.Invoke(context);

            contextStacker.Pop();

            return context;
        }

        public void InvokeSendPipeline(SendOptions sendOptions, TransportMessage physicalMessage)
        {
            var behaviorList = new BehaviorList<SendPhysicalMessageContext>();

            behaviorList.Add<SerializeMessagesBehavior>();
            behaviorList.Add<MutateOutgoingPhysicalMessageBehavior>();
            behaviorList.Add<DispatchMessageToTransportBehavior>();

            foreach (var pipelineOverride in PipelineOverrides)
            {
                pipelineOverride.Override(behaviorList);
            }

            var pipeline = new BehaviorChain<SendPhysicalMessageContext>(behaviorList);
            var context = new SendPhysicalMessageContext(CurrentContext, sendOptions, physicalMessage);

            contextStacker.Push(context);

            pipeline.Invoke(context);

            contextStacker.Pop();
        }

        public BehaviorContext CurrentContext
        {
            get
            {
                var current = contextStacker.Current;

                if (current != null)
                {
                    return current;
                }

                contextStacker.Push(new RootContext(RootBuilder));

                return contextStacker.Current;
            }
        }

        public void Dispose()
        {
            //Injected
        }

        BehaviorContextStacker contextStacker = new BehaviorContextStacker();

    }
}