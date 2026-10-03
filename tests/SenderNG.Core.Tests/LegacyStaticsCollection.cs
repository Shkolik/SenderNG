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

namespace SenderNG.Core.Tests;

/// <summary>
/// Runs the <c>LegacyStatics</c> collection on its own, after every parallel
/// collection has finished.
/// </summary>
/// <remarks>
/// <para>
/// <c>Machine.WorkParametersProvider</c> (Machine.cs:236) is a process-global
/// mutable static. The production host writes it from the
/// <c>MainViewModel</c> constructor (MainViewModel.cs:274) and clears it from
/// <c>Dispose</c> (MainViewModel.cs:1392), so <em>any</em> test class that
/// builds a <c>MainViewModel</c> mutates it as a side effect.
/// </para>
/// <para>
/// The classes that depend on owning that static - the emulator bridge, the
/// toolpath builder, and the dispose regression - share the
/// <c>LegacyStatics</c> collection, which serializes them against each other.
/// That is not sufficient on its own: the other <c>MainViewModel</c>-building
/// classes sit in their own implicit collections and run concurrently, so they
/// could overwrite or null the static between a test seeding it and the
/// emulator snapshotting it. A racing null makes the emulator seed from an
/// empty table, which surfaces as
/// <c>EmulatorWorkParametersBridgeTests.PRB_expressions_read_the_seeded_probe_result</c>
/// failing with <c>Expected: 1, Actual: 0</c>.
/// </para>
/// <para>
/// Marking the collection non-parallel closes that window for the present
/// callers and for any future one: xUnit runs a non-parallel collection only
/// after all parallel collections have completed, so nothing can interleave.
/// The alternative - adding every <c>MainViewModel</c>-building class to this
/// collection by hand - fixes today's callers but silently reopens the race
/// for the next class someone writes.
/// </para>
/// </remarks>
[CollectionDefinition("LegacyStatics", DisableParallelization = true)]
public sealed class LegacyStaticsCollection;