namespace NServiceBus.Routing
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using NServiceBus.Extensibility;

    /// <summary>
    /// Manages the unicast routing table.
    /// </summary>
    public class UnicastRoutingTable
    {
        List<Func<Type, ContextBag, IUnicastRoute>> staticRules = new List<Func<Type, ContextBag, IUnicastRoute>>();
        List<Func<List<Type>, ContextBag, Task<IReadOnlyCollection<IUnicastRoute>>>> dynamicRules = new List<Func<List<Type>, ContextBag, Task<IReadOnlyCollection<IUnicastRoute>>>>();

        internal async Task<IReadOnlyCollection<IUnicastRoute>> GetDestinationsFor(List<Type> messageTypes, ContextBag contextBag)
        {
            var dynamicRoutes = new List<IUnicastRoute>();
            foreach (var rule in dynamicRules)
            {
                dynamicRoutes.AddRange(await rule.Invoke(messageTypes, contextBag).ConfigureAwait(false));
            }

            var staticRoutes = messageTypes
                .SelectMany(type => staticRules, (type, rule) => rule.Invoke(type, contextBag))
                .Where(route => route != null);
            
            return dynamicRoutes.Concat(staticRoutes).Distinct().ToList();
        }

        /// <summary>
        /// Adds a static unicast route.
        /// </summary>
        /// <param name="messageType">Message type.</param>
        /// <param name="destination">Destination endpoint.</param>
        public void AddStatic(Type messageType, EndpointName destination)
        {
            staticRules.Add((t, c) => StaticRule(t, messageType, new UnicastRoute(destination)));
        }


        /// <summary>
        /// Adds a static unicast route.
        /// </summary>
        /// <param name="messageType">Message type.</param>
        /// <param name="destination">Destination endpoint instance.</param>
        public void AddStatic(Type messageType, EndpointInstanceName destination)
        {
            staticRules.Add((t, c) => StaticRule(t, messageType, new UnicastRoute(destination)));
        }


        /// <summary>
        /// Adds a static unicast route.
        /// </summary>
        /// <param name="messageType">Message type.</param>
        /// <param name="destinationAddress">Destination endpoint instance address.</param>
        public void AddStatic(Type messageType, string destinationAddress)
        {
            staticRules.Add((t, c) => StaticRule(t, messageType, new UnicastRoute(destinationAddress)));
        }

        /// <summary>
        /// Adds a rule for generating unicast routes.
        /// </summary>
       // /// <param name="dynamicRule">The rule.</param>
        public void AddDynamic(Func<List<Type>, ContextBag, Task<IReadOnlyCollection<IUnicastRoute>>> dynamicRule)
        {
            dynamicRules.Add(dynamicRule);
        }

        static IUnicastRoute StaticRule(Type messageBeingRouted, Type configuredMessage, UnicastRoute configuredDestination)
        {
            return messageBeingRouted == configuredMessage ? configuredDestination : null;
        }
    }
}