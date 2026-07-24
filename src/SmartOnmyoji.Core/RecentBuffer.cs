namespace SmartOnmyoji.Core;

/// <summary>
/// 定长环形缓冲,记录最近 N 个元素,用于"连续 N 次匹配到同一目标"的判定。
/// 取代旧代码里 <c>success_target_list=[0,1,2,3,4,5]</c> + <c>repeat_tolerance-len(set())</c> 的晦涩写法。
/// </summary>
public sealed class RecentBuffer<T> where T : notnull
{
    private readonly T[] _buffer;
    private int _length;
    private int _head;

    public RecentBuffer(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new T[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _length;
    public bool IsFull => _length == _buffer.Length;

    public void Push(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_length < _buffer.Length) _length++;
    }

    /// <summary>缓冲已满且所有元素相等时返回 true。</summary>
    public bool AllSame(IEqualityComparer<T>? comparer = null)
    {
        if (!IsFull) return false;
        comparer ??= EqualityComparer<T>.Default;
        var first = _buffer[0];
        for (var i = 1; i < _buffer.Length; i++)
            if (!comparer.Equals(_buffer[i], first)) return false;
        return true;
    }

    public void Clear()
    {
        _length = 0;
        _head = 0;
        Array.Clear(_buffer);
    }
}
