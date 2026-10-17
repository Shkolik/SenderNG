#!/usr/bin/env python3
"""Placeholder sketches of machine axis ROLES for the machine-profile topology
picker.

Deliberately roles only - never axis letters: the letters are the controller's
naming (a remapped board reports XYUV, an XYZA board reports Z/A) and the axis
list below the picture is the truth. These are throwaway reference art; the
maintainer is drawing the real ones. Regenerate with:

    python tools/sketch-machine-topology.py

Output: src/SenderNG/Assets/machine-topology/machine-topology-{mill,foam,rotary}.png
        (app assets, loaded as avares://SenderNG/Assets/machine-topology/... - not
        docs/images, which is for README/architecture art)
"""

import math
import os

from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.normpath(
    os.path.join(HERE, "..", "src", "SenderNG", "Assets", "machine-topology"))

BG = (252, 252, 250)
INK = (38, 42, 50)
MUTED = (132, 138, 150)
FAINT = (206, 210, 220)
PLATE = (241, 244, 249)
C_LEN = (198, 74, 58)     # length role
C_HGT = (44, 112, 176)    # height role
C_WID = (34, 142, 92)     # width role
C_WIRE = (206, 58, 58)
C_ROT = (150, 92, 176)

_FONTS = {}


def F(size, bold=False):
    key = (size, bold)
    if key in _FONTS:
        return _FONTS[key]
    names = (
        ["segoeuib.ttf", "arialbd.ttf", "calibrib.ttf"]
        if bold
        else ["segoeui.ttf", "arial.ttf", "calibri.ttf"]
    )
    font = None
    for name in names:
        for base in (None, r"C:\Windows\Fonts"):
            try:
                path = name if base is None else os.path.join(base, name)
                font = ImageFont.truetype(path, size)
                break
            except OSError:
                continue
        if font:
            break
    if font is None:
        font = ImageFont.load_default()
    _FONTS[key] = font
    return font


def canvas(w=820, h=560):
    img = Image.new("RGB", (w, h), BG)
    return img, ImageDraw.Draw(img)


def title(d, main, sub):
    d.text((30, 24), main, font=F(21, True), fill=INK)
    d.text((30, 54), sub, font=F(14), fill=MUTED)


def caption(d, h, s):
    d.text((30, h - 34), s, font=F(13), fill=MUTED)


def iso(x, y, z, ox, oy, s):
    """Classic isometric: +width right-down, +length right-up, +height up."""
    return (ox + (x + y) * 0.8660254 * s, oy + (x - y) * 0.5 * s - z * s)


def polygon(d, pts, outline, fill=None, width=2):
    if fill:
        d.polygon(pts, fill=fill)
    for i in range(len(pts)):
        d.line([pts[i], pts[(i + 1) % len(pts)]], fill=outline, width=width)


def arrow(d, p0, p1, color, w=3, head=13):
    d.line([p0, p1], fill=color, width=w)
    ang = math.atan2(p1[1] - p0[1], p1[0] - p0[0])
    for sgn in (-1, 1):
        a = ang + sgn * math.radians(152)
        d.line([p1, (p1[0] + math.cos(a) * head, p1[1] + math.sin(a) * head)],
               fill=color, width=w)


def dashed(d, p0, p1, color, w=2, on=7, off=6):
    dx, dy = p1[0] - p0[0], p1[1] - p0[1]
    dist = math.hypot(dx, dy)
    if dist < 1e-6:
        return
    ux, uy = dx / dist, dy / dist
    t = 0.0
    while t < dist:
        e = min(t + on, dist)
        d.line([(p0[0] + ux * t, p0[1] + uy * t),
                (p0[0] + ux * e, p0[1] + uy * e)], fill=color, width=w)
        t = e + off


def txt(d, xy, s, color=INK, size=14, anchor="lm", bold=False):
    d.text(xy, s, font=F(size, bold), fill=color, anchor=anchor)


def dot(d, p, color, r=4):
    d.ellipse([p[0] - r, p[1] - r, p[0] + r, p[1] + r], fill=color)


