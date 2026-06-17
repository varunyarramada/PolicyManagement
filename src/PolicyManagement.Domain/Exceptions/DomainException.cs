namespace PolicyManagement.Domain.Exceptions;

/// <summary>
/// Base class for all domain exceptions in the PolicyManagement domain.
/// All domain-specific exceptions must inherit from this class.
/// </summary>
public abstract class DomainException : Exception
{
    /// <summary>
    /// Initialises a new instance of <see cref="DomainException"/> with the specified message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    protected DomainException(string message) : base(message) { }

    /// <summary>
    /// Initialises a new instance of <see cref="DomainException"/> with the specified message
    /// and inner exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this exception.</param>
    protected DomainException(string message, Exception innerException)
        : base(message, innerException) { }
}
