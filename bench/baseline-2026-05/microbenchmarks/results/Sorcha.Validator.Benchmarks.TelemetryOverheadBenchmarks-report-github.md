```

BenchmarkDotNet v0.15.7, Windows 11 (10.0.26200.8328/25H2/2025Update/HudsonValley2)
Intel Core i9-7960X CPU 2.80GHz (Max: 2.81GHz) (Kaby Lake), 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  Job-WWFBRO : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  UnrollFactor=16  
WarmupCount=3  

```
| Method                | TelemetryEnabled | Mean         | Error      | StdDev     | Ratio  | RatioSD | Allocated | Alloc Ratio |
|---------------------- |----------------- |-------------:|-----------:|-----------:|-------:|--------:|----------:|------------:|
| **Empty_Loop**            | **False**            |     **4.719 ns** |  **0.1071 ns** |  **0.0560 ns** |   **1.00** |    **0.02** |         **-** |          **NA** |
| RuleEmitted_x16       | False            |     4.631 ns |  0.1162 ns |  0.0516 ns |   0.98 |    0.02 |         - |          NA |
| TimeRule_Empty_x16    | False            |     8.718 ns |  0.3372 ns |  0.1764 ns |   1.85 |    0.04 |         - |          NA |
| TimeSection_Empty_x16 | False            |     8.774 ns |  0.0913 ns |  0.0405 ns |   1.86 |    0.02 |         - |          NA |
|                       |                  |              |            |            |        |         |           |             |
| **Empty_Loop**            | **True**             |     **4.701 ns** |  **0.0941 ns** |  **0.0418 ns** |   **1.00** |    **0.01** |         **-** |          **NA** |
| RuleEmitted_x16       | True             |   439.407 ns |  5.8614 ns |  2.6025 ns |  93.48 |    0.93 |         - |          NA |
| TimeRule_Empty_x16    | True             |   980.565 ns | 12.7349 ns |  5.6544 ns | 208.60 |    2.05 |         - |          NA |
| TimeSection_Empty_x16 | True             | 1,061.686 ns | 19.5988 ns | 10.2505 ns | 225.86 |    2.77 |         - |          NA |
