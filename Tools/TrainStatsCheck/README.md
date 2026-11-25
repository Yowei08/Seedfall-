# TrainStatsCheck —— 训练场伤害统计的离线验证

不进 Unity 构建，只把 `Assets/Scripts/Training/TrainingStats.cs`（**原文件**）编进来，
再用一个同语义的 `UnityEngine.Mathf` 替身跑算法用例。

跑法（需要 .NET 10 运行时；本机 10.0.12 已装）：

    dotnet run --project Tools\TrainStatsCheck\TrainStatsCheck.csproj

期望输出最后一行：`ALL PASS`

覆盖的用例：单次命中 DPS / 1 秒窗口滚动 / 4 次每秒的量化范围 / 10 秒无伤归零 /
长帧不出现负 DPS / 字段里没有可增长容器。