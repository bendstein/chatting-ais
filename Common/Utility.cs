using ChattingAIs.Agent;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Xml.Linq;
using System.Xml.XPath;

namespace ChattingAIs.Common
{
    public static class Utility
    {
        /// <summary>
        /// Build a configuration from the provided command line args, as well
        /// as several other sources, including: <br />
        /// - appsettings.json (see <see cref="Constants.Configuration.APPSETTINGS_PATH"/>) <br />
        /// - User Secrets <br />
        /// - Environment Variables (only those starting with prefix
        ///     <see cref="Constants.Configuration.ICONFIG_ENV_PREFIX"/>) <br />
        /// - OpenAI chat schema (see <see cref="Constants.Configuration.CHAT_SCHEMA_PATH"/>) <br />
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public static IConfigurationRoot BuildConfiguration(params string[] args)
        {
            //Parse arguments into key-value pairs
            var parsed_arguments = new ArgumentParser()
                .WithPositionalParameters([

                ])
                .AddAbbreviation([
                    ('d', "disable-speech")
                ])
                .AddAllowedFlags([
                    "disable-speech"
                ])
                .ParseArguments(args);

            //Read OpenAI chat schema json (from Constants.Configuration.CHAT_SCHEMA_PATH)
            using var fs = new FileStream(Constants.Configuration.CHAT_SCHEMA_PATH, FileMode.Open, FileAccess.Read);
            using var sr = new StreamReader(fs);
            string chat_schema = sr.ReadToEnd();

            /*
             * Build configuration from sources:
             *   - appsettings (from Constants.Configuration.APPSETTINGS_PATH)
             *   - user secrets (from secrets.json)
             *   - environment variables (starting with prefix Constants.Configuration.ICONFIG_ENV_PREFIX)
             *   - parsed command line arguments (add Constants.Configuration.ICONFIG_PATH_ARGS as prefix)
             *   - OpenAI chat schema (see above)
             */
            var config = new ConfigurationBuilder()
                .AddJsonFile(Constants.Configuration.APPSETTINGS_PATH, true, true)
                .AddUserSecrets<Program>(true, true)
                .AddEnvironmentVariables(Constants.Configuration.ICONFIG_ENV_PREFIX)
                .AddInMemoryCollection(parsed_arguments
                    .ToDictionary(
                        pair => $"{Constants.Configuration.ICONFIG_PATH_ARGS}{pair.Key}",
                        pair => pair.Value))
                .AddInMemoryCollection(new Dictionary<string, string?>()
                {
                    { Constants.Configuration.ICONFIG_CHAT_SCHEMA_PATH, chat_schema }
                })
                .Build();

            return config;
        }

        /// <summary>
        /// Get the first non-empty string in the collection.
        /// </summary>
        /// <param name="inputs">The list of strings to coalesce through.</param>
        /// <returns>The first non-empty string in <paramref name="inputs"/>, or <see cref="string.Empty"/> if none.</returns>
        public static string CoalesceString(params IEnumerable<string?> inputs)
        {
            foreach(var input in inputs)
            {
                if(!string.IsNullOrWhiteSpace(input))
                    return input.Trim();
            }

            return string.Empty;
        }

