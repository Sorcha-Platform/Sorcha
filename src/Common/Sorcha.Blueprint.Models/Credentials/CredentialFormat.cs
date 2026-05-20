// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// Wire format of a verifiable credential. Selects which credential-format handler
/// issues, presents, and verifies a credential (feature 135).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CredentialFormat
{
    /// <summary>SD-JWT VC (RFC 9901 + SD-JWT VC profile). The default Sorcha format.</summary>
    [JsonStringEnumMemberName("sd-jwt-vc")]
    SdJwtVc = 0,

    /// <summary>ISO/IEC 18013-5 mobile document, CBOR/COSE encoded (EUDI online path, OpenID4VP).</summary>
    [JsonStringEnumMemberName("mso_mdoc")]
    MsoMdoc = 1
}
