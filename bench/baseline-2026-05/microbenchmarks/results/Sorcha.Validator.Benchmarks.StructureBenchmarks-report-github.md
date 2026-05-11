```

BenchmarkDotNet v0.15.7, Windows 11 (10.0.26200.8328/25H2/2025Update/HudsonValley2)
Intel Core i9-7960X CPU 2.80GHz (Max: 2.81GHz) (Kaby Lake), 1 CPU, 32 logical and 16 physical cores
.NET SDK 10.0.203
  [Host]     : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4
  Job-WWFBRO : .NET 10.0.7 (10.0.7, 10.0.726.21808), X64 RyuJIT x86-64-v4

IterationCount=8  LaunchCount=1  UnrollFactor=16  
WarmupCount=3  

```
| Method                    | Mean     | Error   | StdDev  | Gen0   | Allocated |
|-------------------------- |---------:|--------:|--------:|-------:|----------:|
| ValidateStructure_Minimal | 107.4 ns | 4.50 ns | 2.35 ns | 0.0018 |     144 B |
| ValidateStructure_Medium  | 110.9 ns | 3.09 ns | 1.37 ns | 0.0018 |     144 B |
