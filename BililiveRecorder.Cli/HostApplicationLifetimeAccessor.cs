using System.Threading;
using Microsoft.Extensions.Hosting;

namespace BililiveRecorder.Core;

internal sealed class HostApplicationLifetimeAccessor : IApplicationLifetimeAccessor
{
    private readonly Microsoft.Extensions.Hosting.IHostApplicationLifetime _lifetime;

    public HostApplicationLifetimeAccessor(IHostApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
    }

    public CancellationToken ApplicationStopping => _lifetime.ApplicationStopping;
}