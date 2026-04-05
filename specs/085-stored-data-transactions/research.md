# Research: Stored Data Transactions

**Feature**: 085-stored-data-transactions
**Date**: 2026-04-05
**Status**: Complete

## R1: HKDF in .NET 10

**Decision**: Use the built-in `System.Security.Cryptography.HKDF` static class. No external library needed.

**Rationale**: .NET 10 ships `System.Security.Cryptography.HKDF` (assembly `System.Security.Cryptography v10.0.0.0`) with full RFC 5869 support. The class provides three static methods, each with both allocating (`byte[]`) and span-based (`Span<byte>`) overloads:

- `HKDF.Extract(HashAlgorithmName, ikm, salt)` -- Extract step producing a PRK
- `HKDF.Expand(HashAlgorithmName, prk, outputLength, info)` -- Expand step producing OKM
- `HKDF.DeriveKey(HashAlgorithmName, ikm, outputLength, salt, info)` -- Combined Extract+Expand in one call

Verified working on this project's .NET 10 SDK. The recommended pattern for chunk key derivation:

```csharp
// Derive a per-chunk encryption key from the master file key
// info encodes the chunk index to ensure each chunk gets a unique key
byte[] chunkKey = HKDF.DeriveKey(
    HashAlgorithmName.SHA256,
    ikm: masterFileKey,                          // 32-byte random key per file
    outputLength: 32,                            // XChaCha20-Poly1305 key size
    salt: fileSalt,                              // random salt stored in file reference metadata
    info: Encoding.UTF8.GetBytes($"sorcha-chunk-{chunkIndex}"));
```

Use the span-based overload for zero-allocation paths in hot loops:

```csharp
Span<byte> chunkKey = stackalloc byte[32];
HKDF.DeriveKey(
    HashAlgorithmName.SHA256,
    ikm: masterFileKey.AsSpan(),
    output: chunkKey,
    salt: fileSalt.AsSpan(),
    info: Encoding.UTF8.GetBytes($"sorcha-chunk-{chunkIndex}"));
```

**Alternatives considered**:
- **BouncyCastle `HkdfBytesGenerator`**: Already a project dependency (`BouncyCastle.Cryptography 2.6.2`), but unnecessary -- the .NET built-in is faster (no managed crypto overhead), has span overloads, and requires no additional imports.
- **Libsodium `crypto_kdf_derive_from_key`**: Sodium.Core (1.4.0) exposes this via the `KeyDerivation` class, but it uses BLAKE2b internally, not SHA-256. Using a different hash algorithm from the rest of the HKDF chain would create unnecessary inconsistency.
- **`ECDiffieHellman.DeriveKeyFromHash`**: Already used in CryptoModule.cs (line 653) for P-256 ECIES, but this is ECDH-specific. HKDF is the correct primitive for deriving multiple keys from a single master secret.

---

## R2: XChaCha20-Poly1305 with HKDF

**Decision**: Use HKDF-SHA256 to derive per-chunk 32-byte keys, then feed each directly to the existing `SymmetricCrypto.EncryptAsync()` with `EncryptionType.XCHACHA20_POLY1305`. No compatibility issues.

**Rationale**: HKDF and XChaCha20-Poly1305 are complementary by design:

1. **Key size match**: HKDF-SHA256 outputs arbitrary-length keys. XChaCha20-Poly1305 requires exactly 32 bytes. `HKDF.DeriveKey(..., outputLength: 32, ...)` produces exactly what's needed.

2. **Nonce safety**: XChaCha20-Poly1305 uses a 24-byte random nonce (vs 12-byte for ChaCha20/AES-GCM). With per-chunk derived keys, each chunk uses a unique key, so nonce collision risk is eliminated even without HKDF -- but combining both gives defence in depth. The existing `SymmetricCrypto.EncryptXChaCha20Poly1305Async` already generates a fresh random nonce per call via `GenerateIV()`.

3. **Existing integration**: `SymmetricCrypto.EncryptAsync(plaintext, EncryptionType.XCHACHA20_POLY1305, key)` accepts an explicit key parameter. Pass the HKDF-derived chunk key directly -- no wrapper needed.

4. **No known issues**: HKDF is a standard extract-and-expand KDF (RFC 5869). XChaCha20-Poly1305 is a standard AEAD cipher. They operate at different layers (key derivation vs encryption) with no interaction. This is the same pattern used by Signal Protocol, WireGuard, and Noise Framework.

