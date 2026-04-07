// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Decision;

public interface IDecisionEngine
{
    Task<ActionDecision> DecideAsync(PendingAction action, CancellationToken cancellationToken = default);
}
