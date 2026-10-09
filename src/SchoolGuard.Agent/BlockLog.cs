using System.Collections.Concurrent;

namespace SchoolGuard.Agent;

public class BlockLog
{
    readonly ConcurrentQueue<string> _q = new();

    public void Add(string name)
    {
        _q.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {name}");
        while (_q.Count > 500 && _q.TryDequeue(out _)) { }
    }

    public string[] Recent() => _q.Reverse().Take(200).ToArray();
}
