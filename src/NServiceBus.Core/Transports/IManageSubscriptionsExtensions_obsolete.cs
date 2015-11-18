﻿namespace NServiceBus.Transports
{
    using System;
    using NServiceBus.Extensibility;

    /// <summary>
    /// Syntactic sugar <see cref="IManageSubscriptions"/>.
    /// </summary>
    public static class IManageSubscriptionsExtensions_obsolete
    {
        /// <summary>
        /// Subscribes to the given event.
        /// </summary>
        /// <param name="manage">The manage subscriptions.</param>
        /// <param name="eventType">The event type.</param>
        /// <param name="context">The current context.</param>
        [ObsoleteEx(
            TreatAsErrorFromVersion = "6",
            RemoveInVersion = "7",
            ReplacementTypeOrMember = "Subscribe(Type eventType, ContextBag context)")]
        public static void Subscribe(this IManageSubscriptions manage, Type eventType, ContextBag context)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Unsubscribes from the given event.
        /// </summary>
        /// <param name="manage">The manage subscriptions.</param>
        /// <param name="eventType">The event type.</param>
        /// <param name="context">The current context.</param>
        [ObsoleteEx(
            TreatAsErrorFromVersion = "6",
            RemoveInVersion = "7",
            ReplacementTypeOrMember = "Unsubscribe(Type eventType, ContextBag context)")]
        public static void Unsubscribe(IManageSubscriptions manage, Type eventType, ContextBag context)
        {
            throw new NotImplementedException();
        }
    }
}