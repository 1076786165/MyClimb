// 不依赖 MonoBehaviour 的普通单例
using System;

public abstract class Singleton<T> where T : class, new()
{
    private static T _instance;
    private static readonly object _lock = new object();

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new T();
                    }
                }
            }
            return _instance;
        }
    }

    public static bool IsInstanceValid => _instance != null;

    public static void DestroyInstance()
    {
        if (_instance is IDisposable disposable)
        {
            disposable.Dispose();
        }
        _instance = null;
    }
}