using BenchmarkDotNet.Running;

// Run any benchmark in this assembly; select with --filter (e.g. --filter *DocketHashOptions*).
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
