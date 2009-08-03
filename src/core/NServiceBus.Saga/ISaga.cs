
namespace NServiceBus.Saga
{
    /// <summary>
    /// This interface is used by <see cref="Configure"/> to identify sagas.
    /// </summary>
    public interface ISaga : ITimeoutable, HasCompleted
    {
        /// <summary>
        /// The saga's data.
        /// </summary>
        ISagaEntity Entity { get; set; }

        /// <summary>
        /// Used for retrieving the endpoint which caused the saga to be initiated.
        /// </summary>
        IBus Bus { get; set; }

        /// <summary>
        /// Called by the infrastructure to give a chance for initialization time configuration.
        /// </summary>
        void Configure(IConfigureHowToFindSagaWithMessage configureHowToFindSagaWithMessage);
    }

    /// <summary>
    /// A more strongly typed version of ISaga meant to be implemented by application developers
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ISaga<T> : ISaga where T : ISagaEntity
    {
        /// <summary>
        /// The saga's data.
        /// </summary>
        T Data { get; set; }
    }
}