        /// <summary>
        /// Construct a list of agents from an xml configuration file.
        /// </summary>
        /// <param name="path">Path to the agent config xml.</param>
        /// <param name="token">A cancellation token to halt execution early.</param>
        /// <returns>The parsed list of <see cref="IAgent"/>s.</returns>
        /// <exception cref="AgentConfigException"></exception>
        public static async Task<List<IAgent>> ParseAgentConfigAsync(string path, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();

            //Load XML
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var doc = await XDocument.LoadAsync(fs, LoadOptions.PreserveWhitespace, token);

            if(doc.Root is null)
                throw new AgentConfigException("XML missing root node.");

            //Get root prompt
            var root_prompt = doc.Root.Element("SystemPrompt");

            token.ThrowIfCancellationRequested();

            //Get all agents
            var xagents = doc.XPathSelectElements("/AgentsConfig/Agents/Agent");

            //Get all agent descriptions
            var agent_descriptions = xagents
                .Where(xagent => !bool.TryParse(xagent.Attribute("moderator")?.Value ?? string.Empty, out var is_mod) || !is_mod)
                .Select(xagent =>
                {
                    var name = xagent.Attribute("name")?.Value ?? string.Empty;
                    var profile = xagent.Element("Profile")?.Value ?? string.Empty;

                    if(string.IsNullOrWhiteSpace(profile))
                        return name;
                    return $"{name}: {profile.Trim()}";
                })
                .Where(desc => !string.IsNullOrWhiteSpace(desc))
                .ToList();

            //Get moderator names
            var moderator_names = xagents
                .Where(xagent => bool.TryParse(xagent.Attribute("moderator")?.Value?? string.Empty, out var is_mod) && is_mod)
                .Select(xagent => xagent.Attribute("name")?.Value ?? string.Empty)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToList();

            token.ThrowIfCancellationRequested();

            List<IAgent> agents = [];

            foreach(var agent in xagents)
            {
                token.ThrowIfCancellationRequested();

                //Get current agent name
                var name = agent.Attribute("name")?.Value ?? string.Empty;

                //Get current agent profile
                var profile = agent.Element("Profile")?.Value ?? string.Empty;

                //Get agent speech settings
                var xspeech = agent.Element("Speech");

                //Get all prompts
                var agent_prompts = agent.Elements("SystemPrompt").ToList();

                //Prepend root prompt if present and not explicitly specified to ignore
                if(!bool.TryParse(agent.Attribute("ignore-root-prompt")?.Value, out var ignore_root_prompt)
                    || !ignore_root_prompt)
                {
                    if(root_prompt != null)
                        agent_prompts.Insert(0, root_prompt);
                }

                //Simplify and aggregate prompts
                StringBuilder prompt_builder = new();

                for(int i = 0; i < agent_prompts.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var prompt = agent_prompts[i];

                    foreach(var node in prompt.Nodes())
                    {
                        //Text node
                        if(node is XText text_node)
                        {
                            prompt_builder.Append(text_node.ToString());
                        }
                        //Element node
                        else if(node is XElement element)
                        {
                            //Format node
                            if(element.Name.LocalName == "Format")
                            {
                                switch(element.Attribute("format-type")?.Value)
                                {
                                    case "agent-name":
                                    {
                                        prompt_builder.Append(name);
                                    }
                                    break;
                                    case "agent-list":
                                    {
                                        prompt_builder.Append(string.Join("\r\n    ", agent_descriptions.Select(d => $"- {d}")));
                                    }
                                    break;
                                    case "moderator-name":
                                    {
                                        prompt_builder.Append(string.Join(", ", moderator_names));
                                    }
                                    break;
                                    default:
                                    break;
                                }
                            }
                            else
                            {
                                throw new AgentConfigException($"Unexpected node {node} in Agent Config.");
                            }
                        }
                        else
                        {
                            throw new AgentConfigException($"Unexpected node {node} in Agent Config.");
                        }
                    }

                    if(i + 1 < agent_prompts.Count)
                        prompt_builder.AppendLine();
                }

                var simplified_prompt = prompt_builder.ToString();

                token.ThrowIfCancellationRequested();

                bool is_moderator = bool.TryParse(agent.Attribute("moderator")?.Value ?? string.Empty, out var is_mod) && is_mod;

                //Convert xml agent to object
                switch(agent.Attribute("type")?.Value)
                {
                    case "UserAgent":
                    {
                        UserAgent user_agent = new(name)
                        {
                            IsModerator = is_moderator,
                        };
                        agents.Add(user_agent);
                    }
                    break;
                    case "OpenAIAgent":
                    {
                        OpenAIAgent openai_agent = new(name)
                        {
                            IsModerator = is_moderator,
                            SystemPrompt = simplified_prompt,
                            VoiceId = xspeech?.Attribute("id")?.Value ?? Constants.OpenAI.VOICE_DFT,
                            Profile = profile,
                            SpeechOptions = new()
                            {
                                SpeedRatio = Math.Clamp(float.TryParse(xspeech?.Attribute("speed-ratio")?.Value, out var ratio)
                                    ? ratio
                                    : 1f, 0f, 2f)
                            }
                        };
                        agents.Add(openai_agent);
                    }
                    break;
                    default:
                    break;
                }
            }

            return agents;
        }
    }
}