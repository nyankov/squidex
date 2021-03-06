﻿// ==========================================================================
//  EventConsumerGrainTests.cs
//  Squidex Headless CMS
// ==========================================================================
//  Copyright (c) Squidex Group
//  All rights reserved.
// ==========================================================================

using System;
using System.Threading.Tasks;
using FakeItEasy;
using FluentAssertions;
using Squidex.Infrastructure.Log;
using Squidex.Infrastructure.States;
using Xunit;

namespace Squidex.Infrastructure.CQRS.Events.Grains
{
    public class EventConsumerGrainTests
    {
        public sealed class MyEvent : IEvent
        {
        }

        public sealed class MyEventConsumerGrain : EventConsumerGrain
        {
            public MyEventConsumerGrain(EventDataFormatter formatter, IEventStore eventStore, ISemanticLog log)
                : base(formatter, eventStore, log)
            {
            }

            protected override IEventSubscription CreateSubscription(IEventStore eventStore, string streamFilter, string position)
            {
                return eventStore.CreateSubscription(this, streamFilter, position);
            }
        }

        private readonly IEventConsumer eventConsumer = A.Fake<IEventConsumer>();
        private readonly IEventStore eventStore = A.Fake<IEventStore>();
        private readonly IEventSubscriber sutSubscriber;
        private readonly IEventSubscription eventSubscription = A.Fake<IEventSubscription>();
        private readonly ISemanticLog log = A.Fake<ISemanticLog>();
        private readonly IStateHolder<EventConsumerState> stateHolder = A.Fake<IStateHolder<EventConsumerState>>();
        private readonly EventDataFormatter formatter = A.Fake<EventDataFormatter>();
        private readonly EventData eventData = new EventData();
        private readonly Envelope<IEvent> envelope = new Envelope<IEvent>(new MyEvent());
        private readonly EventConsumerGrain sut;
        private readonly string consumerName;
        private readonly string initialPosition = Guid.NewGuid().ToString();
        private EventConsumerState state = new EventConsumerState();

        public EventConsumerGrainTests()
        {
            state.Position = initialPosition;

            consumerName = eventConsumer.GetType().Name;

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .Returns(eventSubscription);

            A.CallTo(() => eventConsumer.Name).
                Returns(consumerName);

            A.CallTo(() => stateHolder.State)
                .ReturnsLazily(() => state);

            A.CallToSet(() => stateHolder.State)
                .Invokes(new Action<EventConsumerState>(s => state = s));

            A.CallTo(() => formatter.Parse(eventData, true)).Returns(envelope);

            sut = new MyEventConsumerGrain(formatter, eventStore, log);
            sutSubscriber = sut;

            sut.ActivateAsync(stateHolder).Wait();
        }

