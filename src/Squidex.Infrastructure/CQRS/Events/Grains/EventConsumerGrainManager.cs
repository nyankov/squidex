﻿// ==========================================================================
//  EventConsumerActorManager.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Squidex.Infrastructure.CQRS.Events.Grains.Messages;
using Squidex.Infrastructure.States;

namespace Squidex.Infrastructure.CQRS.Events.Grains
{
    public sealed class EventConsumerGrainManager : DisposableObjectBase, IExternalSystem
    {
        private readonly IStateFactory factory;
        private readonly IPubSub pubSub;
        private readonly List<IEventConsumer> consumers;
        private readonly List<IDisposable> subscriptions = new List<IDisposable>();

        public EventConsumerGrainManager(IEnumerable<IEventConsumer> consumers, IPubSub pubSub, IStateFactory factory)
        {
            Guard.NotNull(pubSub, nameof(pubSub));
            Guard.NotNull(factory, nameof(factory));
            Guard.NotNull(consumers, nameof(consumers));

            this.pubSub = pubSub;
            this.factory = factory;
            this.consumers = consumers.ToList();
        }

        public void Connect()
        {
            var actors = new Dictionary<string, EventConsumerGrain>();

            foreach (var consumer in consumers)
            {
                var actor = factory.GetDetachedAsync<EventConsumerGrain, EventConsumerState>(consumer.Name).Result;

                actors[consumer.Name] = actor;
                actor.Activate(consumer);
            }

            subscriptions.Add(pubSub.Subscribe<StartConsumerMessage>(m =>
            {
                if (actors.TryGetValue(m.ConsumerName, out var actor))
                {
                    actor.Start();
                }
            }));

            subscriptions.Add(pubSub.Subscribe<StopConsumerMessage>(m =>
            {
                if (actors.TryGetValue(m.ConsumerName, out var actor))
                {
                    actor.Stop();
                }
            }));

            subscriptions.Add(pubSub.Subscribe<ResetConsumerMessage>(m =>
            {
                if (actors.TryGetValue(m.ConsumerName, out var actor))
                {
                    actor.Reset();
                }
            }));

            subscriptions.Add(pubSub.ReceiveAsync<GetStatesRequest, GetStatesResponse>(request =>
            {
                var states = actors.Values.Select(x => x.GetState()).ToArray();

                return Task.FromResult(new GetStatesResponse { States = states });
            }));
        }

        protected override void DisposeObject(bool disposing)
        {
            if (disposing)
            {
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
            }
        }
    }
}
