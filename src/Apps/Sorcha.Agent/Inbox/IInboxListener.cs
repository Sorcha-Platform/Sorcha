// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Sorcha.Agent.Models;

namespace Sorcha.Agent.Inbox;

public interface IInboxListener
{
    IAsyncEnumerable<PendingAction> ListenAsync(CancellationToken cancellationToken = default);
}
