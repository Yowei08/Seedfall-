using System;

namespace Seedfall
{
    /// <summary>
    /// PCG32：唯一的逻辑随机源。同种子 + 同输入序列 => 完全相同的结果。
    /// 表现层随机必须走 <see cref="Fork"/>，不得消费逻辑流。
    /// </summary>
    public struct Pcg32
    {
        private ulong _state;
        private readonly ulong _inc;

        public const ulong Multiplier = 6364136223846793005UL;

        public Pcg32(ulong seed, ulong seq = 1UL)
        {
            _state = 0UL;
            _inc = (seq << 1) | 1UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * Multiplier + _inc;
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>[0,1)</summary>
        public float NextFloat()
        {
            return (NextUInt() >> 8) * (1.0f / 16777216.0f);
        }

        public float Range(float min, float max)
        {
            return min + NextFloat() * (max - min);
        }

        /// <summary>[minInclusive, maxExclusive)</summary>
        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(NextUInt() % (uint)(maxExclusive - minInclusive));
        }

        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat() < probability;
        }

        public UnityEngine.Vector2 InsideCircle(float radius)
        {
            float a = Range(0f, 6.2831853f);
            float r = radius * MathfSqrt(NextFloat());
            return new UnityEngine.Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r);
        }

        private static float MathfSqrt(float v) => (float)Math.Sqrt(v);

        /// <summary>派生一条独立流（表现层/子系统专用），不影响本流。</summary>
        public Pcg32 Fork(ulong seq)
        {
            return new Pcg32(_state ^ (seq * 0x9E3779B97F4A7C15UL), seq);
        }
    }
}
