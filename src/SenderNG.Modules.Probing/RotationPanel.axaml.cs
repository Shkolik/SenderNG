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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace SenderNG.Modules.Plugins;

public partial class RotationPanel : UserControl
{
    public RotationPanel() => InitializeComponent();

    /// <summary>Edge pictograms load on attach (not at XAML parse time): the
    /// unit tests construct this panel without an Avalonia app, and decoding an
    /// avares asset there would fail. The AB cell has two stacked pictures —
    /// plain and "extra probe point" — and the swap is a visibility binding on
    /// AddAction, so no reload is needed when the option toggles.</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RotationADImage.Source = Load("rotationAD.png");
        RotationCDImage.Source = Load("rotationCD.png");
        RotationABPlainImage.Source = Load("rotationAB.png");
        RotationABExtraImage.Source = Load("rotationABextra.png");
        RotationCBImage.Source = Load("rotationCB.png");
    }

    private static Bitmap Load(string name) =>
        new(AssetLoader.Open(new Uri($"avares://SenderNG.Modules.Probing/Assets/Probing/{name}")));
}