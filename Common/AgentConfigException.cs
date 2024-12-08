namespace ChattingAIs.Common;

/// <summary>
/// Represents an error while configuring agents
/// </summary>
/// <param name="message">The error message</param>
/// <param name="inner">The exception which caused this exception</param>
public class AgentConfigException(string? message = null, Exception? inner = null) : Exception(message, inner);