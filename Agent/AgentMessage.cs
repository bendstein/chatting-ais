using ChattingAIs.Common;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;

namespace ChattingAIs.Agent;

/// <summary>
/// Represents the context given to an agent when prompting for them for
/// text
/// </summary>
/// <param name="history">The current state of the conversation</param>
/// <param name="agents">All agents participating in the conversation</param>
public class AgentRequest(ImmutableList<AgentResponseMessage> history, ReadOnlyDictionary<string, IAgent> agents)
{
    /// <summary>
    /// The current state of the conversation
    /// </summary>
    public ImmutableList<AgentResponseMessage> History { get; set; } = history;

    /// <summary>
    /// All agents participating in the conversation
    /// </summary>
    public ReadOnlyDictionary<string, IAgent> Agents { get; set; } = agents;
}

/// <summary>
/// Represents the response returned from an agent when prompting them
/// for text
/// </summary>
/// <param name="agent">The agent returning the response</param>
public class AgentResponse(IAgent agent)
{
    /// <summary>
    /// The agent returning the response
    /// </summary>
    public IAgent Agent { get; set; } = agent;

    /// <summary>
    /// The content of the response
    /// </summary>
    public AgentResponseMessage Message { get; set; } = new();
}

/// <summary>
/// Represents a message from an agent
/// </summary>
public class AgentResponseMessage
{
    /// <summary>
    /// The id of the agent returning this response
    /// </summary>
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// The content of the message
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// The participant that this message is targeted
    /// to, or blank if not targeted at anyone in
    /// particular.
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    /// <summary>
    /// The ids of the participants explicitly allowed
    /// to see this message, excluding those not specified,
    /// or empty if all participants are allowed to see it.
    /// </summary>
    [JsonPropertyName("whisper")]
    public string[] Whisper { get; set; } = [];

    public override string ToString()
    {
        StringBuilder sb = new();

        //Message prefix specifies message sender and recipient(s)
        if(Whisper.Length > 0)
        {
            if(Whisper.Length == 1 && Whisper[0] == Sender)
            {
                sb.Append($"[<To Self> {Sender}]");
            }
            else
            {
                sb.Append($"[<Whisper> {Sender} -> {string.Join(", ", Whisper.Where(w => w != Sender))}]");
            }
        }
        else if(string.IsNullOrWhiteSpace(Target))
        {
            sb.Append($"[{Sender}]");
        }    
        else
        {
            sb.Append($"[{Sender} -> {Target}]");
        }

        //Append message content
        string msg = string.IsNullOrWhiteSpace(Message)
            ? Constants.NO_MESSAGE_CONTENT
            : Message;

        sb.Append($" {msg}");

        return sb.ToString().Trim();
    }
}