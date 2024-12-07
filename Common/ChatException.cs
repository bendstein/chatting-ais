namespace ChattingAIs.Common;

public class ChatException(string? message = null, Exception? inner = null) : Exception(message, inner);