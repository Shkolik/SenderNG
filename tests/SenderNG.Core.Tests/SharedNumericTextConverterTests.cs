/*
 * SharedNumericTextConverterTests.cs - pins the linked plugin copy of the numeric converter
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

using System.Globalization;
using Avalonia.Data;
using Xunit;

namespace SenderNG.Core.Tests;

public class SharedNumericTextConverterTests
{
    [Fact]
    public void Shared_copy_formats_and_rejects_like_the_app_copy()
    {
        var conv = SenderNG.Modules.Shared.Converters.Converters.NumericText;
        var inv = CultureInfo.InvariantCulture;

        Assert.Equal("1.5", conv.Convert(1.5, typeof(string), null, inv));
        Assert.Equal("42", conv.Convert(42, typeof(string), null, inv));
        Assert.Equal(BindingOperations.DoNothing, conv.ConvertBack("abc", typeof(double), null, inv));
        Assert.Equal(1.5, conv.ConvertBack("1.5", typeof(double), null, inv));
        Assert.Equal(42, conv.ConvertBack("42", typeof(int), null, inv));
    }
}
