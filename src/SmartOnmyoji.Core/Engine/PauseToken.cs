namespace SmartOnmyoji.Core.Engine;

/// <summary>
/// 协作式暂停原语,与 <see cref="CancellationToken"/> 对称:引擎只在安全点(回合边界)
/// await <see cref="PauseToken.WaitWhilePausedAsync"/> 挂起,暂停期间不消耗 CPU;
/// 调用方(<c>EngineManager</c>)持有 <see cref="PauseTokenSource"/> 控制暂停/恢复,引擎本身不知道谁在暂停它。
/// </summary>
public sealed class PauseTokenSource
{
    private TaskCompletionSource<bool>? _paused;

    public bool IsPaused => Volatile.Read(ref _paused) is not null;

    public PauseToken Token => new(this);

    public void Pause() =>
        Interlocked.CompareExchange(ref _paused, new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously), null);

    public void Resume() =>
        Interlocked.Exchange(ref _paused, null)?.TrySetResult(true);

    internal Task WaitAsync() => Volatile.Read(ref _paused)?.Task ?? Task.CompletedTask;
}

/// <summary>轻量只读句柄,传给引擎;默认值(无 <see cref="PauseTokenSource"/>)表示永不暂停。</summary>
public readonly struct PauseToken(PauseTokenSource? source)
{
    public bool IsPaused => source?.IsPaused ?? false;

    /// <summary>暂停期间挂起;<paramref name="cancellationToken"/> 取消时立即抛出,让 Stop 在暂停中也能生效。</summary>
    public async Task WaitWhilePausedAsync(CancellationToken cancellationToken = default)
    {
        if (source is null) return;
        while (source.IsPaused)
            await source.WaitAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}
