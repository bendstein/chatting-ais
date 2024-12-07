namespace ChattingAIs.Common;

public class AgentConfigException(string? message = null, Exception? inner = null) : Exception(message, inner);