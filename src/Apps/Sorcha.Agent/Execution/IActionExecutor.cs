// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Execution;

public interface IActionExecutor
{
    Task<bool> ExecuteAsync(PendingAction action, ActionDecision decision, CancellationToken cancellationToken = default);
}
