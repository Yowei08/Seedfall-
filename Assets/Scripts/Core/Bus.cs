using System;
using System.Collections.Generic;
using UnityEngine;

namespace Seedfall
{
    /// <summary>轻量事件总线。用于"逻辑已发生"的广播（埋点 / 表现），不含玩法决策。</summary>
    public static class Bus
    {
        private static readonly Dictionary<Type, Delegate> Handlers = new Dictionary<Type, Delegate>();

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            Handlers.TryGetValue(typeof(T), out Delegate d);
            Handlers[typeof(T)] = (Action<T>)d + handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            if (!Handlers.TryGetValue(typeof(T), out Delegate d)) return;
            Delegate next = (Action<T>)d - handler;
            if (next == null) Handlers.Remove(typeof(T));
            else Handlers[typeof(T)] = next;
        }

        public static void Publish<T>(in T evt) where T : struct
        {
            if (Handlers.TryGetValue(typeof(T), out Delegate d) && d is Action<T> a) a(evt);
        }

        public static void Clear() => Handlers.Clear();
    }

    /// <summary>固定步长时钟：渲染帧率变化不影响逻辑结果。</summary>
    public sealed class GameClock
    {
        public const float FixedDt = 1f / 60f;

        private float _accumulator;

        public int TickIndex { get; private set; }
        public int OverrunTicks { get; private set; }

        public void Reset()
        {
            _accumulator = 0f;
            TickIndex = 0;
            OverrunTicks = 0;
        }

        /// <summary>
        /// 每渲染帧调用。onFixedTick 会被调用若干个固定步（最多 maxCatchUp 个，超出丢弃并计数）。
        /// </summary>
        public void Advance(float deltaTime, int maxCatchUp, Action<float> onFixedTick)
        {
            if (deltaTime > 0.25f) deltaTime = 0.25f;
            _accumulator += deltaTime;
            int steps = 0;
            while (_accumulator >= FixedDt)
            {
                _accumulator -= FixedDt;
                steps++;
                if (steps > maxCatchUp)
                {
                    OverrunTicks++;
                    continue; // 追不上就丢弃逻辑帧，避免"慢动作"雪崩
                }
                TickIndex++;
                onFixedTick(FixedDt);
            }
        }
    }

    public static class Log
    {
        public static void Info(string msg) => Debug.Log("[Seedfall] " + msg);
        public static void Warn(string msg) => Debug.LogWarning("[Seedfall] " + msg);
        public static void Error(string msg) => Debug.LogError("[Seedfall] " + msg);
    }
}
