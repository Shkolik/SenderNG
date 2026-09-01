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

namespace CNC.Core.Services;

/// <summary>
/// A tool-table slot choice for controller tool selectors (the Work Parameters
/// strip and the Probe → Tool length picker): slot 0 renders as "None",
/// numbered slots as <c>T{id}</c> with the editor-local name appended in
/// parentheses when one is present (grblHAL has no name field; names live in
/// <see cref="OffsetsConfig.ToolNames"/>). A record so a ComboBox's
/// <c>SelectedItem</c> re-matches by value after the <c>ItemsSource</c> is
/// rebuilt (e.g. when a name is saved in the Offsets editor).
/// </summary>
public sealed record ToolSelectorItem(int Id, string Name)
{
    public string DisplayName =>
        Id == 0 ? "None" : string.IsNullOrWhiteSpace(Name) ? $"T{Id}" : $"T{Id} ({Name})";
}