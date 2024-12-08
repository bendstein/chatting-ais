using ChattingAIs.Agent;
using ChattingAIs.Common;
using NAudio.Wave;
using OpenAI;

namespace ChattingAIs;

/// <summary>
/// Facilitate discussion between multiple agents, one of
/// which represents the user
/// </summary>
public class ConsoleGroupChat
{
    /// <summary>
    /// Configuration for group chat
    /// </summary>
    private readonly Config config;

    /// <summary>
    /// The underlying class delegating to each
    /// chat agent
    /// </summary>
    private readonly GroupChat group_chat;

    /// <summary>
    /// The user's agent
    /// </summary>
    private readonly UserAgent user_agent;

    /// <summary>
    /// OpenAI client
    /// </summary>
    private readonly OpenAIClient client;

    public ConsoleGroupChat(GroupChat group_chat, OpenAIClient client, Config config)
    {
        this.group_chat = group_chat;
        this.client = client;
        this.config = config;
        user_agent = group_chat.Agents.Values.Select(a => a as UserAgent)
            .Where(a => a is not null)
            .FirstOrDefault()?? new UserAgent(Constants.USER_AGENT_DEFAULT_ID)
            {
                IsModerator = true
            };
    }

    /// <summary>
    /// Run the conversation loop
    /// </summary>
    /// <param name="token">Cancellation token to cancel the conseration</param>
    /// <returns>A long-running task representing the conversation</returns>
    public async Task RunAsync(CancellationToken token = default)
    {
        //Give instructions and provide agent summaries
        if(config.ShowIntro)
        {
            var agents = group_chat.Agents.Values.Where(a => a != user_agent).ToList();

            Console.WriteLine($"""
            You, {user_agent.Id}, are the moderator of a conversation between {agents.Count} speakers.

            Controls:
              - <Esc>      : Cancel current step of the conversation, and return control to the user.
              - <Enter>    : Toggle conversation between 'Auto' and 'Manual' mode.
                - 'Auto'  : Conversation flows automatically, with a brief delay between each step.
                - 'Manual': Conversation requires user keypress to continue flow.
              - <Space>   : If conversation is paused in 'Manual' mode, continue to next step of the conversation.
              - <Ctrl + C>: Stop program.

            Speaker Profiles:
            {string.Join(Environment.NewLine, agents
                .Select(agent => $"  - {agent.Id}: {agent.Profile}"))}
            """);
        }

        //Control time between each iteration of the loop
        using Stepper stepper = new(config.Cooldown);

        //Start stepper in auto mode
        stepper.SetAutoStep();

        //Event to trigger to manually step
        ManualResetEvent manual_step = new(false);

        //Cancellation token for stopping the current step of the loop
        //and returning control to the user
        CancellationTokenSource current_step_cancellation = new();

        //Explicit target. Start with user
        string? target = user_agent.Id;

        //Task for current speaker.
        Task? speech_task = null;

        //Mutex for the above items that modify the flow of the
        //conversation
        Lock mutex = new();

        //Task handling user input
        Task user_input_task = Task.Run(async () =>
        {
            //Poll for keypress
            while(!token.IsCancellationRequested)
            {
                //If user isn't speaking, isn't the explicit target, and keyboard input is available
                if(!user_agent.IsActive && target != user_agent.Id && Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);

                    switch(key.Key)
                    {
                        //Escape; Cancel current step
                        case ConsoleKey.Escape:
                        {
                            lock(mutex)
                            {
                                current_step_cancellation.Cancel();
                            }
                        }
                        break;
                        //Spacebar; Trigger manual step
                        case ConsoleKey.Spacebar:
                        {
                            lock(mutex)
                            {
                                manual_step.Set();
                            }
                        }
                        break;
                        //Return; Toggle current mode
                        case ConsoleKey.Enter:
                        {
                            lock(mutex)
                            {
                                //Reset manual step event
                                manual_step.Reset();

                                //Change to next step type
                                stepper.ToggleStepType(manual_step);
                            }
                        }
                        break;
                    }
                }

                await Task.Delay(config.UserPollCooldown);
            }
        }, token);

