using ChattingAIs.Common;
using OpenAI.Audio;
using OpenAI.Chat;
using System.Text.Json;

namespace ChattingAIs.Agent;

/// <summary>
/// Represents something that can act as a participant
/// in a conversation.
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Unique ID for the agent
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Whether the agent is a moderator for the
    /// conversation
    /// </summary>
    bool IsModerator { get; }

    /// <summary>
    /// A summary of the agent that other participants
    /// can see
    /// </summary>
    string Profile { get; }

    /// <summary>
    /// Participate in a conversation based on its current state
    /// </summary>
    /// <param name="request">The current state of the conversation</param>
    /// <param name="token">A token to cancel execution of this method</param>
    /// <returns>A task resolving to the agent's response</returns>
    Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken token = default);
}

/// <summary>
/// Represents an <see cref="IAgent"/> that is capable of creating audio.
/// </summary>
public interface IAudioAgent : IAgent
{
    /// <summary>
    /// Generate audio for the given response message.
    /// </summary>
    /// <param name="message">A response message from <see cref="IAgent.ChatAsync(AgentRequest, CancellationToken)"/></param>
    /// <param name="token">A token to cancel execution of this method</param>
    /// <returns>An audio stream corresponding to the given message.</returns>
    Task<Stream> SpeakAsync(AgentResponseMessage message, CancellationToken token = default);
}

/// <summary>
/// Represents the application user
/// </summary>
/// <param name="id">Agent's unique identifier</param>
public class UserAgent(string id) : IAgent
{
    public string Id { get; set; } = id;

    public bool IsModerator { get; set; }

    /// <summary>
    /// Whether <see cref="ChatAsync(AgentRequest, CancellationToken)"/> is
    /// currently executing for this agent
    /// </summary>
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

/// <summary>
/// Represents an agent that uses OpenAI APIs in order to
/// generate text and audio.
/// </summary>
/// <param name="id">Agent's unique identifier</param>
public class OpenAIAgent(string id) : IAgent, IAudioAgent
{
    public string Id { get; set; } = id;

    /// <summary>
    /// The identifier of the OpenAI voice to use when generating speech
    /// </summary>
    public string VoiceId { get; set; } = string.Empty;

    public bool IsModerator { get; set; }

    /// <summary>
    /// The system prompt to include to OpenAI in
    /// <see cref="ChatAsync(AgentRequest, CancellationToken)"/>
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    /// <summary>
    /// OpenAI client for generating text content
    /// </summary>
    public ChatClient? ChatClient { get; set; }

    /// <summary>
    /// OpenAI client for generating audio content
    /// </summary>
    public AudioClient? SpeechClient { get; set; }

    /// <summary>
    /// Settings to modify text generation
    /// </summary>
    public ChatCompletionOptions? ChatOptions { get; set; }

    /// <summary>
    /// Settings to modify audio generation
    /// </summary>
    public SpeechGenerationOptions? SpeechOptions { get; set; }

    public async Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken token = default)
    {
        //Make sure OpenAI client is present
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

        //Send context to client
        ChatCompletion completion;

        try
        {
            completion = (ChatCompletion)await ChatClient.CompleteChatAsync(history, ChatOptions, token);
        }
        catch(Exception e) when(e is not OperationCanceledException and not TaskCanceledException)
        {
            throw new ChatException($"Failed to get response from OpenAI. {e.Message}", e);
        }

        //Handle the response
        if(completion.Content.Count > 0)
        {
            var content = completion.Content[0];

            switch(content.Kind)
            {
                //Deserialize the structured response text from JSON
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
                //Handle refusal from OpenAI
                case ChatMessageContentPartKind.Refusal:
                {
                    throw new ChatException($"OpenAI refused to respond to the prompt. Reason: {content.Refusal}");
                }
                //Handle unexpected message type
                default:
                {
                    throw new ChatException($"Unexpected chat response of kind {content.Kind}.");
                }
            }
        }
        //Empty message
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
        if(string.IsNullOrWhiteSpace(message.Message))
            return Stream.Null;

        //Make sure OpenAI client is present
        if(SpeechClient == null)
            throw new ChatException($"OpenAIAgent isn't associated with an OpenAI AudioClient.");

        token.ThrowIfCancellationRequested();

        //Initialize voice from VoideId
        var voice = new GeneratedSpeechVoice(Utility.CoalesceString(VoiceId, Constants.OpenAI.VOICE_DFT));

        //Send message to speech client, and get the resulting binary stream
        var speech_response = await SpeechClient.GenerateSpeechAsync(message.Message, voice, SpeechOptions?? new(), token);

        return speech_response.Value.ToStream();
    }
}