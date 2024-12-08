namespace ChattingAIs.Common;

/// <summary>
/// Represents an error during a conversation
/// </summary>
/// <param name="message">The error message</param>
/// <param name="inner">The exception which caused this exception</param>
public class ChatException(string? message = null, Exception? inner = null) : Exception(message, inner);