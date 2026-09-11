using System;
using System.Collections.Concurrent;
using UnityEngine;

public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static UnityMainThreadDispatcher _instance;
    public static UnityMainThreadDispatcher Instance
    {
        get
        {
            if (_instance == null)
            {
                // シーン内に存在しない場合は、新しいGameObjectを作成してアタッチ
                GameObject dispatcherObject = new GameObject("UnityMainThreadDispatcher");
                _instance = dispatcherObject.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(dispatcherObject); // シーン遷移時に破棄されないようにする
            }
            return _instance;
        }
    }

    private readonly ConcurrentQueue<Action> _executionQueue = new ConcurrentQueue<Action>();

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        _instance = this;
        DontDestroyOnLoad(this.gameObject);
    }

    public void Enqueue(Action action)
    {
        if (action == null) throw new ArgumentNullException("action");
        _executionQueue.Enqueue(action);
    }

    private void Update()
    {
        while (_executionQueue.TryDequeue(out var action))
        {
            action?.Invoke();
        }
    }
}