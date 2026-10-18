/*
 * ColorLiteralScanner.cs - finds raw color literals in view AXAML
 */

/*

Copyright (c) 2026 Andrew Shkolik
All rights reserved.

Redistribution and use in source and binary forms, with or without modification,
are permitted provided that the following conditions are met:

· Redistributions of source code must retain the above copyright notice, this
list of conditions and the following disclaimer.

· Redistributions in binary form must reproduce the above copyright notice, this
list of conditions and the following disclaimer in the documentation and/or
other materials provided with the distribution.

· Neither the name of the copyright holder nor the names of its contributors may
be used to endorse or promote products derived from this software without
specific prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

*/

using System.Text.RegularExpressions;

namespace SenderNG.Tests;

/// <summary>Finds raw color literals in view AXAML (hex or a named color used as
/// an attribute value), ignoring XML comments. Transparent is allowed.</summary>
internal static class ColorLiteralScanner
{
    private static readonly Regex Hex = new(
        @"(?<![\w#&])#[0-9a-fA-F]{3,8}\b", RegexOptions.Compiled);

    private static readonly Regex Named = new(
        "\"(Black|White|Gray|LightGray|DarkGray|Silver|Red|Green|Blue|Yellow|Orange|Pink|Purple|Cyan|Magenta|Brown)\"",
        RegexOptions.Compiled);

    private static readonly Regex XmlComment = new(
        "<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    public static IReadOnlyList<string> Find(string xamlText)
    {
        var stripped = XmlComment.Replace(xamlText, string.Empty);
        var found = new List<string>();
        found.AddRange(Hex.Matches(stripped).Select(m => m.Value));
        found.AddRange(Named.Matches(stripped).Select(m => m.Groups[1].Value));
        return found;
    }
}
