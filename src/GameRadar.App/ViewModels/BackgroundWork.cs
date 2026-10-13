namespace GameRadar.App.ViewModels;

/// <summary>
/// The work started without being awaited (a recomputation, card pictures, a catalog
/// downloaded on a click), kept until it ends: what <see cref="WhenIdleAsync"/> waits
/// for before the tests delete their folder. Only touched on the UI thread.
/// </summary>
public sealed class BackgroundWork
{
    private readonly HashSet<Task> _running = [];

    /// <summary>Keeps <paramref name="task"/> until it ends, and returns it.</summary>
    public Task Track(Task task)
    {
        _running.RemoveWhere(t => t.IsCompleted);
        _running.Add(task);
        return task;
    }

    public bool IsBusy => _running.Any(t => !t.IsCompleted);

    /// <summary>
    /// Completes once the work started so far is over, and the work it started
    /// meanwhile too. Never throws: the tracked work reports its own errors.
    /// </summary>
    public async Task WhenIdleAsync()
    {
        while (_running.Where(t => !t.IsCompleted).ToList() is { Count: > 0 } running)
        {
            try
            {
                await Task.WhenAll(running);
            }
            catch (Exception)
            {
                // Reported where it happened.
            }
        }
    }
}
