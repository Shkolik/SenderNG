using SenderNG.Avalonia.Modules.Fixed;

namespace SenderNG.Core.Tests;

public class ProgramViewModelTests
{
    [Fact]
    public void LoadGCode_fills_program_rows()
    {
        var vm = new ProgramViewModel();

        vm.LoadGCode("G0 X0\nG0 Y0\r\nM30");

        Assert.Equal(3, vm.ProgramRows.Count);
        Assert.Equal(new GCodeProgramRow(1, "·", "G0 X0"), vm.ProgramRows[0]);
        Assert.Equal(new GCodeProgramRow(2, "·", "G0 Y0"), vm.ProgramRows[1]);
        Assert.Equal(new GCodeProgramRow(3, "·", "M30"), vm.ProgramRows[2]);
    }

    [Fact]
    public void LoadGCode_preserves_empty_lines()
    {
        var vm = new ProgramViewModel();

        vm.LoadGCode("G0 X0\n\nM30");

        Assert.Equal(3, vm.ProgramRows.Count);
        Assert.Equal(string.Empty, vm.ProgramRows[1].Data);
        Assert.Equal(2, vm.ProgramRows[1].Line);
    }

    [Fact]
    public void LoadGCode_clears_previous_rows()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X0\nG0 Y0");

        vm.LoadGCode("M2");

        Assert.Single(vm.ProgramRows);
        Assert.Equal("M2", vm.ProgramRows[0].Data);
    }

    [Fact]
    public void LoadGCode_with_empty_text_clears_rows()
    {
        var vm = new ProgramViewModel();
        vm.LoadGCode("G0 X0");

        vm.LoadGCode(string.Empty);

        Assert.Empty(vm.ProgramRows);

        vm.LoadGCode(null!);

        Assert.Empty(vm.ProgramRows);
    }
}
