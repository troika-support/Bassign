namespace Bassign.Application.Common.Persistence;

/// <summary>Another user (or device) changed the same record between our read and our write.</summary>
public sealed class ConcurrencyConflictException(string message, Exception? inner = null) : Exception(message, inner);
