// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using NUnit.Framework;

// Limit parallel test execution to reduce Blazor WASM asset loading contention.
// Too many parallel browser contexts cause mono_download_assets failures
// when the server can't serve all .NET WASM DLLs simultaneously.
[assembly: LevelOfParallelism(3)]
