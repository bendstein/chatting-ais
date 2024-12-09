using NAudio.Wave;

namespace ChattingAIs.Common;

public static class Extensions
{
    /// <summary>
    /// Wait asynchronously for a wait handle to trigger. <br />
    /// See: <a href="https://stackoverflow.com/a/68632819">https://stackoverflow.com/a/68632819</a>
    /// </summary>
    /// <param name="wait_handle"></param>
    /// <param name="timeout"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public static Task WaitOneAsync(this WaitHandle wait_handle, CancellationToken token = default, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(wait_handle);

        TaskCompletionSource<bool> tcs = new();

        //Register callback on cancelltion token cancellation
        CancellationTokenRegistration ctr = token.Register(() => tcs.TrySetCanceled());

        //Register callback on wait handle completion
        RegisteredWaitHandle rwh = ThreadPool.RegisterWaitForSingleObject(wait_handle,
            (_, timedOut) =>
            {
                //On completion, if timeout was exceeded, mark task as cancelled
                if(timedOut)
                {
                    tcs.TrySetCanceled();
                }
                //Otherwise, task was completed successfully
                else
                {
                    tcs.TrySetResult(true);
                }
            },
            null, timeout?? Timeout.InfiniteTimeSpan, true);

        Task<bool> task = tcs.Task;

        //Once task completes, unregister registered callbacks
        _ = task.ContinueWith(_ =>
        {
            rwh.Unregister(null);
            return ctr.Unregister();
        }, CancellationToken.None);

        return task;
    }

    public static async Task PlayAsync(this IWaveProvider source, CancellationToken token = default)
    {
        //Create device
        using var wave_out = new WaveOutEvent();

        //Attach event on audio output completion to
        //trigger wait handle
        ManualResetEvent audio_complete = new(false);
        wave_out.PlaybackStopped += (_, _) => audio_complete.Set();

        //Init device with audio source and start playback
        wave_out.Init(source);

        wave_out.Play();

        //When cancellation token is triggered, stop audio
        using CancellationTokenRegistration ctr = token.Register(wave_out.Stop);

        //Wait for audio to complete
        await audio_complete.WaitOneAsync(token);
    }
}