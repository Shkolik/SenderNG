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

using Xunit;

namespace SenderNG.Tests;

/// <summary>
/// Runs the <c>LocalizationState</c> collection on its own, after every parallel
/// collection has finished.
/// </summary>
/// <remarks>
/// <para>
/// <c>LocalizationService.Current</c> is a process-global mutable static
/// (<c>LocalizationService.cs:101</c>) and <c>LocText</c> resolves through it at
/// call time. The shell tests in this assembly therefore both read and write one
/// shared locale: several set <c>Current</c> to a catalog instance and call
/// <c>SetLocale("ru")</c>, while others set <c>Current = null</c> to pin the
/// English fallback and assert on English text.
/// </para>
/// <para>
/// Those two kinds of test must not run concurrently. When they did,
/// <c>AboutDialogModelTests.Copy_info_second_copy_within_window_is_not_reverted_by_first_timer</c>
/// failed under load with <c>Expected: "Copy info" / Actual: "Скопировать инфо"</c>:
/// a parallel test had swapped the ambient locale mid-test, so the final read of
/// <c>CopyButtonText</c> resolved Russian. It passed in isolation and failed in
/// bulk, exactly like the <c>LegacyStatics</c> hazard documented in
/// <c>SenderNG.Core.Tests/LegacyStaticsCollection.cs</c>.
/// </para>
/// <para>
/// Marking this collection non-parallel closes the window for the present
/// callers and for any future one: xUnit runs a non-parallel collection only
/// after all parallel collections have completed, so nothing can interleave.
/// Joining the collection by hand would have to be repeated for every new locale
/// test — the same trap the <c>LegacyStatics</c> note calls out — so the classes
/// that touch the ambient locale belong here as a rule, not as a favour.
/// </para>
/// </remarks>
[CollectionDefinition("LocalizationState", DisableParallelization = true)]
public sealed class LocalizationStateCollection;
