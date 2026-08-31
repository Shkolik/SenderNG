/*
 * ProgramViewModel.cs - Program view model
 */

/*

Copyright (c) 2018-2022 Io Engineering (Terje Io)

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

using System.Collections.ObjectModel;
using CNC.Core.Services;
using SenderNG.ViewModels;

namespace SenderNG.Modules.Fixed;

public record GCodeProgramRow(int Line, string Status, string Data);

public record ProgramLimitRow(string Letter, double Min, double Max);

public class ProgramViewModel : ViewModelBase
{
    public ObservableCollection<GCodeProgramRow> ProgramRows { get; } = new();

    public ObservableCollection<ProgramLimitRow> LimitRows { get; } = new();

    public bool HasLimits => LimitRows.Count > 0;

    /// <summary>Raw program text as loaded (transforms such as height map/rotation rewrite it).</summary>
    public string? LoadedText { get; private set; }

    public void LoadGCode(string? text)
    {
        LoadedText = text;
        ProgramRows.Clear();
        LimitRows.Clear();
        OnPropertyChanged(nameof(HasLimits));
        if (string.IsNullOrWhiteSpace(text))
            return;

        int n = 1;
        foreach (var line in text.Split(["\r\n", "\n"], StringSplitOptions.None))
        {
            var data = line.TrimEnd('\r');
            // Status: · idle, ✓ done, ✕ error — placeholder · for now
            ProgramRows.Add(new GCodeProgramRow(n++, "·", data));
        }

        var limits = GCodeLimitsCalculator.Compute(text);
        if (limits is not null)
        {
            foreach (var l in limits)
                LimitRows.Add(new ProgramLimitRow(l.Letter.ToString(), l.Min, l.Max));
            OnPropertyChanged(nameof(HasLimits));
        }
    }

    /// <summary>Reset the loaded program to the start: all rows return to the un-sent state.</summary>
    public void Rewind()
    {
        for (var i = 0; i < ProgramRows.Count; i++)
            ProgramRows[i] = ProgramRows[i] with { Status = "·" };
    }
}
