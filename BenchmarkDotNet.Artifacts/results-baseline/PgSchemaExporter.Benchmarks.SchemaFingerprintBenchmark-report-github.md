```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26100.8875)
Intel Core i9-9900K CPU 3.60GHz (Coffee Lake), 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.301
  [Host]   : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2
  ShortRun : .NET 8.0.28 (8.0.2826.26413), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method  | Mean     | Error    | StdDev  | Gen0      | Gen1     | Allocated |
|-------- |---------:|---------:|--------:|----------:|---------:|----------:|
| Compute | 181.2 ms | 75.11 ms | 4.12 ms | 1000.0000 | 666.6667 |   8.87 MB |
