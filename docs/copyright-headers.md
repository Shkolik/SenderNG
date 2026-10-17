# Copyright headers

SenderNG is a fork of [ioSender](https://github.com/terjeio/ioSender) by Io Engineering
(Terje Io), licensed under BSD 3-Clause. Because the codebase mixes ioSender-derived code
with new SenderNG code, every source file carries a copyright header that reflects **who
authored it**. This document defines the three header forms and the rule for choosing one.

**Rule of thumb: only completely new code carries only the SenderNG author's name.
Anything derived from ioSender also retains ioSender's copyright notice**, because BSD 3-Clause
condition 1 requires that redistributed source retain the original notice.

## The three categories

| Category | Header | When it applies |
| ---------- | -------- | ----------------- |
| Mine only | `Copyright (c) 2026 Andrew Shkolik` only | **Completely new** SenderNG code with no ioSender antecedent. |
| Double | ioSender + Andrew | Derived from ioSender (a control or feature ported over) but substantially rewritten for SenderNG's architecture. |
| Untouched | Original ioSender header only | Near-verbatim carries of ioSender source into the repo (engine files). These are **not** rewritten — leave their header exactly as upstream wrote it. |

### Mine only

Applies to new architecture and new features that do not mirror an ioSender control, e.g.
the module system (`*Module.cs` orchestrators), the app settings dialog, new settings/config
stores, and the app shell. If the file's behavior is a port of an ioSender widget, it is
**not** mine-only even if the implementation is a from-scratch MVVM rewrite.

### Double

Applies to widget implementations whose behavior maps onto an ioSender control, even when
rewritten for SenderNG's Avalonia + MVVM structure. Examples (each ported from its ioSender
counterpart):

- Offsets editor ← `OffsetView` · Work Parameters ← `WorkParametersControl`
- DRO ← `DROControl` · Console ← `ConsoleControl`
- GRBL Settings ← `GrblConfigControl` · Program ← `GCodeListControl`/`JobControl`

The ViewModel **and** its view (`*.axaml` and `*.axaml.cs`) both carry the double header
when the view layout is itself ported from the ioSender WPF XAML. A view whose layout is a
fresh Avalonia build — same behavior, different markup structure — is new code and carries
the mine-only header: the plugin `*View.axaml` files fall in this bucket, so only their
ViewModels (the ported logic) are double.

### Untouched
Files carried over essentially verbatim from ioSender (e.g. `src/SenderNG.Core/GCodeParser.cs`).
These already have ioSender's full original header. **Do not add or edit
anything** — the upstream notice is already correct and must not be touched.

## Deciding a new file

Ask: does this file's behavior exist in ioSender?

1. **Yes, and it's essentially the same code I copied** → untouched (keep ioSender header).
2. **Yes, but I rewrote it** (new architecture, MVVM, different framework) → **double**.
3. **No, this is new** → **mine only**.

When in doubt, prefer **double** over mine-only: BSD condition 1 requires retaining the
upstream notice, and it is safer to over-attribute than to strip ioSender's name from a
derivative.

## Header forms

Headers go at the **very top** of the file. Keep the file's existing line-ending convention
(source files in this repo are LF; docs are mixed).

### Mine only — `.cs`

```csharp
/*
 * <filename> - <short description>
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
```

### Double — `.cs`

Same as above with both copyrights on their own lines:

```csharp
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
```

The two copyright lines are each on **their own row with a blank line between them**, and a
blank line between Andrew's line and `All rights reserved.`

### `.axaml` view layouts

XML files cannot host a `/* */` block, so the view layout header is an XML comment. It uses
the same attribution lines followed by the same BSD body as the `.cs` form above:
```xml
<!--
<filename>.axaml - <short description>

Copyright (c) 2018-2022 Io Engineering (Terje Io)

Copyright (c) 2026 Andrew Shkolik

All rights reserved.
... (same BSD body as the `.cs` form) ...
-->
```

Mine-only `.axaml` files use the same form with only the Andrew line.

## Repo-wide license note

The root `LICENSE` already lists both copyright holders (`Copyright (c) 2019-2023, Io
Engineering (Terje Io)` and `Copyright (c) 2024-2026, Andrew Shkolik`). The per-file headers
here are the granular, per-author attribution required by BSD condition 1 when source is
redistributed.