        //Continue looping until done
        while(!token.IsCancellationRequested)
        {
            try
            {
                //Joined cancellation token for current step
                var joined_cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, current_step_cancellation.Token);

                IAgent? agent = null;

                try
                {
                    //Get message from agent
                    var response = await group_chat.StepAsync(target, joined_cancellation.Token);

                    agent = response.Agent;

                    //Wait for current speaker to finish speaking.
                    if(speech_task is not null)
                    {
                        await speech_task;
                        speech_task = null;
                    }

                    //If agent is audio-capable, and speech is enabled, start speaking
                    if(config.EnableAudio && response.Agent is IAudioAgent audio_agent)
                    {
                        speech_task = Task.Run(async () =>
                        {
                            //Get audio corresponding to response message
                            var speech = await audio_agent.SpeakAsync(response.Message, token);

                            //Play to audio device
                            using var audio = new Mp3FileReader(speech);
                            using var wave_out = new WaveOutEvent();

                            //Wait handle for audio to complete
                            ManualResetEvent audio_complete = new(false);

                            wave_out.PlaybackStopped += (sender, e) =>
                            {
                                //Trigger wait handle indicating audio completion
                                audio_complete.Set();
                            };

                            wave_out.Init(audio);
                            wave_out.Play();

                            //When cancellation token is triggered, stop audio
                            CancellationTokenRegistration ctr = joined_cancellation.Token.Register(() =>
                            {
                                wave_out.Stop();
                            });

                            try
                            {
                                //Wait for audio to complete
                                await audio_complete.WaitOneAsync(token);
                            }
                            finally
                            {
                                //Unregister callback from cancellation token
                                ctr.Unregister();
                            }

                        }, token);
                    }

                    //Print message to console
                    Console.WriteLine(response.Message);
                }
                finally
                {
                    //Clear explicit target
                    lock(mutex)
                    {
                        target = null;
                    }
                }

                //Wait for next step, except for user
                if(agent is null || agent is not UserAgent)
                    await stepper.StepAsync(joined_cancellation.Token);
            }
            catch(Exception e) when(e is OperationCanceledException or TaskCanceledException) 
            { 
                //Propagate cancelltion from main cancellation token
                if(token.IsCancellationRequested)
                {
                    throw;
                }
                //Cancel current step only, and return control to the user
                else if(current_step_cancellation.IsCancellationRequested)
                {
                    lock(mutex)
                    {
                        current_step_cancellation = new();
                        target = user_agent.Id;
                    }
                }
                //Shouldn't happen
                else
                {
                    throw;
                }
            }
        }

        //Wait for current speaker to finish speaking.
        if(speech_task is not null)
        {
            await speech_task;
            speech_task = null;
        }

        //Wait for user input loop to complete.
        //At this point token should be cancelled,
        //So this shouldn't take long
        await user_input_task;
    }

    /// <summary>
    /// Class handling 'step' logic for loop in <see cref="RunAsync(CancellationToken)"/>.
    /// </summary>
    /// <param name="cooldown">The time to wait between steps in Auto step mode.</param>
    private class Stepper(TimeSpan? cooldown) : IDisposable
    {
        /// <summary>
        /// When in auto-step mode, wait for this timespan for next step
        /// </summary>
        private TimeSpan? cooldown = cooldown;

        /// <summary>
        /// Mutex for <see cref="step_mode"/> and <see cref="step_mode_change_flag"/>
        /// </summary>
        private readonly Lock step_mode_lock = new();

        /// <summary>
        /// Mutex to ensure only one <see cref="StepAsync(CancellationToken)"/> can
        /// be executing at a time
        /// </summary>
        private readonly SemaphoreSlim is_stepping = new(1, 1);

        /// <summary>
        /// Cancellation token that triggers whenever <see cref="step_mode"/> is changed.
        /// It is re-instantiated in a lock statement in <see cref="StepAsync(CancellationToken)"/>
        /// </summary>
        private CancellationTokenSource step_mode_change_flag = new();

        /// <summary>
        /// The current step mode. In Auto mode, it will step after each passing of
        /// <see cref="cooldown"/>. In Manual mode, will step each time <see cref="manual_step_source"/>
        /// is triggered.
        /// </summary>
        private StepMode step_mode = StepMode.Auto;

        /// <summary>
        /// Event that triggers each step in Manual step mode.
        /// </summary>
        private ManualResetEvent? manual_step_source = new(false);

        private bool disposed = false;

        public async Task StepAsync(CancellationToken token)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            //Only allow this to be running one at a time
            await is_stepping.WaitAsync(token);

            try
            {
                bool done;

                do
                {
                    done = true;

                    token.ThrowIfCancellationRequested();

                    //Get current step mode
                    StepMode current_step_mode;

                    lock(step_mode_lock)
                    {
                        current_step_mode = step_mode;

                        //Reset change flag
                        step_mode_change_flag = new();
                    }

                    switch(current_step_mode)
                    {
                        case StepMode.Auto:
                        {
                            token.ThrowIfCancellationRequested();

                            //Auto step with no cooldown.
                            if(!cooldown.HasValue)
                                break;

                            //Wait for any of the following:
                            // - Waited for cooldown to elapse
                            // - Task is cancelled
                            // - Step mode is changed (step_mode_change_flag is cancelled)
                            try
                            {
                                await Task.Delay(cooldown.Value,
                                    CancellationTokenSource.CreateLinkedTokenSource(
                                        token,
                                        step_mode_change_flag.Token
                                    ).Token);
                            }
                            catch(Exception e) when(e is OperationCanceledException or TaskCanceledException)
                            {
                                //If task was actually cancelled, propagate
                                if(token.IsCancellationRequested)
                                {
                                    throw;
                                }
                                //If Step Mode is changed, change mode and continue loop
                                else if(step_mode_change_flag.IsCancellationRequested)
                                {
                                    done = false;

                                    //Change mode and reset flag
                                    lock(step_mode_lock)
                                    {
                                        current_step_mode = step_mode;

                                        //Reset change flag
                                        step_mode_change_flag = new();
                                    }
                                }
                                //Don't think this can happen
                                else
                                {
                                    throw;
                                }
                            }
                        }
                        break;
                        case StepMode.Manual:
                        {
                            token.ThrowIfCancellationRequested();

                            //Should never happen
                            if(manual_step_source is null)
                                throw new InvalidOperationException($"Cannot manually step without manual step source.");

                            //Wait for any of the following to happen:
                            // - Manual step is triggered (manual_step_source is cancelled)
                            // - Task is cancelled
                            // - Step mode is changed (step_mode_change_flag is cancelled)
                            try
                            {
                                await manual_step_source.WaitOneAsync(CancellationTokenSource.CreateLinkedTokenSource(
                                        token,
                                        step_mode_change_flag.Token
                                    ).Token);
                            }
                            catch(Exception e) when(e is OperationCanceledException or TaskCanceledException)
                            {
                                //If task was actually cancelled, propagate
                                if(token.IsCancellationRequested)
                                {
                                    throw;
                                }
                                //If Step Mode is changed, change mode and continue loop
                                else if(step_mode_change_flag.IsCancellationRequested)
                                {
                                    done = false;

                                    //Change mode and reset flag
                                    lock(step_mode_lock)
                                    {
                                        current_step_mode = step_mode;

                                        //Reset change flag
                                        step_mode_change_flag = new();
                                    }
                                }
                                //Don't think this can happen
                                else
                                {
                                    throw;
                                }
                            }
                            finally
                            {
                                //Reset manual step
                                manual_step_source.Reset();
                            }
                        }
                        break;
                        default:
                        throw new NotImplementedException();
                    }
                }
                while(!done);
            }
            finally
            {
                is_stepping.Release();
            }
        }

        /// <summary>
        /// Change step mode to Auto, stepping after each passing of
        /// <see cref="cooldown"/>
        /// </summary>
        public void SetAutoStep()
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            lock(step_mode_lock)
            {
                if(step_mode != StepMode.Auto)
                {
                    //Set mode
                    step_mode = StepMode.Auto;

                    //Set change flag
                    step_mode_change_flag.Cancel();
                }
            }
        }

        /// <summary>
        /// Change step mode to Manual, stepping each time
        /// <see cref="manual_step_source"/> is triggered.
        /// </summary>
        /// <param name="manual_step_source">The source of manual stepping</param>
        public void SetManualStep(ManualResetEvent manual_step_source)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            lock(step_mode_lock)
            {
                if(step_mode != StepMode.Manual)
                {
                    //Set mode
                    step_mode = StepMode.Manual;

                    //Set manual step source
                    this.manual_step_source = manual_step_source;

                    //Set change flag
                    step_mode_change_flag.Cancel();
                }
            }
        }

        /// <summary>
        /// Toggle step mode
        /// </summary>
        /// <param name="manual_step_source">The source of manual steps, in manual step mode</param>
        public void ToggleStepType(ManualResetEvent? manual_step_source = null)
        {
            ObjectDisposedException.ThrowIf(disposed, this);

            lock(step_mode_lock)
            {
                switch(step_mode)
                {
                    case StepMode.Auto:
                    {
                        SetManualStep(manual_step_source ?? throw new ArgumentNullException(nameof(manual_step_source)));
                    }
                    break;
                    case StepMode.Manual:
                    {
                        SetAutoStep();
                    }
                    break;
                    default:
                    {
                        throw new InvalidOperationException();
                    }
                }
            }
        }

        public enum StepMode
        {
            Auto,
            Manual
        }

        #region IDisposable

        private void Dispose(bool disposing)
        {
            if(!disposed)
            {
                disposed = true;

                if(disposing)
                {
                    is_stepping.Dispose();
                    step_mode_change_flag.Dispose();
                    manual_step_source?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            Dispose(true);
        }

        #endregion
    }

    /// <summary>
    /// Configuration for group chat
    /// </summary>
    public class Config
    {
        /// <summary>
        /// Whether to provide instructions and agent summaries on start.
        /// </summary>
        public bool ShowIntro { get; set; } = true;

        /// <summary>
        /// Whether text-to-speech audio is enabled
        /// </summary>
        public bool EnableAudio { get; set; } = true;

        /// <summary>
        /// Delay between steps in auto-step mode
        /// </summary>
        public TimeSpan? Cooldown { get; set; }

        /// <summary>
        /// Delay when polling for user input
        /// </summary>
        public TimeSpan UserPollCooldown { get; set; }
    }
}