# --------------------------------------------------------------------------
# Mill: one carriage plane (width x length) + the depth role (height).
# --------------------------------------------------------------------------
def mill():
    img, d = canvas()
    title(d, "Mill", "base topology: 3 axes, one plane + depth")
    ox, oy, s = 168, 392, 92

    # Floor plate = the carriage plane the two in-plane roles span.
    polygon(d, [iso(0, 0, 0, ox, oy, s), iso(2.5, 0, 0, ox, oy, s),
                iso(2.5, 2.9, 0, ox, oy, s), iso(0, 2.9, 0, ox, oy, s)],
            FAINT, fill=PLATE, width=2)

    # +Z role first so the in-plane arrows sit on top of it.
    arrow(d, iso(0, 0, 0, ox, oy, s), iso(0, 0, 1.95, ox, oy, s), C_HGT)
    arrow(d, iso(0, 0, 0, ox, oy, s), iso(2.05, 0, 0, ox, oy, s), C_WID)
    arrow(d, iso(0, 0, 0, ox, oy, s), iso(0, 2.45, 0, ox, oy, s), C_LEN)

    txt(d, (ox + 16, iso(0, 0, 2.1, ox, oy, s)[1]), "height  ·  3rd", C_HGT, 15, bold=True)
    txt(d, (iso(2.15, 0, 0, ox, oy, s)[0] + 6, iso(2.15, 0, 0, ox, oy, s)[1] + 16),
        "width  ·  1st", C_WID, 15, bold=True)
    txt(d, (iso(0, 2.55, 0, ox, oy, s)[0] + 4, iso(0, 2.55, 0, ox, oy, s)[1] - 16),
        "length  ·  2nd", C_LEN, 15, bold=True)

    # Left of the origin is the only corner free of all three arrows.
    txt(d, (ox - 12, oy + 8), "machine origin", MUTED, 12, anchor="rm")
    txt(d, (ox + 300, oy + 8), "the plane the two in-plane roles span", MUTED, 12, anchor="mm")

    caption(d, img.height,
            "Roles only, no axis letters \u2014 letters come from the controller on connect. "
            "Optional axes are appended below the base ones.")
    return img


# --------------------------------------------------------------------------
# Foam: two parallel planes, two roles each, separated along the third role.
# Oblique on purpose: both towers face the viewer as plain rectangles, so the
# separation is the only receding axis and no two labels compete for space.
# --------------------------------------------------------------------------
def foam():
    img, d = canvas(h=620)
    title(d, "Foam", "base topology: 4 axes, two parallel planes, 2 axes each")

    x0, y0 = 150, 380        # tower 1 bottom-left, in pixels
    LEN, HGT = 160, 190      # the two in-plane roles
    SX, SY = 205, -100       # tower-to-tower offset (the third role)

    def t1(dx=0, dy=0):
        return (x0 + dx, y0 + dy)

    def t2(dx=0, dy=0):
        return (x0 + SX + dx, y0 + SY + dy)

    def face(f):
        return [f(0, 0), f(LEN, 0), f(LEN, -HGT), f(0, -HGT)]

    r1, r2 = face(t1), face(t2)

    # The receding top face makes the pair read as one block, not two panes.
    polygon(d, [t1(0, -HGT), t1(LEN, -HGT), t2(LEN, -HGT), t2(0, -HGT)],
            FAINT, fill=(245, 241, 236), width=1)
    for r in (r1, r2):
        polygon(d, r, INK, fill=(250, 250, 248), width=3)
    for k in range(4):
        dashed(d, r1[k], r2[k], FAINT, 1)

    # Both in-plane roles, once per tower - the whole point of this type.
    for (f, n_len, n_hgt, n_plane, side) in (
            (t1, "1st", "2nd", 1, -1), (t2, "3rd", "4th", 2, 1)):
        o = f(10, -10)
        arrow(d, o, (o[0] + LEN - 20, o[1]), C_LEN, w=3, head=11)
        arrow(d, o, (o[0], o[1] - HGT + 20), C_HGT, w=3, head=11)
        txt(d, (o[0] + (LEN - 20) / 2, o[1] + 20), f"length  \u00b7  {n_len}", C_LEN, 14,
            anchor="mm", bold=True)
        txt(d, (o[0] - 14 if side < 0 else o[0] + LEN + 8, o[1] - (HGT - 20) / 2),
            f"height  \u00b7  {n_hgt}", C_HGT, 14, anchor="rm" if side < 0 else "lm", bold=True)
        txt(d, (o[0] + (LEN - 20) / 2, o[1] - HGT + 60), f"Plane {n_plane}", MUTED, 14,
            anchor="mm")

    # One wire, corresponding points: what the two planes actually steer.
    wa, wb = t1(LEN / 2, -70), t2(LEN / 2, -70)
    d.line([wa, wb], fill=C_WIRE, width=3)
    txt(d, ((wa[0] + wb[0]) / 2, (wa[1] + wb[1]) / 2 - 30), "wire", C_WIRE, 14,
        anchor="mm", bold=True)

    # Plane-to-plane distance, dimensioned parallel to the separation role.
    da, db = t1(LEN, 40), t2(LEN, 40)
    arrow(d, da, db, C_WID, w=2, head=10)
    arrow(d, db, da, C_WID, w=2, head=10)
    txt(d, (da[0], da[1] + 28), "plane-to-plane distance", C_WID, 14, bold=True)
    txt(d, (da[0], da[1] + 48), "(wire length) \u2014 profile value", MUTED, 12)

    caption(d, img.height,
            "Each tower owns its own pair of the same two roles, which is what lets the "
            "wire tilt. Axis order is set per profile.")
    return img


