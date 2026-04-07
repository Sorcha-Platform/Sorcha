// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Agent;

/// <summary>
/// Standard exit codes for the Sorcha Agent CLI.
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int GeneralError = 1;
    public const int AuthenticationError = 2;
    public const int AuthorizationError = 3;
    public const int ValidationError = 4;
    public const int NotFound = 5;
    public const int ConfigurationError = 6;
    public const int NetworkError = 7;
    public const int ServiceError = 8;
}