**Per-chunk encryption flow**:
```
masterFileKey (32 bytes, random)
    |
    +--HKDF-SHA256(salt, info="sorcha-chunk-0")--> chunkKey0 --> XChaCha20-Poly1305(chunk0)
    +--HKDF-SHA256(salt, info="sorcha-chunk-1")--> chunkKey1 --> XChaCha20-Poly1305(chunk1)
    +--HKDF-SHA256(salt, info="sorcha-chunk-N")--> chunkKeyN --> XChaCha20-Poly1305(chunkN)
```

The master file key is then envelope-encrypted per recipient using the existing `EncryptionPipelineService` pattern (asymmetric wrap of 32-byte symmetric key).

**Alternatives considered**:
- **Single key for all chunks** (no HKDF): Simpler but reusing one key+random-nonce across many chunks increases nonce collision probability. With 4MB chunks and a 1GB file, that's 256 encryptions under the same key. XChaCha20's 24-byte nonce gives ~2^192 space so collision risk is negligible in practice, but per-chunk keys are a stronger security posture for zero additional cost.
- **AES-256-GCM instead of XChaCha20**: AES-GCM has a 12-byte nonce (2^96 space) which makes single-key multi-chunk more risky. Also, Sorcha's default encryption type is already XChaCha20-Poly1305 throughout the codebase (SymmetricCrypto, EncryptionPipelineService, PayloadManager).

---

## R3: File Chunking Patterns

**Decision**: Use a `Stream`-based approach with a pooled `byte[]` buffer via `ArrayPool<byte>`. Read chunks sequentially from the source stream. Handle remainder chunks naturally.

**Rationale**:

1. **Stream-based is the only viable option for large files**: `Memory<byte>` / `ReadOnlyMemory<byte>` require the entire file in memory first. For a 40MB file with 4MB chunks, the stream approach requires only one 4MB buffer at a time vs 40MB + 4MB for memory-based.

2. **`ArrayPool<byte>.Shared`**: Avoids GC pressure from allocating and discarding 4MB byte arrays per chunk. The pool reuses buffers across chunks.

3. **Remainder handling**: `Stream.ReadAsync` naturally returns fewer bytes on the last read. Track actual bytes read and slice the buffer accordingly.

```csharp
public static async IAsyncEnumerable<ChunkData> ChunkFileAsync(
    Stream source,
    int chunkSize = 4 * 1024 * 1024,  // 4MB default
    [EnumeratorCancellation] CancellationToken ct = default)
{
    var pool = ArrayPool<byte>.Shared;
    byte[] buffer = pool.Rent(chunkSize);
    try
    {
        int chunkIndex = 0;
        int bytesRead;
        while ((bytesRead = await ReadExactlyOrRemainderAsync(source, buffer, chunkSize, ct)) > 0)
        {
            // Copy only the bytes actually read (important for last chunk)
            var chunkBytes = new byte[bytesRead];
            Buffer.BlockCopy(buffer, 0, chunkBytes, 0, bytesRead);

            yield return new ChunkData(chunkIndex, chunkBytes, bytesRead);
            chunkIndex++;
        }
    }
    finally
    {
        pool.Return(buffer, clearArray: true);  // Clear for security
    }
}

// Reads exactly chunkSize bytes, or fewer only at end of stream
private static async Task<int> ReadExactlyOrRemainderAsync(
    Stream stream, byte[] buffer, int count, CancellationToken ct)
{
    int totalRead = 0;
    while (totalRead < count)
    {
        int read = await stream.ReadAsync(
            buffer.AsMemory(totalRead, count - totalRead), ct);
        if (read == 0) break;  // End of stream
        totalRead += read;
    }
    return totalRead;
}
```

Key design points:
- **`ReadExactlyOrRemainderAsync`**: A single `ReadAsync` call may return fewer bytes than requested even mid-stream (network streams, Blazor interop). Loop until we have a full chunk or hit EOF.
- **`clearArray: true`**: The buffer may contain plaintext file data. Clear on return to pool.
- **`IAsyncEnumerable<ChunkData>`**: Allows streaming processing -- encrypt and submit each chunk as it's read, without buffering all chunks.