# --------------------------------------------------------------------------
# Optional rotary: a role the axis turns ABOUT, plus its pivot in machine coords.
# --------------------------------------------------------------------------
def rotary():
    img, d = canvas()
    title(d, "Optional rotary axis", "appended below the base axes: kind = rotary")
    ox, oy, s = 190, 400, 88

    def c(x, y, z):
        return iso(x, y, z, ox, oy, s)

    polygon(d, [c(0, 0, 0), c(2.6, 0, 0), c(2.6, 2.6, 0), c(0, 2.6, 0)],
            FAINT, fill=PLATE, width=2)
    arrow(d, c(0, 0, 0), c(2.2, 0, 0), C_WID)
    arrow(d, c(0, 0, 0), c(0, 2.2, 0), C_LEN)
    arrow(d, c(0, 0, 0), c(0, 0, 1.5), C_HGT)

    # A rotary table: circle in the base plane, turning about the height role.
    cx, cy, r = 1.35, 1.25, 0.72
    ctr = c(cx, cy, 0)
    d.ellipse([ctr[0] - r * s * 1.2, ctr[1] - r * s * 0.7,
               ctr[0] + r * s * 1.2, ctr[1] + r * s * 0.7],
              outline=C_ROT, width=3)

    # The rotation AXIS is the role's direction; the pivot is a machine point.
    axis_lo, axis_hi = c(cx, cy, -0.25), c(cx, cy, 1.45)
    dashed(d, axis_lo, axis_hi, C_ROT, 2, on=8, off=6)
    dot(d, ctr, C_ROT, 5)

    # Curved arrow about the axis. The role only fixes the LINE it turns about;
    # where that line sits is the machine-coordinate pivot marked above.
    arx, ary = 40, 23
    acx, acy = ctr[0], ctr[1] - 26
    d.arc([acx - arx, acy - ary, acx + arx, acy + ary], start=200, end=340,
          fill=C_ROT, width=3)
    end = math.radians(340)
    tip = (acx + arx * math.cos(end), acy + ary * math.sin(end))
    tang = math.atan2(math.cos(end), -math.sin(end))
    for sgn in (-1, 1):
        a = tang + sgn * math.radians(150)
        d.line([tip, (tip[0] + math.cos(a) * 11, tip[1] + math.sin(a) * 11)],
               fill=C_ROT, width=3)

    txt(d, (axis_hi[0] + 16, axis_hi[1] + 4), "rotary  \u00b7  about height", C_ROT, 15,
        bold=True)
    txt(d, (axis_hi[0] + 16, axis_hi[1] + 24), "(points up)", MUTED, 12)
    txt(d, (ctr[0] + 86, ctr[1] + 6), "rotation origin (machine coords)", C_ROT, 13,
        bold=True)
    txt(d, (c(2.3, 0, 0)[0] + 8, c(2.3, 0, 0)[1] + 16), "width  \u00b7  1st", C_WID, 14,
        bold=True)
    txt(d, (c(0, 1.15, 0)[0] + 22, c(0, 1.15, 0)[1] + 31), "length  \u00b7  2nd", C_LEN, 14,
        anchor="rm", bold=True)
    txt(d, (ox - 10, c(0, 0, 1.5)[1] + 4), "height  \u00b7  3rd", C_HGT, 14, anchor="rm",
        bold=True)

    caption(d, img.height,
            "A rotary's role names the direction it turns about; the pivot is stored in "
            "machine coordinates.")
    return img


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    for name, fn in (("mill", mill), ("foam", foam), ("rotary", rotary)):
        path = os.path.join(OUT_DIR, f"machine-topology-{name}.png")
        fn().save(path)
        print("wrote", path)


if __name__ == "__main__":
    main()
