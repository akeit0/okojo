namespace Okojo.Runtime;

/// <summary>
///     Identifies a host task queue.
///     Concrete queue taxonomies belong to hosting/profile layers, not to core Okojo.
/// </summary>
public readonly record struct HostTaskQueueKey(string Name)
{
    public override string ToString()
    {
        return Name;
    }
}
