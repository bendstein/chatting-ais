using ChattingAIs.Common;
using OpenAI.Audio;
using OpenAI.Chat;
using System.Text.Json;

namespace ChattingAIs.Agent;

public interface IAgent
{
    string Id { get; set; }

    bool IsModerator { get; set; }

    string Profile { get; }

    Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken token = default);
}

public interface IAudioAgent : IAgent
{
    Task<Stream> SpeakAsync(AgentResponseMessage message, CancellationToken token = default);
}

public class UserAgent(string id) : IAgent
{
    public string Id { get; set; } = id;

    public bool IsModerator { get; set; }

    public bool IsActive { get; private set; } = false;

    public string Profile => string.Empty;

    public async Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken token = default)
    {
        IsActive = true;

        try
        {
            //Prompt user for their message
            Console.Write($"{Id}, please enter your message: ");

            var message = Console.ReadLine()?.Trim();

            token.ThrowIfCancellationRequested();

            string? target = null;

            if(string.IsNullOrWhiteSpace(message))
            { }
            //Prompt user for message target if they provided a message
            else
            {
                List<string> agents = [.. request.Agents.Keys.Where(k => k != Id)];
                int selection = -1;

                do
                {
                    token.ThrowIfCancellationRequested();

                    Console.WriteLine($"Who would you like to address?\r\nAgents:");

                    for(int i = 0; i < agents.Count; i++)
                        Console.WriteLine($"    {i}) {agents[i]}");

                    Console.WriteLine($"    {agents.Count}) Anyone");
                    Console.Write($"Choice: ");

                    string choice = Console.ReadLine() ?? string.Empty;

                    token.ThrowIfCancellationRequested();

                    if(int.TryParse(choice, out selection) && selection >= 0
                        && selection <= agents.Count)
                    { }
                    else
                    {
                        //Short delay to give cancellation token time to cancel
                        await Task.Delay(25, token);

                        selection = -1;

                        token.ThrowIfCancellationRequested();

                        Console.Error.WriteLine($"Invalid selection '{choice}'.\r\n");
                    }
                }
                while(selection < 0 || selection > agents.Count);

                target = selection == agents.Count
                    ? string.Empty
                    : agents[selection];
            }

            return new AgentResponse(this)
            {
                Message = new()
                {
                    Sender = Id,
                    Message = message ?? string.Empty,
                    Target = target ?? string.Empty
                }
            };
        }
        finally
        {
            IsActive = false;
        }
    }
}

public class OpenAIAgent(string id) : IAgent, IAudioAgent
{
    public string Id { get; set; } = id;

    public string VoiceId { get; set; } = string.Empty;

    public bool IsModerator { get; set; }

    public string SystemPrompt { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    public ChatClient? ChatClient { get; set; }

    public AudioClient? SpeechClient { get; set; }

    public ChatCompletionOptions? ChatOptions { get; set; }

    public SpeechGenerationOptions? SpeechOptions { get; set; }

    public async Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken token = default)
    {
        if(ChatClient == null)
            throw new ChatException($"OpenAIAgent isn't associated with an OpenAI ChatClient.");

        token.ThrowIfCancellationRequested();

        List<ChatMessage> history = [];

        //Add system prompt
        if(!string.IsNullOrWhiteSpace(SystemPrompt))
            history.Add(new SystemChatMessage(SystemPrompt));

        //Add history
        foreach(var message in request.History)
        {
            //Don't include messages not for this user
            if(!IsModerator && message.Whisper.Length > 0)
            {
                if(message.Sender == Id || message.Target == Id || message.Whisper.Contains(Id)) { }
                else
                {
                    continue;
                }
            }

            history.Add(new UserChatMessage(JsonSerializer.Serialize(message)));
        }

        ChatCompletion completion;

        try
        {
            completion = (ChatCompletion)await ChatClient.CompleteChatAsync(history, ChatOptions, token);
        }
        catch(Exception e) when(e is not OperationCanceledException and not TaskCanceledException)
        {
            throw new ChatException($"Failed to get response from OpenAI. {e.Message}", e);
        }

        if(completion.Content.Count > 0)
        {
            var content = completion.Content[0];

            switch(content.Kind)
            {
                case ChatMessageContentPartKind.Text:
                {
                    try
                    {
                        var deserialized = JsonSerializer.Deserialize<AgentResponseMessage>(content.Text)
                            ?? throw new ChatException($"Failed to deserialize message.");

                        return new AgentResponse(this)
                        {
                            Message = deserialized
                        };
                    }
                    catch(Exception e) when (e is not ChatException)
                    {
                        throw new ChatException($"Failed to deserialize message. {e.Message}", e);
                    }
                }
                case ChatMessageContentPartKind.Refusal:
                {
                    throw new ChatException($"OpenAI refused to respond to the prompt. Reason: {content.Refusal}");
                }
                default:
                {
                    throw new ChatException($"Unexpected chat response of kind {content.Kind}.");
                }
            }
        }
        else
        {
            return new AgentResponse(this)
            {
                Message = new()
                {
                    Sender = Id
                }
            };
        }
    }

    public async Task<Stream> SpeakAsync(AgentResponseMessage message, CancellationToken token = default)
    {
        if(SpeechClient == null)
            throw new ChatException($"OpenAIAgent isn't associated with an OpenAI AudioClient.");

        token.ThrowIfCancellationRequested();

        var voice = new GeneratedSpeechVoice(Utility.CoalesceString(VoiceId, Constants.OpenAI.VOICE_DFT));

        var speech_response = await SpeechClient.GenerateSpeechAsync(message.Message, voice, SpeechOptions?? new(), token);

        return speech_response.Value.ToStream();
    }
}