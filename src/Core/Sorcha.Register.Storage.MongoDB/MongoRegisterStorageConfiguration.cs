// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Storage.MongoDB;

/// <summary>
/// Configuration for MongoDB Register storage.
/// </summary>
public class MongoRegisterStorageConfiguration
{
    /// <summary>
    /// MongoDB connection string. Required — must be provided via configuration.
    /// No default is set to prevent accidental use of a local instance in production.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Database name for register storage.
    /// When UseDatabasePerRegister is true, this is the registry database for register metadata.
    /// When false, all data is stored in this single database.
    /// </summary>
    public string DatabaseName { get; set; } = "sorcha_register";

    /// <summary>
    /// Use a separate database for each register's data (recommended for production).
    /// When true, register metadata is in DatabaseName, and each register's transactions/dockets
    /// are in their own database named "{DatabaseNamePrefix}{RegisterId}".
    /// </summary>
    public bool UseDatabasePerRegister { get; set; } = true;

    /// <summary>
    /// Prefix for per-register databases. Default: "sorcha_register_"
    /// Each register will have database: "{DatabaseNamePrefix}{RegisterId}"
    /// </summary>
    public string DatabaseNamePrefix { get; set; } = "sorcha_register_";

    /// <summary>
    /// Collection name for registers (in registry database).
    /// </summary>
    public string RegisterCollectionName { get; set; } = "registers";

    /// <summary>
    /// Collection name for transactions.
    /// </summary>
    public string TransactionCollectionName { get; set; } = "transactions";

    /// <summary>
    /// Collection name for dockets.
    /// </summary>
    public string DocketCollectionName { get; set; } = "dockets";

    /// <summary>
    /// Maximum batch size for bulk operations.
    /// </summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>
    /// Whether to create indexes on startup.
    /// </summary>
    public bool CreateIndexesOnStartup { get; set; } = true;
}
