using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BililiveRecorder.Core;

public interface IBackgroundTaskTracer
{
    void AddTask(Task task);

    Task WhenAll(CancellationToken cancellationToken = default);
}

public class BackgroundTaskTracer : IBackgroundTaskTracer
{
    private readonly HashSet<Task> _taskList = new();
    public void AddTask(Task task)
    {
        _taskList.Add(task);
    }

    public async Task WhenAll(CancellationToken cancellationToken = default)
    {
        await Task.WhenAny(Task.WhenAll(_taskList), Task.Delay(-1, cancellationToken));
    }
}