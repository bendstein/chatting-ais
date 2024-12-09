using ChattingAIs.Common;
using NAudio.Wave;
using OpenAI.Audio;
using OpenAI.Chat;
using System.Numerics;
using System.Text;
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

    public string Profile => string.Empty;

    /// <summary>
    /// Whether <see cref="ChatAsync(AgentRequest, CancellationToken)"/> is
    /// currently executing for this agent
    /// </summary>
    public bool IsActive { get; private set; } = false;

    /// <summary>
    /// Audio input device index
    /// </summary>
    public int LineIn { get; set; } = -2;

    /// <summary>
    /// OpenAI client of transcribing audio
    /// </summary>
    public AudioClient? TranscribeClient { get; set; }

    public async Task<AgentResponse> ChatAsync(AgentRequest request, CancellationToken token = default)
    {
        IsActive = true;

        try
        {
            //If any audio in devices, check if user would like to record audio
            bool record_message = false;

            if(LineIn >= -1)
            {
                do
                {
                    Console.Write($"{Id}, record audio for transcribed message? ([y]/[n]): ");

                    var record_message_choice = Console.ReadLine()?.Trim()?.ToUpper()?.FirstOrDefault();

                    switch(record_message_choice)
                    {
                        case 'Y':
                        {
                            record_message = true;
                        }
                        break;
                        case 'N':
                        {
                            record_message = false;
                        }
                        break;
                        default:
                        {
                            Console.Error.WriteLine($"Invalid selection.");
                        }
                        continue;
                    }
                } while(false);
            }

            string? message = null;

            if(record_message)
            {
                //Start recording
                message = await RecordAudioAsync(token);
                Console.WriteLine($"\r\n{message}");
            }
            else
            {
                //Prompt user for their message
                Console.Write($"Please enter your message: ");
                message = Console.ReadLine()?.Trim();
            }

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

    public async Task<string> RecordAudioAsync(CancellationToken token = default)
    {
        //Make sure OpenAI client is present
        if(TranscribeClient == null)
            throw new ChatException($"UserAgent isn't associated with an OpenAI AudioClient.");

        bool recording_started = false;
        bool recording_paused = true;
        bool recording_complete = false;

        //Write audio to memory stream
        WaveFormat wave_fmt = new(44100, 32, 2);

        await using var ms = new MemoryStream();
        using var sr = new WaveFileWriter(ms, wave_fmt);

        //Init audio device
        using var wave_in = new WaveInEvent()
        {
            DeviceNumber = LineIn,
            WaveFormat = wave_fmt
        };

        //New input audio input received
        wave_in.DataAvailable += (_, e) =>
        {
            sr.Write(e.Buffer);
        };

        //Get initial position
        var console_start = Console.GetCursorPosition();

        //Clear line and write message
        void Write(string message)
        {
            var console_position = Console.GetCursorPosition();

            //Pad message with spaces
            var padded = message.PadRight(console_position.Left - console_start.Left, ' ');

            //Overwrite message
            Console.SetCursorPosition(console_start.Left, console_start.Top);
            Console.Write(padded);
        }

        Write("Press [space] to start recording audio, or [esc] to cancel: ");

        //Control from console
        await Task.Run(async () =>
        {
            //Keep reading keys until done
            while(!recording_complete && !token.IsCancellationRequested)
            {
                if(Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);

                    //Toggle recording on space
                    if(key.Key == ConsoleKey.Spacebar)
                    {
                        //Restart recording
                        if(recording_paused)
                        {
                            wave_in.StartRecording();
                            await Task.Delay(TimeSpan.FromMilliseconds(wave_in.BufferMilliseconds));

                            Write("Recording audio... Press [space] to stop or [esc] to finish: ");

                            recording_started = true;
                            recording_paused = false;
                        }
                        //Stop recording
                        else
                        {
                            Write("Stopped recording. Press [space] to continue or [esc] to finish: ");

                            recording_paused = true;
                            wave_in.StopRecording();

                            //Give buffer time to finish
                            await Task.Delay(TimeSpan.FromMilliseconds(wave_in.BufferMilliseconds));
                        }
                    }
                    //Finish recording on escape
                    else if(key.Key == ConsoleKey.Escape)
                    {
                        //Stop recording
                        if(!recording_paused)
                        {
                            Write("Finished recording.");

                            recording_paused = true;
                            wave_in.StopRecording();

                            //Give buffer time to finish
                            await Task.Delay(TimeSpan.FromMilliseconds(wave_in.BufferMilliseconds));
                        }

                        //Complete recording
                        recording_complete = true;
                    }
                }

                await Task.Delay(25);
            }
        }, token);

        //If recording was never started, return empty string
        if(!recording_started)
            return string.Empty;

        await sr.FlushAsync(token);

        //Copy to new memory stream
        using var new_ms = new MemoryStream(ms.ToArray());

        //Transcribe audio
        var result = await TranscribeClient.TranscribeAudioAsync(new_ms, $"voice.wav", new()
        {
            
        }, token);

        string message = result.Value.Text;

        return message;
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
                if(message.Sender == Id || message.Target == Id || message.Whisper.Contains(Id))
                { }
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
                    catch(Exception e) when(e is not ChatException)
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
        var speech_response = await SpeechClient.GenerateSpeechAsync(message.Message, voice, SpeechOptions ?? new(), token);

        return speech_response.Value.ToStream();
    }
}