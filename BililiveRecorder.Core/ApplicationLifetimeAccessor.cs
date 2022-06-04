using System.Threading;
namespace BililiveRecorder.Core;

public interface IApplicationLifetimeAccessor
{
    CancellationToken ApplicationStopping { get; }
}

public class NullApplicationLifetimeAccessor : IApplicationLifetimeAccessor
{
    public static NullApplicationLifetimeAccessor Instance => new NullApplicationLifetimeAccessor();

    public CancellationToken ApplicationStopping => CancellationToken.None;
}

