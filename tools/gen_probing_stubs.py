"""Generate stub center-finder pictograms (120x58) in the ioSender gray-on-white
style. Placeholders until the real artwork arrives; same sizes as the buttons."""
from PIL import Image, ImageDraw

W, H = 120, 58
STROKE = (99, 99, 99, 255)
DASH = (150, 150, 150, 255)
LW = 2


def arrow(d, tip, dx, dy, length):
    """Arrow with tip at (tip), pointing (dx, dy), tail `length` back."""
    x0, y0 = tip[0] - dx * length, tip[1] - dy * length
    d.line([(x0, y0), tip], fill=STROKE, width=LW)
    hl = 6.0
    px, py = -dy, dx  # perpendicular unit
    d.line([(tip[0] - dx * hl - px * hl / 2, tip[1] - dy * hl - py * hl / 2), tip], fill=STROKE, width=LW)
    d.line([(tip[0] - dx * hl + px * hl / 2, tip[1] - dy * hl + py * hl / 2), tip], fill=STROKE, width=LW)


def dashed_v(d, x, y0, y1, seg=4, gap=3):
    y = y0
    while y < y1:
        d.line([(x, y), (x, min(y + seg, y1))], fill=DASH, width=1)
        y += seg + gap


def dashed_h(d, y, x0, x1, seg=4, gap=3):
    x = x0
    while x < x1:
        d.line([(x, y), (min(x + seg, x1), y)], fill=DASH, width=1)
        x += seg + gap


def pocket(d):
    d.rectangle([12, 12, 108, 46], outline=STROKE, width=LW)


def workpiece(d):
    d.rectangle([38, 16, 82, 42], outline=STROKE, width=LW)


def save(im, name):
    im.save(f"src/SenderNG.Modules.Probing/Assets/Probing/{name}.png")
    print(name, im.size)


def new_canvas():
    im = Image.new("RGBA", (W, H), (255, 255, 255, 255))
    return im, ImageDraw.Draw(im)


# X inside: pocket, arrows from the middle toward the side walls.
im, d = new_canvas()
pocket(d)
arrow(d, (24, 29), -1, 0, 22)
arrow(d, (96, 29), 1, 0, 22)
dashed_v(d, 60, 16, 42)
save(im, "centerXIn")

# X outside: workpiece, arrows from outside pointing at the side walls.
im, d = new_canvas()
workpiece(d)
arrow(d, (34, 29), 1, 0, 24)
arrow(d, (86, 29), -1, 0, 24)
dashed_v(d, 60, 16, 42)
save(im, "centerXOut")

# Y inside: pocket, arrows from the middle toward the top/bottom walls.
im, d = new_canvas()
pocket(d)
arrow(d, (60, 18), 0, -1, 10)
arrow(d, (60, 42), 0, 1, 10)
dashed_h(d, 29, 16, 104)
save(im, "centerYIn")

# Y outside: workpiece, arrows from outside pointing at the top/bottom walls.
im, d = new_canvas()
workpiece(d)
arrow(d, (60, 12), 0, 1, 8)
arrow(d, (60, 46), 0, -1, 8)
dashed_h(d, 29, 16, 104)
save(im, "centerYOut")