```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.8875)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method       | Mean     | Error    | StdDev   | Allocated |
|------------- |---------:|---------:|---------:|----------:|
| LoadMetadata | 45.36 ms | 13.04 ms | 0.715 ms | 160.65 KB |