**Alternatives considered**:
- **`Memory<byte>` slicing over pre-loaded array**: Would work for small files but defeats the purpose of chunking. The whole point is to avoid loading the entire file into memory.
- **`Pipe` / `System.IO.Pipelines`**: More complex, designed for network I/O scenarios with back-pressure. Overkill for sequential file chunking where we process one chunk at a time.
- **`Stream.ReadExactlyAsync` (.NET 7+)**: This method throws if the stream ends before filling the buffer, which is wrong for the last chunk. We need "read up to N bytes, return fewer at EOF" semantics.

---

## R4: Blazor WASM File Upload

**Decision**: Use the built-in `InputFile` component with `IBrowserFile`. Read files via `OpenReadStream()` in chunks for processing. Track progress manually via bytes-read counters. For files over ~50MB, consider a JS interop streaming approach.

**Rationale**:

### IBrowserFile API

Blazor provides `InputFile` (renders `<input type="file">`) which fires `InputFileChangeEventArgs` containing `IBrowserFile` instances:

```csharp
<InputFile OnChange="OnFileSelected" accept=".jpg,.png,.pdf" multiple />

@code {
    private async Task OnFileSelected(InputFileChangeEventArgs e)
    {
        // Single file
        IBrowserFile file = e.File;

        // Multiple files
        IReadOnlyList<IBrowserFile> files = e.GetMultipleFiles(maxAllowedFiles: 5);

        // Key properties:
        // file.Name         -- original filename
        // file.ContentType   -- MIME type
        // file.Size          -- size in bytes
        // file.LastModified   -- last modified date

        // Open a read stream (MUST specify maxAllowedSize for files > 512KB)
        using var stream = file.OpenReadStream(maxAllowedSize: 50 * 1024 * 1024);
    }
}
```

### Memory Constraints in WASM

Blazor WASM runs in a WebAssembly sandbox with these constraints:

- **Default WASM memory**: Browsers typically allow 256MB-2GB of WASM linear memory, but practical limits are lower due to GC pressure and fragmentation.
- **`OpenReadStream` is NOT in-memory**: It creates a JS interop stream that reads from the browser's File API. Data is transferred from JS to .NET in segments (default ~32KB SignalR/interop frames for Server, direct for WASM).
- **Safe to read 40MB files**: Yes, but do NOT buffer the entire file in a `MemoryStream`. Instead, read in chunks and process (encrypt + submit) each chunk before reading the next.
- **Existing pattern in codebase**: `FileRenderer.razor` (line 42-44) reads the entire file into `MemoryStream` with a 10MB limit. This must be replaced with chunked reading for the stored data feature.

### Upload Progress

Blazor has no built-in progress callback on `IBrowserFile.OpenReadStream()`. Implement progress tracking by wrapping the stream or counting bytes in the chunk loop:

```csharp
long totalRead = 0;
long totalSize = file.Size;

await foreach (var chunk in ChunkFileAsync(stream, chunkSize: 4_194_304, ct))
{
    // Encrypt and submit chunk...
    totalRead += chunk.BytesRead;

    double progress = (double)totalRead / totalSize * 100;
    await InvokeAsync(() =>
    {
        UploadProgress = progress;
        StateHasChanged();
    });
}
```

For finer-grained progress (within a chunk), use a `ProgressStream` wrapper:

```csharp
public class ProgressStream : Stream
{
    private readonly Stream _inner;
    private readonly Action<long> _onProgress;
    private long _totalRead;

    // Wrap file.OpenReadStream(), report bytes read via callback
    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer, CancellationToken ct = default)
    {
        int read = await _inner.ReadAsync(buffer, ct);
        _totalRead += read;
        _onProgress(_totalRead);
        return read;
    }
    // ... other Stream overrides delegate to _inner
}
```

**Alternatives considered**:
- **JS interop `fetch` with `ReadableStream`**: Would give native browser upload progress but bypasses Blazor's file handling, requires manual JS/C# bridge, and loses the benefit of client-side chunk encryption before upload.
- **SignalR streaming**: Possible for Blazor Server, but Sorcha.UI.Web.Client is Blazor WASM (client-side). File data goes from browser JS to WASM .NET, not via SignalR.
- **`HttpClient` with `StreamContent` + progress handler**: For the HTTP upload of encrypted chunks to the API, use `HttpClient` with a `ProgressMessageHandler` or manual `StreamContent`. This is the upload-to-server side, separate from the file-read side.

