using ChattingAIs;
using ChattingAIs.Agent;
using ChattingAIs.Common;
using NAudio.Wave;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Chat;
using System.ClientModel;

//Build configuration from appsettings, arguments, env variables, user secrets
var config = Utility.BuildConfiguration(args);

//Make it so that ctrl + c triggers cancellation token
CancellationTokenSource sigint_cancellation_source = new();

Console.CancelKeyPress += (sender, ev) =>
{
    //If cancellation already requested, exit
    if(sigint_cancellation_source.IsCancellationRequested)
    {
        Environment.Exit(0);
        return;
    }

    //Otherwise, don't cancel normally, trigger token
    ev.Cancel = true;
    sigint_cancellation_source.Cancel();
};

//Create agents from xml config
var agents = await Utility.ParseAgentConfigAsync("Content/AgentConfig.xml", sigint_cancellation_source.Token);

//Init OpenAI
OpenAIClient client = new(new ApiKeyCredential(config["OpenAI:Key"] ?? throw new ArgumentException("OpenAI API Key 'OpenAI:Key' is required.")),
    new());

ChatClient chat_client = client.GetChatClient(Utility.CoalesceString(config["OpenAI:ChatModel"], Constants.OpenAI.CHAT_MODEL));

//Specify json schema for structured output
ChatCompletionOptions chat_options = new()
{
    ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat("DiscussionResponse",
        new BinaryData(config["OpenAI:Chat:Schema"] ?? string.Empty), "Discussion", true)
};

AudioClient speech_client = client.GetAudioClient(Utility.CoalesceString(config["OpenAI:SpeechModel"], Constants.OpenAI.SPEECH_MODEL));

AudioClient transcribe_client = client.GetAudioClient(Utility.CoalesceString(config["OpenAI:TranscribeModel"], Constants.OpenAI.TRANSCRIBE_MODEL));

//Get audio devices
int line_in = int.TryParse(config["Args:Line-In"], out var li) ? li : -2;
int line_out = int.TryParse(config["Args:Line-Out"], out var lo) ? lo : -2;

if(line_in < -1 && WaveInEvent.DeviceCount > 0)
{
    Console.WriteLine($"Select audio input device:");
    for(int i = -1; i < WaveInEvent.DeviceCount; i++)
    {
        Console.WriteLine($" - {i}: {WaveInEvent.GetCapabilities(i).ProductName}");
    }

    while(line_in < -1 || line_in >= WaveInEvent.DeviceCount)
    {
        Console.Write($"Choice: ");
        string choice = Console.ReadLine()?? string.Empty;

        if(int.TryParse(choice, out line_in)
            && line_in >= -1 && line_in < WaveInEvent.DeviceCount)
        {
            break;
        }
        else
        {
            Console.Error.WriteLine($"Invalid selection {choice}.");
        }
    }
}

//Attach openai to agents
foreach(var agent in agents)
{
    if(agent is OpenAIAgent openai_agent)
    {
        openai_agent.ChatClient = chat_client;
        openai_agent.ChatOptions = chat_options;
        openai_agent.SpeechClient = speech_client;
    }
    else if(agent is UserAgent user_agent)
    {
        user_agent.TranscribeClient = transcribe_client;

        //Set line in device for user agent
        user_agent.LineIn = line_in;
    }
}

//Create group chat
var group_chat = new GroupChat(config, agents);

//Get cooldown
TimeSpan? cooldown = (double.TryParse(config["Settings:Cooldown"], out var cldn) && cldn > 0)
    ? TimeSpan.FromMilliseconds(cldn)
    : null;

//Start group chat
var console_group_chat = new ConsoleGroupChat(group_chat, new()
{
    Cooldown = cooldown,
    EnableAudio = !bool.TryParse(config["Args:Disable-Speech"], out var disable_speech) || !disable_speech,
    UserPollCooldown = TimeSpan.FromMilliseconds(25)
});

try
{
    await console_group_chat.RunAsync(sigint_cancellation_source.Token);
}
//Swallow cancellation exceptions
catch(Exception e) when (e is OperationCanceledException or TaskCanceledException) { }

return;
