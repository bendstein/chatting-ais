using ChattingAIs.Agent;
using Microsoft.Extensions.Configuration;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace ChattingAIs;

public class GroupChat
{
    private readonly Dictionary<string, IAgent> agents = [];

    private readonly List<AgentResponseMessage> history = [];

    private readonly int? history_capacity;

    private readonly Lock agent_lock = new();

    private readonly ReaderWriterLockSlim history_lock = new(LockRecursionPolicy.SupportsRecursion);

    private readonly Random random = Random.Shared;

    private readonly IConfiguration config;

    public ReadOnlyDictionary<string, IAgent> Agents
    {
        get
        {
            lock(agent_lock)
                return agents.AsReadOnly();
        }
    }

    public GroupChat(IConfiguration config, IEnumerable<IAgent>? agents = null, int? history_capacity = null, Random? random = null)
    {
        this.config = config;

        this.history_capacity = history_capacity.HasValue
            ? Math.Max(1, history_capacity.Value) 
            : null;

        if(random is not null)
            this.random = random;

        if(agents is not null)
            AddAgents(agents);
    }

    public async Task<AgentResponse> StepAsync(string? agent_id = null, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        //Get most recent message
        AgentResponseMessage? last_message = null;

        history_lock.EnterReadLock();
        try
        {
            last_message = history.LastOrDefault();
        }
        finally
        {
            history_lock.ExitReadLock();
        }

        //Untargeted step, check target of most recent message
        if(string.IsNullOrWhiteSpace(agent_id))
        {
            //If last message is targeted, use target as next agent
            if(last_message is not null && !string.IsNullOrWhiteSpace(last_message.Target))
            {
                agent_id = last_message.Target;
            }
        }

        //If still no target, or agent doesn't exist, choose a random non-moderator
        if(string.IsNullOrWhiteSpace(agent_id) || !HasAgent(agent_id))
        {
            List<string> possible_targets;

            lock(agent_lock)
                possible_targets = agents.Where(a => !a.Value.IsModerator)
                    .Select(a => a.Key)
                    .ToList();

            //If there is more than one possible target, do not include the last
            //speaker in the pool
            if(possible_targets.Count > 1 && last_message != null)
                possible_targets.Remove(last_message.Sender);

            agent_id = possible_targets[random.Next(possible_targets.Count)];
        }

        //No valid agent
        if(string.IsNullOrWhiteSpace(agent_id) || !TryGetAgent(agent_id, out var agent))
            throw new KeyNotFoundException($"No agent with id {agent_id ?? string.Empty}.");

        //Send discussion history to agent
        var response = await agent.ChatAsync(new(GetHistoryForAgent(agent), GetAgents()), token);

        //Make sure to keep this consistent
        response.Message.Sender = agent.Id;

        //Push to history
        AddHistory(response.Message);

        //Return response
        return response;
    }

    #region Manage Agents

    public IAgent GetAgent(string id)
    {
        lock(agent_lock)
            return agents[id];
    }

    public bool TryGetAgent(string id, [NotNullWhen(true)] out IAgent? agent)
    {
        lock(agent_lock)
            return agents.TryGetValue(id, out agent);
    }

    public bool HasAgent(string id)
    {
        lock(agent_lock)
            return agents.ContainsKey(id);
    }

    public bool HasAgent(IAgent agent)
    {
        lock(agent_lock)
            return agents.ContainsKey(agent.Id);
    }

    public void AddAgents(params IEnumerable<IAgent> agents)
    {
        lock(agent_lock)
        {
            foreach(var agent in agents)
                this.agents.Add(agent.Id, agent);
        }
    }

    public void SetAgents(params IEnumerable<IAgent> agents)
    {
        lock(agent_lock)
        {
            foreach(var agent in agents)
                this.agents[agent.Id] = agent;
        }
    }

    public ReadOnlyDictionary<string, IAgent> GetAgents()
    {
        lock(agent_lock)
            return agents.AsReadOnly();
    }

    #endregion

    #region Manage History

    public ImmutableList<AgentResponseMessage> GetHistory()
    {
        history_lock.EnterReadLock();
        try
        {
            return [.. history];
        }
        finally
        {
            history_lock.ExitReadLock();
        }
    }

    public void AddHistory(params IEnumerable<AgentResponseMessage> messages)
    {
        history_lock.EnterWriteLock();
        try
        {
            foreach(var message in messages)
            {
                history.Add(message);

                //Dequeue oldest message if capacity is exceeded
                if(history_capacity.HasValue && history_capacity.Value > 0
                    && history_capacity.Value < history.Count)
                {
                    history.RemoveAt(0);
                }
            }
        }
        finally
        {
            history_lock.ExitWriteLock();
        }
    }

    public ImmutableList<AgentResponseMessage> GetHistoryForAgent(IAgent _agent)
    {
        return GetHistory();
    }

    #endregion
}