---

## R5: Camera Capture in Blazor WASM

**Decision**: Use standard HTML `<input type="file" accept="image/*" capture="environment">` via Blazor's `InputFile` component with additional HTML attributes. No Blazor-specific wrapper library needed.

**Rationale**:

The HTML `capture` attribute is a W3C standard (HTML Media Capture) supported by all modern mobile browsers. When present on a file input:
- `capture="environment"` -- opens the rear (world-facing) camera
- `capture="user"` -- opens the front (selfie) camera
- `capture` (no value) -- browser picks default camera

On desktop browsers, the `capture` attribute is ignored and the normal file picker opens. This is correct behaviour -- desktop users select files from disk.

Blazor's `InputFile` component renders a standard `<input type="file">`. Additional HTML attributes are passed through via Blazor's attribute splatting:

```razor
@* Camera capture for mobile, file picker for desktop *@
<InputFile OnChange="OnFileSelected"
           accept="image/jpeg,image/png"
           capture="environment"
           AdditionalAttributes="@_cameraAttrs" />

@code {
    // Alternative: use AdditionalAttributes for dynamic control
    private Dictionary<string, object> _cameraAttrs = new()
    {
        ["capture"] = "environment"
    };
}
```

However, `InputFile` does NOT directly support the `capture` attribute as a parameter. Two approaches:

**Approach A -- Dual inputs** (recommended for UX):
```razor
@* Separate buttons for clarity *@
<MudStack Row="true" Spacing="2">
    <MudButton OnClick="@(() => _fileInput?.Click())"
               StartIcon="@Icons.Material.Filled.UploadFile">
        Choose File
    </MudButton>
    <MudButton OnClick="@(() => _cameraInput?.Click())"
               StartIcon="@Icons.Material.Filled.PhotoCamera">
        Take Photo
    </MudButton>
</MudStack>

<InputFile @ref="_fileInput" OnChange="OnFileSelected"
           accept="image/jpeg,image/png,application/pdf"
           style="display:none" />

<InputFile @ref="_cameraInput" OnChange="OnFileSelected"
           accept="image/*" capture="environment"
           style="display:none" />
```

