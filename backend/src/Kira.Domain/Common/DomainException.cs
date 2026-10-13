namespace Kira.Domain.Common;

/// <summary>A business rule was broken. Shown to the user as a 400 with this message.</summary>
public class DomainException(string message) : Exception(message);
