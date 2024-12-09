namespace ChattingAIs.Common
{
    public static class Constants
    {
        public const string
            USER_AGENT_DEFAULT_ID = "<user>",
            NO_MESSAGE_CONTENT = "<no content>";

        public static class Configuration
        {
            public const string
                APPSETTINGS_PATH = "appsettings.json",
                AGENT_CONFIG_PATH = "content/AgentConfig.json",
                CHAT_SCHEMA_PATH = "content/OpenAIChatSchema.json",
                ICONFIG_PATH_ARGS = "Args:",
                ICONFIG_ENV_PREFIX = "ChattingAIs:",
                ICONFIG_CHAT_SCHEMA_PATH = "OpenAI:Chat:Schema";
        }

        public static class OpenAI
        {
            public const string
                CHAT_MODEL = "gpt-4o",
                SPEECH_MODEL = "tts-1",
                TRANSCRIBE_MODEL = "whisper-1",
                VOICE_DFT = "alloy";
        }
    }
}