**Approach B -- JS interop** (if attribute splatting doesn't work):
```csharp
// Set the capture attribute via JS after render
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
        await JSRuntime.InvokeVoidAsync("setCaptureAttribute", _inputElementId);
}
```

```javascript
window.setCaptureAttribute = (elementId) => {
    const el = document.getElementById(elementId);
    if (el) el.setAttribute('capture', 'environment');
};
```

**Key behaviours**:
- iOS Safari: Opens camera directly, returns HEIC/JPEG
- Android Chrome: Opens camera app, returns JPEG
- Desktop Chrome/Firefox/Edge: Ignores `capture`, shows file picker
- `accept="image/*"` enables the camera option even without `capture` on some mobile browsers

**Alternatives considered**:
- **`MediaDevices.getUserMedia()` via JS interop**: Gives full camera control (preview, resolution settings) but is far more complex -- requires a `<video>` element, canvas capture, manual JPEG encoding. Overkill for simple photo capture.
- **Third-party Blazor camera components**: Several NuGet packages exist (e.g., `BlazorCamera`) but they add dependencies for trivial HTML functionality. The native `capture` attribute handles the use case with zero JS.

---

## R6: Streaming HTTP Response in Minimal APIs

**Decision**: Use `Results.Stream()` with a callback delegate for large file responses. Set `Content-Disposition` and `Content-Type` via the `httpContext.Response.Headers` or the `Results.Stream` overload parameters.

**Rationale**:

.NET Minimal APIs provide several file-serving result types. For chunked, streaming responses where data is assembled on-the-fly (decrypt chunks, reassemble, stream):

### Option 1: `Results.Stream()` with write callback (recommended)

```csharp
app.MapGet("/api/registers/{registerId}/files/{fileRefId}", async (
    string registerId,
    string fileRefId,
    IFileReassemblyService reassembly,
    HttpContext httpContext) =>
{
    var fileMeta = await reassembly.GetFileMetadataAsync(registerId, fileRefId);
    if (fileMeta is null)
        return Results.NotFound();

    return Results.Stream(
        streamWriterCallback: async (outputStream) =>
        {
            // Fetch, decrypt, and write each chunk sequentially
            await foreach (var decryptedChunk in reassembly.StreamDecryptedChunksAsync(
                registerId, fileRefId, httpContext.RequestAborted))
            {
                await outputStream.WriteAsync(decryptedChunk, httpContext.RequestAborted);
                await outputStream.FlushAsync(httpContext.RequestAborted);
            }
        },
        contentType: fileMeta.ContentType,
        fileDownloadName: fileMeta.FileName,
        lastModified: fileMeta.SealedAt,
        entityTag: null);
});
```

This overload:
- Sets `Content-Disposition: attachment; filename="..."` automatically via `fileDownloadName`
- Sets `Content-Type` from the `contentType` parameter
- Uses chunked transfer encoding (no `Content-Length` header) since total size is unknown at stream start
- The callback receives the raw response body stream -- write bytes directly

### Option 2: `Results.Stream()` with a pre-built `Stream`

If we can construct a readable `Stream` that decrypts on-read:

```csharp
return Results.Stream(
    stream: new ChunkReassemblyStream(registerId, fileRefId, reassembly),
    contentType: fileMeta.ContentType,
    fileDownloadName: fileMeta.FileName);
```

Less ideal because implementing a custom readable `Stream` with async chunk fetching is more complex than the callback approach.

### Option 3: `Results.File()` (existing pattern, NOT recommended for large files)

The codebase currently uses `Results.File(byte[], contentType, fileName)` in `Blueprint.Service/Program.cs:1222`. This loads the entire file into memory. Unacceptable for multi-MB files.

### Setting Content-Length (optional optimisation)

If the file size is known from metadata (sum of chunk sizes minus encryption overhead), set `Content-Length` to enable browser download progress:

```csharp
return Results.Stream(
    streamWriterCallback: async (outputStream) => { /* ... */ },
    contentType: fileMeta.ContentType,
    fileDownloadName: fileMeta.FileName);

// OR manually set Content-Length before streaming:
app.MapGet("/api/registers/{registerId}/files/{fileRefId}", async (
    string registerId, string fileRefId,
    IFileReassemblyService reassembly,
    HttpContext httpContext) =>
{
    var fileMeta = await reassembly.GetFileMetadataAsync(registerId, fileRefId);
    if (fileMeta is null) { httpContext.Response.StatusCode = 404; return; }

    httpContext.Response.ContentType = fileMeta.ContentType;
    httpContext.Response.Headers.ContentDisposition =
        $"attachment; filename=\"{fileMeta.FileName}\"";
    httpContext.Response.ContentLength = fileMeta.OriginalSize;

    await foreach (var chunk in reassembly.StreamDecryptedChunksAsync(
        registerId, fileRefId, httpContext.RequestAborted))
    {
        await httpContext.Response.Body.WriteAsync(chunk, httpContext.RequestAborted);
    }
});
```

**Alternatives considered**:
- **`Results.File(byte[], ...)` (current pattern)**: Loads entire file into memory. The existing usage at `Blueprint.Service/Program.cs:1222` works for small attachments but cannot scale to multi-MB stored data transactions.
- **`Results.File(string filePath, ...)` with temp file**: Would require writing decrypted data to a temp file first, introducing disk I/O latency and requiring cleanup. The streaming callback avoids this entirely.
- **`Results.Bytes()`**: Same as `Results.File(byte[])` -- full materialization in memory.
- **gRPC server streaming**: Would work for service-to-service, but browser clients need HTTP. The API Gateway (YARP) proxies HTTP, not gRPC streams, to the browser.

---

## Summary of Decisions

| Question | Decision | Key Dependency |
|----------|----------|---------------|
| HKDF | Built-in `System.Security.Cryptography.HKDF` | .NET 10 runtime (already present) |
| XChaCha20 + HKDF | Compatible, use HKDF for per-chunk keys | Existing `SymmetricCrypto` + `Sodium.Core` |
| File chunking | Stream-based with `ArrayPool<byte>` | No new dependency |
| Blazor file upload | `InputFile` + `IBrowserFile.OpenReadStream()` + manual progress | Built-in Blazor |
| Camera capture | HTML `capture="environment"` attribute on `InputFile` | Built-in HTML5 |
| Streaming response | `Results.Stream()` callback + Content-Disposition | Built-in Minimal APIs |