        [Fact]
        public void Should_not_subscribe_to_event_store_when_stopped_in_db()
        {
            state = state.Stopped();

            sut.Activate(eventConsumer);
            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = true, Position = initialPosition, Error = null });

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .MustNotHaveHappened();
        }

        [Fact]
        public void Should_subscribe_to_event_store_when_not_found_in_db()
        {
            sut.Activate(eventConsumer);
            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = initialPosition, Error = null });

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_subscribe_to_event_store_when_not_stopped_in_db()
        {
            sut.Activate(eventConsumer);
            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = initialPosition, Error = null });

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_stop_subscription_when_stopped()
        {
            sut.Activate(eventConsumer);
            sut.Stop();
            sut.Stop();

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = true, Position = initialPosition, Error = null });

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventSubscription.StopAsync())
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public void Should_reset_consumer_when_resetting()
        {
            sut.Activate(eventConsumer);
            sut.Stop();
            sut.Reset();
            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = null, Error = null });

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Twice);

            A.CallTo(() => eventConsumer.ClearAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventSubscription.StopAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, state.Position))
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, null))
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public async Task Should_invoke_and_update_position_when_event_received()
        {
            sut.Activate(eventConsumer);

            var @event = new StoredEvent(Guid.NewGuid().ToString(), 123, eventData);

            await OnEventAsync(eventSubscription, @event);

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = @event.EventPosition, Error = null });

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventConsumer.On(envelope))
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public async Task Should_ignore_old_events()
        {
            sut.Activate(eventConsumer);

            A.CallTo(() => formatter.Parse(eventData, true))
                .Throws(new TypeNameNotFoundException());

            var @event = new StoredEvent(Guid.NewGuid().ToString(), 123, eventData);

            await OnEventAsync(eventSubscription, @event);

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = @event.EventPosition, Error = null });

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventConsumer.On(envelope))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task Should_not_invoke_and_update_position_when_event_is_from_another_subscription()
        {
            sut.Activate(eventConsumer);

            var @event = new StoredEvent(Guid.NewGuid().ToString(), 123, eventData);

            await OnEventAsync(A.Fake<IEventSubscription>(), @event);

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = initialPosition, Error = null });

            A.CallTo(() => eventConsumer.On(envelope))
                .MustNotHaveHappened();
        }

        [Fact]
        public async Task Should_not_make_error_handling_when_exception_is_from_another_subscription()
        {
            sut.Activate(eventConsumer);

            var ex = new InvalidOperationException();

            await OnErrorAsync(A.Fake<IEventSubscription>(), ex);

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = initialPosition, Error = null });

            A.CallTo(() => stateHolder.WriteAsync())
                .MustNotHaveHappened();
        }

        [Fact]
        public void Should_stop_if_resetting_failed()
        {
            sut.Activate(eventConsumer);

            var ex = new InvalidOperationException();

            A.CallTo(() => eventConsumer.ClearAsync())
                .Throws(ex);

            sut.Reset();
            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = true, Position = initialPosition, Error = ex.ToString() });

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventSubscription.StopAsync())
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public async Task Should_stop_if_handling_failed()
        {
            sut.Activate(eventConsumer);

            var ex = new InvalidOperationException();

            A.CallTo(() => eventConsumer.On(envelope))
                .Throws(ex);

            var @event = new StoredEvent(Guid.NewGuid().ToString(), 123, eventData);

            await OnEventAsync(eventSubscription, @event);

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = true, Position = initialPosition, Error = ex.ToString() });

            A.CallTo(() => eventConsumer.On(envelope))
                .MustHaveHappened();

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventSubscription.StopAsync())
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public async Task Should_stop_if_deserialization_failed()
        {
            sut.Activate(eventConsumer);

            var ex = new InvalidOperationException();

            A.CallTo(() => formatter.Parse(eventData, true))
                .Throws(ex);

            var @event = new StoredEvent(Guid.NewGuid().ToString(), 123, eventData);

            await OnEventAsync(eventSubscription, @event);

            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = true, Position = initialPosition, Error = ex.ToString() });

            A.CallTo(() => eventConsumer.On(envelope))
                .MustNotHaveHappened();

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventSubscription.StopAsync())
                .MustHaveHappened(Repeated.Exactly.Once);
        }

        [Fact]
        public async Task Should_start_after_stop_when_handling_failed()
        {
            sut.Activate(eventConsumer);

            var exception = new InvalidOperationException();

            A.CallTo(() => eventConsumer.On(envelope))
                .Throws(exception);

            var @event = new StoredEvent(Guid.NewGuid().ToString(), 123, eventData);

            await OnEventAsync(eventSubscription, @event);

            sut.Start();
            sut.Start();
            sut.Dispose();

            state.ShouldBeEquivalentTo(new EventConsumerState { IsStopped = false, Position = initialPosition, Error = null });

            A.CallTo(() => eventConsumer.On(envelope))
                .MustHaveHappened();

            A.CallTo(() => stateHolder.WriteAsync())
                .MustHaveHappened(Repeated.Exactly.Twice);

            A.CallTo(() => eventSubscription.StopAsync())
                .MustHaveHappened(Repeated.Exactly.Once);

            A.CallTo(() => eventStore.CreateSubscription(A<IEventSubscriber>.Ignored, A<string>.Ignored, A<string>.Ignored))
                .MustHaveHappened(Repeated.Exactly.Twice);
        }

        private Task OnErrorAsync(IEventSubscription subscriber, Exception ex)
        {
            return sutSubscriber.OnErrorAsync(subscriber, ex);
        }

        private Task OnEventAsync(IEventSubscription subscriber, StoredEvent ev)
        {
            return sutSubscriber.OnEventAsync(subscriber, ev);
        }
    }
}