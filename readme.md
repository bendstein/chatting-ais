A small console app in which the user facilitates a conversation between multiple LLM participants.

Command Line Args:

-d, --disable-audio: Do not convert the agents' message to audio and play aloud.

Usage:

- An API key for OpenAI must be provided. It can be put in appsettings.json under OpenAI:Key, or can be
	included as an environmnet variable, ChattingAIs:OpenAI:Key.
- Configure the agents and system prompts in Content/AgentConfig.xml.
	- AgentsConfig > SystemPrompt is a system prompt that is sent to each agent.
	- AgentsConfig > Agents is an array of all participants (AgentsConfig > Agents > Agent)
		- The name attribute is the Agent's identifier. This should be unique per-participant
		- The ignore-root-prompt attribute will cause the agent to not be given AgentsConfig > SystemPrompt
		- The moderator attribute will label the agent as a moderator. When an agent is randomly chosen during
			conversation, it will not choose this agent.
		- Agent with type UserAgent is the user.
		- Agents with type OpenAIAgent represent participants that generate text via OpenAI.
			- Agent > Profile is a description of the participant. All participants' profiles are included in the system prompt.
			- Agent > Speech provides text-to-speech settings. id is the name of the pre-defined voice from OpenAI, and speed ratio
				modifies how quickly the speech is read aloud.
			- Agent > SystemPrompt appends additional text to the main SystemPrompt, that only this agent sees.

- The user will be the first speaker. The user will be prompted for a message. They can press *enter* to forgo their message.
- If the user provides a message, they will be prompted to select who they're speaking to. They should enter a number to select the
	respective participant.
- The other participants will then speak between themselves. By default, the conversation will move automatically.
- Pressing *escape* will cause the conversation to stop, and give control back to the user.
- Pressing *enter* will toggle the conversation between *auto* and *manual* modes.
		- In *auto* mode, the conversation will move automatically, with a short delay.
		- In *manual* mode, the user must press *space* to move the conversation along.
- Pressing *ctrl + c* will exit the program.
