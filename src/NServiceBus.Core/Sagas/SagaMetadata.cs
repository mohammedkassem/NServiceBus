namespace NServiceBus.Sagas
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using System.Runtime.Serialization;

    /// <summary>
    /// Contains metadata for known sagas.
    /// </summary>
    public class SagaMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SagaMetadata" /> class.
        /// </summary>
        /// <param name="name">The name of the saga.</param>
        /// <param name="sagaType">The type for this saga.</param>
        /// <param name="entityName">The name of the saga data entity.</param>
        /// <param name="sagaEntityType">The type of the related saga entity.</param>
        /// <param name="correlationProperty">The property this saga is correlated on if any.</param>
        /// <param name="messages">The messages collection that a saga handles.</param>
        /// <param name="finders">The finder definition collection that can find this saga.</param>
        public SagaMetadata(string name, Type sagaType, string entityName, Type sagaEntityType, CorrelationPropertyMetadata correlationProperty, IReadOnlyCollection<SagaMessage> messages, IReadOnlyCollection<SagaFinderDefinition> finders)
        {
            this.correlationProperty = correlationProperty;
            Name = name;
            EntityName = entityName;
            SagaEntityType = sagaEntityType;
            SagaType = sagaType;


            if (!messages.Any(m => m.IsAllowedToStartSaga))
            {
                throw new Exception($@"
Sagas must have at least one message that is allowed to start the saga. Please add at least one `IAmStartedByMessages` to your {name} saga.");
            }

            if (correlationProperty != null)
            {
                if (!AllowedCorrelationPropertyTypes.Contains(correlationProperty.Type))
                {
                    var supportedTypes = string.Join(",", AllowedCorrelationPropertyTypes.Select(t => t.Name));

                    throw new Exception($@"
{correlationProperty.Type.Name} is not supported for correlated properties. Please change the correlation property {correlationProperty.Name} on saga {name} to any of the supported types, {supportedTypes}, or use a custom saga finder.");
                }
            }

            associatedMessages = new Dictionary<string, SagaMessage>();

            foreach (var sagaMessage in messages)
            {
                associatedMessages[sagaMessage.MessageType] = sagaMessage;
            }

            sagaFinders = new Dictionary<string, SagaFinderDefinition>();

            foreach (var finder in finders)
            {
                sagaFinders[finder.MessageType] = finder;
            }
        }

        /// <summary>
        /// Returns the list of messages that is associated with this saga.
        /// </summary>
        public IReadOnlyCollection<SagaMessage> AssociatedMessages => associatedMessages.Values.ToList();

        /// <summary>
        /// Gets the list of finders for this saga.
        /// </summary>
        public IReadOnlyCollection<SagaFinderDefinition> Finders => sagaFinders.Values.ToList();

        /// <summary>
        /// The name of the saga.
        /// </summary>
        public string Name { get; private set; }

        /// <summary>
        /// The name of the saga data entity.
        /// </summary>
        public string EntityName { get; private set; }

        /// <summary>
        /// The type of the related saga entity.
        /// </summary>
        public Type SagaEntityType { get; private set; }

        /// <summary>
        /// The type for this saga.
        /// </summary>
        public Type SagaType { get; private set; }

        /// <summary>
        /// Property this saga is correlated on.
        /// </summary>
        public bool TryGetCorrelationProperty(out CorrelationPropertyMetadata property)
        {
            property = correlationProperty;

            return property != null;
        }

        internal static bool IsSagaType(Type t)
        {
            return typeof(Saga).IsAssignableFrom(t) && t != typeof(Saga) && !t.IsGenericType;
        }

        /// <summary>
        /// True if the specified message type is allowed to start the saga.
        /// </summary>
        public bool IsMessageAllowedToStartTheSaga(string messageType)
        {
            SagaMessage sagaMessage;

            if (!associatedMessages.TryGetValue(messageType, out sagaMessage))
            {
                return false;
            }
            return sagaMessage.IsAllowedToStartSaga;
        }

        /// <summary>
        /// Gets the configured finder for this message.
        /// </summary>
        /// <param name="messageType">The message <see cref="MemberInfo.Name" />.</param>
        /// <param name="finderDefinition">The finder if present.</param>
        /// <returns>True if finder exists.</returns>
        public bool TryGetFinder(string messageType, out SagaFinderDefinition finderDefinition)
        {
            return sagaFinders.TryGetValue(messageType, out finderDefinition);
        }

        /// <summary>
        /// Creates a <see cref="SagaMetadata" /> from a specific Saga type.
        /// </summary>
        /// <param name="sagaType">A type representing a Saga. Must be a non-generic type inheriting from <see cref="Saga" />.</param>
        /// <returns>An instance of <see cref="SagaMetadata" /> describing the Saga.</returns>
        public static SagaMetadata Create(Type sagaType)
        {
            return Create(sagaType, new List<Type>(), new Conventions());
        }

        /// <summary>
        /// Creates a <see cref="SagaMetadata" /> from a specific Saga type.
        /// </summary>
        /// <param name="sagaType">A type representing a Saga. Must be a non-generic type inheriting from <see cref="Saga" />.</param>
        /// <param name="availableTypes">Additional available types, used to locate saga finders and other related classes.</param>
        /// <param name="conventions">Custom conventions to use while scanning types.</param>
        /// <returns>An instance of <see cref="SagaMetadata" /> describing the Saga.</returns>
        public static SagaMetadata Create(Type sagaType, IEnumerable<Type> availableTypes, Conventions conventions)
        {
            if (!IsSagaType(sagaType))
            {
                throw new Exception(sagaType.FullName + " is not a saga");
            }

            var genericArguments = GetBaseSagaType(sagaType).GetGenericArguments();
            if (genericArguments.Length != 1)
            {
                throw new Exception($"'{sagaType.Name}' saga type does not implement Saga<T>");
            }

            var saga = (Saga) FormatterServices.GetUninitializedObject(sagaType);
            var mapper = new SagaMapper();
            saga.ConfigureHowToFindSaga(mapper);

            var sagaEntityType = genericArguments.Single();

            ApplyScannedFinders(mapper, sagaEntityType, availableTypes, conventions);

            var finders = new List<SagaFinderDefinition>();


            var propertyMappings = mapper.Mappings.Where(m => !m.HasCustomFinderMap)
                .GroupBy(m=>m.SagaPropName)
                .ToList();

            if (propertyMappings.Count > 1)
            {
                var messageTypes = string.Join(",", propertyMappings.SelectMany(g => g.Select(m=>m.MessageType.FullName)));
                throw new Exception($@"
Sagas can only have mappings that correlate on a single saga property. Please use custom finders to correlate {messageTypes} to saga {sagaType.Name}");
            }

            CorrelationPropertyMetadata correlationProperty = null;

            if (propertyMappings.Any())
            {
                var mapping = propertyMappings.Single().First();
                correlationProperty = new CorrelationPropertyMetadata(mapping.SagaPropName, mapping.SagaPropType);
            }

            foreach (var mapping in mapper.Mappings)
            {
                SetFinderForMessage(mapping, sagaEntityType, finders);
            }

            var associatedMessages = GetAssociatedMessages(sagaType)
                .ToList();

            return new SagaMetadata(sagaType.FullName, sagaType, sagaEntityType.FullName, sagaEntityType, correlationProperty, associatedMessages, finders);
        }

        static void ApplyScannedFinders(SagaMapper mapper, Type sagaEntityType, IEnumerable<Type> availableTypes, Conventions conventions)
        {
            var actualFinders = availableTypes.Where(t => typeof(IFinder).IsAssignableFrom(t) && t.IsClass)
                .ToList();

            foreach (var finderType in actualFinders)
            {
                foreach (var interfaceType in finderType.GetInterfaces())
                {
                    var args = interfaceType.GetGenericArguments();
                    //since we dont want to process the IFinder type
                    if (args.Length != 2)
                    {
                        continue;
                    }

                    var entityType = args[0];
                    if (entityType != sagaEntityType)
                    {
                        continue;
                    }

                    var messageType = args[1];
                    if (!conventions.IsMessageType(messageType))
                    {
                        var error = $"A custom IFindSagas must target a valid message type as defined by the message conventions. Please change '{messageType.FullName}' to a valid message type or add it to the message conventions. Finder name '{finderType.FullName}'.";
                        throw new Exception(error);
                    }

                    var existingMapping = mapper.Mappings.SingleOrDefault(m => m.MessageType == messageType);
                    if (existingMapping != null)
                    {
                        var bothMappingAndFinder = $"A custom IFindSagas and an existing mapping where found for message '{messageType.FullName}'. Please either remove the message mapping for remove the finder. Finder name '{finderType.FullName}'.";
                        throw new Exception(bothMappingAndFinder);
                    }
                    mapper.ConfigureCustomFinder(finderType, messageType);
                }
            }
        }

        static void SetFinderForMessage(SagaToMessageMap mapping, Type sagaEntityType, List<SagaFinderDefinition> finders)
        {
            if (mapping.HasCustomFinderMap)
            {
                finders.Add(new SagaFinderDefinition(typeof(CustomFinderAdapter<,>).MakeGenericType(sagaEntityType, mapping.MessageType), mapping.MessageType.FullName, new Dictionary<string, object>
                {
                    {"custom-finder-clr-type", mapping.CustomFinderType}
                }));
            }
            else
            {
                finders.Add(new SagaFinderDefinition(typeof(PropertySagaFinder<>).MakeGenericType(sagaEntityType), mapping.MessageType.FullName, new Dictionary<string, object>
                {
                    {"property-accessor", mapping.MessageProp},
                    {"saga-property-name", mapping.SagaPropName}
                }));
            }
        }

        static IEnumerable<SagaMessage> GetAssociatedMessages(Type sagaType)
        {
            var result = GetMessagesCorrespondingToFilterOnSaga(sagaType, typeof(IAmStartedByMessages<>))
                .Select(t => new SagaMessage(t.FullName, true)).ToList();

            foreach (var messageType in GetMessagesCorrespondingToFilterOnSaga(sagaType, typeof(IHandleMessages<>)))
            {
                if (result.Any(m => m.MessageType == messageType.FullName))
                {
                    continue;
                }
                result.Add(new SagaMessage(messageType.FullName, false));
            }

            foreach (var messageType in GetMessagesCorrespondingToFilterOnSaga(sagaType, typeof(IHandleTimeouts<>)))
            {
                if (result.Any(m => m.MessageType == messageType.FullName))
                {
                    continue;
                }
                result.Add(new SagaMessage(messageType.FullName, false));
            }

            return result;
        }

        static IEnumerable<Type> GetMessagesCorrespondingToFilterOnSaga(Type sagaType, Type filter)
        {
            foreach (var interfaceType in sagaType.GetInterfaces())
            {
                foreach (var argument in interfaceType.GetGenericArguments())
                {
                    var genericType = filter.MakeGenericType(argument);
                    var isOfFilterType = genericType == interfaceType;
                    if (!isOfFilterType)
                    {
                        continue;
                    }
                    yield return argument;
                }
            }
        }

        static Type GetBaseSagaType(Type t)
        {
            var currentType = t.BaseType;
            var previousType = t;

            while (currentType != null)
            {
                if (currentType == typeof(Saga))
                {
                    return previousType;
                }

                previousType = currentType;
                currentType = currentType.BaseType;
            }

            throw new InvalidOperationException();
        }

        Dictionary<string, SagaMessage> associatedMessages;
        CorrelationPropertyMetadata correlationProperty;
        Dictionary<string, SagaFinderDefinition> sagaFinders;

        static Type[] AllowedCorrelationPropertyTypes =
        {
            typeof(Guid),
            typeof(string),
            typeof(long),
            typeof(ulong),
            typeof(int),
            typeof(uint),
            typeof(short),
            typeof(ushort)
        };

        class SagaMapper : IConfigureHowToFindSagaWithMessage
        {
            void IConfigureHowToFindSagaWithMessage.ConfigureMapping<TSagaEntity, TMessage>(Expression<Func<TSagaEntity, object>> sagaEntityProperty, Expression<Func<TMessage, object>> messageExpression)
            {
                var sagaProp = Reflect<TSagaEntity>.GetProperty(sagaEntityProperty, true);

                ValidateMapping(messageExpression, sagaProp);

                ThrowIfNotPropertyLambdaExpression(sagaEntityProperty, sagaProp);
                var compiledMessageExpression = messageExpression.Compile();
                var messageFunc = new Func<object, object>(o => compiledMessageExpression((TMessage) o));

                Mappings.Add(new SagaToMessageMap
                {
                    MessageProp = messageFunc,
                    SagaPropName = sagaProp.Name,
                    SagaPropType = sagaProp.PropertyType,
                    MessageType = typeof(TMessage)
                });
            }

            static void ValidateMapping<TMessage>(Expression<Func<TMessage, object>> messageExpression, PropertyInfo sagaProp)
            {
                if (sagaProp.Name.ToLower() != "id")
                {
                    return;
                }

                if (messageExpression.Body.NodeType != ExpressionType.Convert)
                {
                    return;
                }

                var memberExpr = ((UnaryExpression) messageExpression.Body).Operand as MemberExpression;

                if (memberExpr == null)
                {
                    return;
                }

                var propertyInfo = memberExpr.Member as PropertyInfo;

                const string message = "Message properties mapped to the saga id needs to be of type Guid, please change property {0} on message {1} to a Guid";

                if (propertyInfo != null)
                {
                    if (propertyInfo.PropertyType != typeof(Guid))
                    {
                        throw new Exception(string.Format(message, propertyInfo.Name, typeof(TMessage).Name));
                    }

                    return;
                }


                var fieldInfo = memberExpr.Member as FieldInfo;

                if (fieldInfo != null)
                {
                    if (fieldInfo.FieldType != typeof(Guid))
                    {
                        throw new Exception(string.Format(message, fieldInfo.Name, typeof(TMessage).Name));
                    }
                }
            }

            public void ConfigureCustomFinder(Type finderType, Type messageType)
            {
                Mappings.Add(new SagaToMessageMap
                {
                    MessageType = messageType,
                    CustomFinderType = finderType
                });
            }

            // ReSharper disable once UnusedParameter.Local
            void ThrowIfNotPropertyLambdaExpression<TSagaEntity>(Expression<Func<TSagaEntity, object>> expression, PropertyInfo propertyInfo)
            {
                if (propertyInfo == null)
                {
                    throw new ArgumentException(
                        $@"Only public properties are supported for mapping Sagas. The lambda expression provided '{expression.Body}' is not mapping to a Property.");
                }
            }

            public List<SagaToMessageMap> Mappings = new List<SagaToMessageMap>();
        }

        /// <summary>
        /// Details about a saga data property used to correlate messages hitting the saga.
        /// </summary>
        public class CorrelationPropertyMetadata
        {
            /// <summary>
            /// Creates a new instance of <see cref="CorrelationPropertyMetadata" />.
            /// </summary>
            /// <param name="name">The name of the correlation property.</param>
            /// <param name="type">The type of the correlation property.</param>
            public CorrelationPropertyMetadata(string name, Type type)
            {
                Name = name;
                Type = type;
            }

            /// <summary>
            /// The name of the correlation property.
            /// </summary>
            public string Name { get; private set; }

            /// <summary>
            /// The type of the correlation property.
            /// </summary>
            public Type Type { get; private set; }
        }
    }
}