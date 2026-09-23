namespace SkyConnect.RoamingoFlightExporter.Application;

public interface IBoundedParallelRunner
{
    Task ForEachAsync<T>(
        IEnumerable<T> items,
        int maxDegreeOfParallelism,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken);
}

public sealed class BoundedParallelRunner : IBoundedParallelRunner
{
    public Task ForEachAsync<T>(
        IEnumerable<T> items,
        int maxDegreeOfParallelism,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);

        return Parallel.ForEachAsync(
            items,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxDegreeOfParallelism,
                CancellationToken = cancellationToken
            },
            handler);
    }
}
