using System;
using Sea = Seedfall;

namespace UnityEngine
{
    // TrainingStats.cs 只用到 Mathf.Max / Mathf.Min 两个引擎 API，这里给同语义替身
    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
    }
}

namespace TrainStatsCheck
{
    internal static class Program
    {
        private static int _fail;

        private static void Check(string label, bool ok, string detail)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + label + "  " + detail);
            if (!ok) _fail++;
        }

        private static int Main()
        {
            const float dt = 1f / 60f;   // 60 fps 固定步长

            // 用例 1：t=0 打 12，立刻 DPS = 12，累计 = 12
            var s = new Sea.TrainingStats();
            s.AddDamage(12f);
            s.Tick(dt);
            Check("1a 单次命中后 DPS=12", Math.Abs(s.Dps - 12f) < 0.001f, "dps=" + s.Dps.ToString("0.000"));
            Check("1b 累计=12", Math.Abs(s.Total - 12f) < 0.001f, "total=" + s.Total.ToString("0.000"));

            // 用例 2：不再攻击，1 秒后（窗口滚空）DPS 归 0，但累计仍 = 12
            for (int i = 0; i < 62; i++) s.Tick(dt);
            Check("2a 1 秒后 DPS=0", s.Dps < 0.001f, "dps=" + s.Dps.ToString("0.0000"));
            Check("2b 累计仍=12（未到 10 秒）", Math.Abs(s.Total - 12f) < 0.001f, "total=" + s.Total.ToString("0.000"));

            // 用例 3：按住左键（每 0.25 s 一次 x12）跑 4 秒
            var s3 = new Sea.TrainingStats();
            for (int f = 0; f < 60 * 4; f++)
            {
                if (f % 15 == 0) s3.AddDamage(12f);
                s3.Tick(dt);
            }
            // 名义 48 DPS；1 秒窗是半开区间，命中正好落在窗边界时窗内是 3 次或 4 次 => 36~48
            Check("3a 按住左键读数落在 36~48（1 秒窗对 0.25 s 节奏的量化范围）",
                  s3.Dps >= 35.5f && s3.Dps <= 48.5f, "dps=" + s3.Dps.ToString("0.00"));
            Check("3b 命中率确实是 4 次/秒", s3.Hits == 16, "hits over 4s = " + s3.Hits);
            Check("3c 4 秒累计=192", Math.Abs(s3.Total - 192f) < 0.001f, "total=" + s3.Total.ToString("0.0"));

            // 用例 4：10 秒无伤 => 累计归 0、DPS 归 0、命中数归 0
            var s4 = new Sea.TrainingStats();
            s4.AddDamage(100f);
            for (int i = 0; i < 60 * 9; i++) s4.Tick(dt);
            Check("4a 9 秒时累计还在=100", Math.Abs(s4.Total - 100f) < 0.5f, "total=" + s4.Total.ToString("0.0"));
            Check("4b 9 秒时倒计时约 1 秒", Math.Abs(s4.SecondsToReset - 1f) < 0.2f,
                  "left=" + s4.SecondsToReset.ToString("0.00"));
            for (int i = 0; i < 61; i++) s4.Tick(dt);
            Check("4c 10 秒后累计归 0", s4.Total < 0.001f, "total=" + s4.Total.ToString("0.000"));
            Check("4d 10 秒后 DPS=0", s4.Dps < 0.001f, "dps=" + s4.Dps.ToString("0.0000"));
            Check("4e 10 秒后命中数归 0", s4.Hits == 0, "hits=" + s4.Hits);

            // 用例 5：长帧（卡顿 0.5 s）不崩、不出现负 DPS
            var s5 = new Sea.TrainingStats();
            s5.AddDamage(50f);
            for (int i = 0; i < 40; i++) s5.Tick(0.5f);
            Check("5a 长帧后 DPS 非负且=0", s5.Dps >= 0f && s5.Dps < 0.001f, "dps=" + s5.Dps.ToString("0.0000"));

            // 用例 6：字段里没有可增长的容器（List / Dictionary）
            var flds = typeof(Sea.TrainingStats).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            bool allFixed = true;
            foreach (var f in flds)
            {
                string t = f.FieldType.ToString();
                if (t.Contains("List") || t.Contains("Dictionary")) allFixed = false;
            }
            Check("6a 只有定长数组/值类型字段（无 List/Dictionary）", allFixed, "fields=" + flds.Length);

            Console.WriteLine(_fail == 0 ? "ALL PASS" : (_fail + " FAILED"));
            return _fail == 0 ? 0 : 1;
        }
    }
}