// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Services.Sms;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// #1214 — AcsSmsSender has no real provider call. It used to log "SMS dispatched" and return, so a
/// configured deploy switched SMS 2FA / phone-verification ON while every one-time code silently
/// vanished and the log claimed success. It must fail loud unless an operator explicitly opts into the
/// dev no-op.
/// </summary>
public class AcsSmsSenderTests
{
    private static AcsSmsSender Create(bool allowNoOp) =>
        new(NullLogger<AcsSmsSender>.Instance,
            Options.Create(new SmsSettings { AcsConnectionString = "endpoint=...", AllowNoOpSender = allowNoOp }));

    [Fact]
    public async Task SendAsync_NoDevFlag_Throws_RatherThanSilentlyDropTheCode()
    {
        var sender = Create(allowNoOp: false);

        var act = () => sender.SendAsync("+447700900123", "Your code is 123456");

        (await act.Should().ThrowAsync<NotSupportedException>())
            .WithMessage("*not implemented*")
            .WithMessage("*AllowNoOpSender*");
    }

    [Fact]
    public async Task SendAsync_DevFlagSet_ReturnsWithoutThrowing()
    {
        var sender = Create(allowNoOp: true);

        // No throw — the dev no-op path. (It logs a NOT-SENT warning; behaviour under test is that it
        // does not pretend to have sent, i.e. it does not throw and does not report success as delivery.)
        await sender.SendAsync("+447700900123", "Your code is 123456");
    }
}
