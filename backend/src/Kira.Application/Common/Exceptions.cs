namespace Kira.Application.Common;

public class NotFoundException(string message = "Not found.") : Exception(message);
public class ForbiddenException(string message = "You don't have access to this.") : Exception(message);
public class UnauthorizedException(string message = "Please sign in again.") : Exception(message);
/// <summary>The AI isn't configured or the AI service failed. Mapped to 503.</summary>
public class AiUnavailableException(string message) : Exception(message);
