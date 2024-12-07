using ChattingAIs.Common;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json.Serialization;

namespace ChattingAIs.Agent;

public class AgentRequest(ImmutableList<AgentResponseMessage> history, ReadOnlyDictionary<string, IAgent> agents)
{
    public ImmutableList<AgentResponseMessage> History { get; set; } = history;

    public ReadOnlyDictionary<string, IAgent> Agents { get; set; } = agents;
}

public class AgentResponse(IAgent agent)
{
    public IAgent Agent { get; set; } = agent;

    public AgentResponseMessage Message { get; set; } = new();
}

public class AgentResponseMessage
{
    [JsonPropertyName("sender")]
    public string Sender { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public string Target { get; set; } = string.Empty;

    [JsonPropertyName("whisper")]
    public string[] Whisper { get; set; } = [];

    public override string ToString()
    {
        StringBuilder sb = new();

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

        string msg = string.IsNullOrWhiteSpace(Message)
            ? Constants.NO_MESSAGE_CONTENT
            : Message;

        sb.Append($" {msg}");

        return sb.ToString().Trim();
    }
}