#!/usr/bin/env python3
"""Draws the festival backgrounds (docs/06, Backgrounds).

Writes src/FalconNotes.UI/wwwroot/img/backgrounds/{name}-{light|dark}.svg: one seamless 480 px tile per background and
per theme, which app.css repeats behind the page. The drawings are original to Falcon Notes: every shape is written
out below, and nothing is copied or traced from anywhere else, so they need no third-party notice.

    python3 scripts/make-backgrounds.py                 # write the tiles
    python3 scripts/make-backgrounds.py --preview x.svg # also write a contact sheet of them all, to look at

Run it again after changing a drawing, and commit the tiles with the change.
"""
import math
import pathlib
import sys

SIZE = 480
ROOT = pathlib.Path(__file__).resolve().parent.parent
OUT = ROOT / "src" / "FalconNotes.UI" / "wwwroot" / "img" / "backgrounds"


# --- Building blocks ------------------------------------------------------------------------------------------------

def fill(d, colour):
    return f'<path d="{d}" fill="{colour}"/>'


def line(d, colour, width=1.6):
    return f'<path d="{d}" fill="none" stroke="{colour}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>'


def circle(x, y, r, colour):
    return f'<circle cx="{x}" cy="{y}" r="{r}" fill="{colour}"/>'


def ellipse(x, y, rx, ry, colour, rot=0):
    turn = f' transform="rotate({rot} {x} {y})"' if rot else ""
    return f'<ellipse cx="{x}" cy="{y}" rx="{rx}" ry="{ry}" fill="{colour}"{turn}/>'


def turned(inner, degrees):
    return f'<g transform="rotate({degrees})">{inner}</g>'


def n(value):
    return f"{value:.2f}".rstrip("0").rstrip(".")


def polar(radius, degrees):
    a = math.radians(degrees - 90)
    return radius * math.cos(a), radius * math.sin(a)


def star(colour, points=5, outer=10.0, inner=4.2):
    path = []
    for i in range(points * 2):
        x, y = polar(outer if i % 2 == 0 else inner, i * 180 / points)
        path.append(f'{"M" if i == 0 else "L"}{n(x)},{n(y)}')
    return fill(" ".join(path) + " Z", colour)


def sparkle(colour, size=7.0):
    """A four-pointed twinkle with curved sides."""
    s, c = size, size * 0.22
    return fill(f"M0,{-s} C{c},{-c} {c},{-c} {s},0 C{c},{c} {c},{c} 0,{s} C{-c},{c} {-c},{c} {-s},0 C{-c},{-c} {-c},{-c} 0,{-s} Z", colour)


def crescent(colour, radius=22.0, bite=18.0, dx=8.0, dy=-3.0):
    """A moon: a disc with a smaller disc taken out of it."""
    d = math.hypot(dx, dy)
    a = (radius * radius - bite * bite + d * d) / (2 * d)
    h = math.sqrt(max(radius * radius - a * a, 0))
    ux, uy = dx / d, dy / d
    x1, y1 = a * ux - h * uy, a * uy + h * ux
    x2, y2 = a * ux + h * uy, a * uy - h * ux
    return fill(f"M{n(x1)},{n(y1)} A{radius},{radius} 0 1 1 {n(x2)},{n(y2)} A{bite},{bite} 0 1 0 {n(x1)},{n(y1)} Z", colour)


def burst(colour, tips, rays=12, near=7.0, far=22.0, width=2.0):
    """A firework: rays from the middle, every other one shorter, each with a spark beyond its tip."""
    parts = []
    for i in range(rays):
        reach = far if i % 2 == 0 else far * 0.68
        x1, y1 = polar(near, i * 360 / rays)
        x2, y2 = polar(reach, i * 360 / rays)
        x3, y3 = polar(reach + 5.5, i * 360 / rays)
        parts.append(line(f"M{n(x1)},{n(y1)} L{n(x2)},{n(y2)}", colour, width))
        parts.append(circle(n(x3), n(y3), 1.5 if i % 2 == 0 else 1.1, tips))
    return "".join(parts) + circle(0, 0, 2.4, tips)


def flower(petal, heart, petals=5, reach=9.0, size=6.2, core=3.6):
    parts = [circle(*map(n, polar(reach, i * 360 / petals)), size, petal) for i in range(petals)]
    return "".join(parts) + circle(0, 0, core, heart)


def rosette(outer, inner, heart, dots):
    """A rangoli: two rings of petals, a centre, and a ring of dots."""
    parts = [turned(ellipse(0, -15, 5, 9.5, outer), i * 45) for i in range(8)]
    parts += [turned(ellipse(0, -9, 3.6, 6.5, inner), i * 45 + 22.5) for i in range(8)]
    parts += [circle(*map(n, polar(27, i * 45 + 22.5)), 1.5, dots) for i in range(8)]
    return "".join(parts) + circle(0, 0, 4.2, heart)


# --- Halloween ------------------------------------------------------------------------------------------------------

def pumpkin(skin, rib, stem, glow):
    return (fill("M-2,-17 C-3,-24 1,-30 8,-31 L10,-26 C6,-25 4,-22 4,-16 Z", stem)
            + ellipse(-13, 2, 15, 19, skin) + ellipse(13, 2, 15, 19, skin) + ellipse(0, 2, 12.5, 21, skin)
            + line("M-7,-17 C-13,-7 -13,12 -7,22 M7,-17 C13,-7 13,12 7,22", rib, 1.5)
            + fill("M-15,-2 L-9,-10 L-4,-2 Z M4,-2 L9,-10 L15,-2 Z M-2,4 L0,0 L2,4 Z", glow)
            + fill("M-16,7 C-8,11 8,11 16,7 C12,16 5,19 0,19 C-5,19 -12,16 -16,7 Z", glow)
            + f'<rect x="-8" y="8" width="4" height="4" rx="0.6" fill="{skin}"/><rect x="3" y="13" width="4" height="4.6" rx="0.6" fill="{skin}"/>')


def bat(colour):
    return fill("M0,-4 C-2,-9 -5,-12 -6,-13 C-6,-9 -7,-7 -8,-5 C-14,-12 -24,-14 -36,-8 C-30,-6 -27,-1 -27,4 C-23,1 -19,1 -16,5 "
                "C-13,2 -9,2 -6,7 C-4,10 -2,12 0,12 C2,12 4,10 6,7 C9,2 13,2 16,5 C19,1 23,1 27,4 C27,-1 30,-6 36,-8 "
                "C24,-14 14,-12 8,-5 C7,-7 6,-9 6,-13 C5,-12 2,-9 0,-4 Z", colour)


def ghost(sheet, face):
    return (fill("M-14,20 C-14,10 -15,2 -15,-4 C-15,-14 -8,-21 0,-21 C8,-21 15,-14 15,-4 C15,2 14,10 14,20 "
                 "C11,20 10,16 7,16 C4,16 4,20 0,20 C-4,20 -4,16 -7,16 C-10,16 -11,20 -14,20 Z", sheet)
            + ellipse(-5, -6, 2.3, 3.3, face) + ellipse(5, -6, 2.3, 3.3, face) + ellipse(0, 2.5, 2, 2.8, face))


def sweet(wrap, stripe):
    return (fill("M-9,0 L-21,-8 C-19,-3 -19,3 -21,8 Z M9,0 L21,-8 C19,-3 19,3 21,8 Z", wrap) + circle(0, 0, 10, wrap)
            + line("M-6,-4 C-2,-7 4,-5 5,-1 C6,3 1,6 -2,3", stripe, 1.6))


def halloween(dark):
    orange, rib = ("#fb923c", "#c2410c") if dark else ("#f97316", "#c2410c")
    plum = "#a78bfa" if dark else "#8b5cf6"
    glow = "#fde68a" if dark else "#fff7ed"
    sheet, face = ("#ede9fe", "#2a1245") if dark else ("#c4b5fd", "#fff7ed")
    moon = "#fcd34d" if dark else "#fdba74"
    big = [pumpkin(orange, rib, "#84cc16" if dark else "#65a30d", glow), bat(plum), crescent(moon), ghost(sheet, face),
           pumpkin(orange, rib, "#84cc16" if dark else "#65a30d", glow), bat(plum), sweet(plum, glow), ghost(sheet, face), bat(plum)]
    small = [sparkle(moon), sparkle(plum, 5), circle(0, 0, 2.2, orange)]
    return big, small


# --- Christmas ------------------------------------------------------------------------------------------------------

def tree(green, shade, trunk, gold, red):
    return (f'<rect x="-4" y="21" width="8" height="9" rx="1" fill="{trunk}"/>'
            + fill("M0,-28 L-11,-11 L-6,-11 L-17,5 L-10,5 L-22,22 L22,22 L10,5 L17,5 L6,-11 L11,-11 Z", green)
            + line("M-8,-3 Q0,3 8,-5 M-13,13 Q0,21 14,10", shade, 1.4)
            + circle(-5, -2, 2.1, red) + circle(5, -9, 1.9, gold) + circle(8, 9, 2.1, red) + circle(-9, 16, 2.1, gold) + circle(1, 15, 1.9, red)
            + f'<g transform="translate(0 -31)">{star(gold, 5, 6.5, 2.8)}</g>')


def snowflake(colour):
    arm = line("M0,0 V-17 M0,-11 L-4.5,-15.5 M0,-11 L4.5,-15.5 M0,-5.5 L-3,-8.5 M0,-5.5 L3,-8.5", colour, 1.7)
    return "".join(turned(arm, i * 60) for i in range(6))


def gift(box, lid, ribbon):
    return (f'<rect x="-15" y="-5" width="30" height="23" rx="2" fill="{box}"/><rect x="-18" y="-12" width="36" height="9" rx="2" fill="{lid}"/>'
            f'<rect x="-3.5" y="-12" width="7" height="30" fill="{ribbon}"/>'
            + fill("M0,-12 C-5,-23 -17,-23 -13,-15 C-11,-11 -4,-12 0,-12 Z M0,-12 C5,-23 17,-23 13,-15 C11,-11 4,-12 0,-12 Z", ribbon))


def bauble(ball, band, cap):
    return (line("M0,-19 V-25", cap, 1.4) + f'<rect x="-4" y="-19" width="8" height="5.5" rx="1.2" fill="{cap}"/>' + circle(0, 0, 14, ball)
            + line("M-13,-2 Q-6.5,-8 0,-2 T13,-2", band, 1.8) + line("M-11,5 Q-5.5,0 0,5 T11,5", band, 1.4))


def cane(base, stripe):
    hook = "M-7,20 V-7 A9,9 0 0 1 11,-7"
    return (line(hook, base, 7) + f'<path d="{hook}" fill="none" stroke="{stripe}" stroke-width="7" stroke-dasharray="4.5 6.5" stroke-linecap="butt"/>')


def holly(leaf, berry):
    blade = "M0,0 C3,-7 9,-5 11,-11 C14,-6 20,-7 23,0 C20,7 14,6 11,11 C9,5 3,7 0,0 Z"
    return (f'<g transform="rotate(-150)">{fill(blade, leaf)}</g><g transform="rotate(-30)">{fill(blade, leaf)}</g>'
            + circle(-3, 3, 3.6, berry) + circle(3.5, 3.5, 3.6, berry) + circle(0, -2.5, 3.6, berry))


def christmas(dark):
    green, shade = ("#4ade80", "#bbf7d0") if dark else ("#16a34a", "#bbf7d0")
    red = "#f87171" if dark else "#dc2626"
    gold = "#fcd34d" if dark else "#f59e0b"
    ice = "#bae6fd" if dark else "#38bdf8"
    base = "#fee2e2" if dark else "#fecaca"
    big = [tree(green, shade, "#a16207", gold, red), snowflake(ice), gift(red, "#fca5a5" if dark else "#ef4444", gold), bauble(red, base, gold),
           cane(base, red), tree(green, shade, "#a16207", gold, red), holly(green, red), bauble(gold, "#fef3c7", red), snowflake(ice)]
    small = [sparkle(gold), circle(0, 0, 2.4, ice), circle(0, 0, 1.6, ice)]
    return big, small


# --- New Year -------------------------------------------------------------------------------------------------------

def balloon(skin, shine, string):
    return (line("M0,19 C-4,26 4,31 0,38", string, 1.2) + ellipse(0, 0, 13, 16, skin) + fill("M0,15 L-3.5,20 L3.5,20 Z", skin)
            + line("M-7,-7 C-6,-10 -4,-12 -1,-12", shine, 1.6))


def streamer(colour):
    return line("M-18,6 C-12,-8 -6,10 0,-2 C6,-12 12,8 18,-6", colour, 2.2)


def confetti(a, b, c):
    return (f'<rect x="-12" y="-9" width="9" height="3.6" rx="1.2" fill="{a}" transform="rotate(-25 -8 -7)"/>'
            f'<rect x="4" y="3" width="9" height="3.6" rx="1.2" fill="{b}" transform="rotate(35 8 5)"/>'
            + circle(8, -9, 2.3, c) + circle(-8, 8, 1.9, b) + circle(0, -1, 1.5, a))


def new_year(dark):
    gold = "#fcd34d" if dark else "#f59e0b"
    pink = "#f9a8d4" if dark else "#ec4899"
    blue = "#93c5fd" if dark else "#3b82f6"
    violet = "#c4b5fd" if dark else "#8b5cf6"
    pale = "#fef3c7" if dark else "#fde68a"
    big = [burst(gold, pale), balloon(pink, "#fce7f3", violet), burst(blue, pale, 10, 6, 17), streamer(violet), burst(pink, pale, 14, 8, 24),
           confetti(gold, blue, pink), burst(violet, pale, 10, 6, 18), balloon(blue, "#dbeafe", gold), confetti(pink, violet, gold)]
    small = [sparkle(gold), sparkle(pale, 5), circle(0, 0, 2.2, pink)]
    return big, small


# --- Valentine's Day ------------------------------------------------------------------------------------------------

HEART = ("M0,20 C-6,14 -24,4 -24,-9 C-24,-17 -18,-22 -11,-22 C-6,-22 -2,-19 0,-15 C2,-19 6,-22 11,-22 "
         "C18,-22 24,-17 24,-9 C24,4 6,14 0,20 Z")


def heart(colour, shine):
    return fill(HEART, colour) + line("M-17,-10 C-17,-14 -14,-17 -10,-17", shine, 1.8)


def heart_outline(colour):
    return line(HEART, colour, 2.2)


def struck_heart(colour, shine, arrow):
    return (line("M-34,13 L-20,8 M20,-8 L34,-13", arrow, 2) + fill("M34,-13 L26,-15.5 L29,-7.5 Z", arrow)
            + line("M-34,13 L-37,7 M-30,11.5 L-33,5.5 M-34,13 L-28,16 M-30,11.5 L-24,14.5", arrow, 1.5) + heart(colour, shine))


def letter(paper, fold, seal):
    return (f'<rect x="-20" y="-13" width="40" height="27" rx="3" fill="{paper}"/>' + line("M-19,-11 L0,4 L19,-11", fold, 1.6)
            + f'<g transform="translate(0 4) scale(0.3)">{fill(HEART, seal)}</g>')


def valentine(dark):
    rose, shine = ("#fb7185", "#fecdd3") if dark else ("#e11d48", "#fecdd3")
    pink = "#f9a8d4" if dark else "#f472b6"
    blush = "#fecdd3" if dark else "#fda4af"
    paper, fold = ("#ffe4e6", "#fb7185") if dark else ("#fecdd3", "#e11d48")
    big = [heart(rose, shine), heart_outline(pink), letter(paper, fold, rose), heart(pink, "#fce7f3"), struck_heart(rose, shine, blush),
           heart_outline(rose), heart(blush, "#fff1f2"), heart(rose, shine), heart_outline(pink)]
    small = [f'<g transform="scale(0.26)">{fill(HEART, pink)}</g>', sparkle(blush, 5), circle(0, 0, 2.1, rose)]
    return big, small


# --- Easter ---------------------------------------------------------------------------------------------------------

EGG = "M0,-23 C11,-23 17,-5 17,6 C17,17 9,23 0,23 C-9,23 -17,17 -17,6 C-17,-5 -11,-23 0,-23 Z"


def egg(shell, trim, kind):
    if kind == "zigzag":
        inside = line("M-20,-2 L-13,-9 L-6.5,-2 L0,-9 L6.5,-2 L13,-9 L20,-2", trim, 2.4) + line("M-20,10 H20", trim, 2.2) + line("M-20,-15 H20", trim, 1.6)
    elif kind == "dots":
        inside = "".join(circle(x, y, 2.3, trim) for x, y in [(-6, -12), (6, -12), (0, -3), (-10, 5), (10, 5), (-3, 14), (6, 14), (0, -19)])
    else:
        inside = line("M-20,-10 Q0,-17 20,-10 M-20,1 Q0,-6 20,1 M-20,12 Q0,5 20,12", trim, 2.6)
    return fill(EGG, shell) + f'<g clip-path="url(#egg)">{inside}</g>'


def bunny(fur, ear, face):
    return (ellipse(-7, -20, 5.2, 14, fur, -9) + ellipse(7, -20, 5.2, 14, fur, 9) + ellipse(-7, -19, 2.3, 9, ear, -9) + ellipse(7, -19, 2.3, 9, ear, 9)
            + ellipse(0, 3, 15, 13.5, fur) + circle(-5.5, 0, 1.7, face) + circle(5.5, 0, 1.7, face)
            + fill("M-2,5 H2 L0,7.5 Z", ear) + line("M0,7.5 V9.5 M0,9.5 C-1.5,11.5 -4,11.5 -5,10 M0,9.5 C1.5,11.5 4,11.5 5,10", face, 1.1))


def carrot(root, groove, leaf):
    return (line("M0,-11 C-2,-19 -8,-22 -11,-21 M0,-11 C0,-20 1,-24 3,-26 M0,-11 C3,-18 8,-20 12,-19", leaf, 2.4)
            + fill("M0,24 C-8,8 -10,-3 -7,-9 C-3,-13 3,-13 7,-9 C10,-3 8,8 0,24 Z", root) + line("M-6,-3 H-1 M1,4 H6 M-4,10 H0", groove, 1.3))


def easter(dark):
    pink = "#f9a8d4" if dark else "#f472b6"
    blue = "#93c5fd" if dark else "#60a5fa"
    yellow = "#fde047" if dark else "#facc15"
    lilac = "#c4b5fd" if dark else "#a78bfa"
    green = "#86efac" if dark else "#4ade80"
    fur, face = ("#f5f5f4", "#44403c") if dark else ("#d6d3d1", "#57534e")
    white = "#ffffff"
    big = [egg(pink, white, "zigzag"), flower(yellow, "#fb923c"), bunny(fur, pink, face), egg(blue, white, "dots"), carrot("#fb923c", "#fed7aa", green),
           flower(lilac, yellow), egg(lilac, white, "waves"), egg(green, white, "zigzag"), flower(pink, yellow)]
    small = [circle(0, 0, 2.4, yellow), flower(blue, white, 5, 4.2, 2.8, 1.8), circle(0, 0, 1.8, pink)]
    return big, small


# --- Diwali ---------------------------------------------------------------------------------------------------------

def lamp(clay, rim, flame, heart):
    """A diya: a shallow clay bowl with a flame at its lip."""
    return (fill("M0,-33 C8,-22 10,-13 0,-4 C-10,-13 -8,-22 0,-33 Z", flame) + fill("M0,-23 C3.6,-17 4.6,-12 0,-7 C-4.6,-12 -3.6,-17 0,-23 Z", heart)
            + fill("M-24,-3 C-22,13 -12,19 0,19 C12,19 22,13 24,-3 C14,2 -14,2 -24,-3 Z", clay) + ellipse(0, -2.6, 23.5, 4.2, rim)
            + circle(-11, 9, 1.6, rim) + circle(0, 12, 1.6, rim) + circle(11, 9, 1.6, rim))


def diwali(dark):
    magenta = "#f472b6" if dark else "#db2777"
    orange = "#fb923c" if dark else "#f97316"
    gold = "#fcd34d" if dark else "#f59e0b"
    violet = "#c4b5fd" if dark else "#7c3aed"
    clay, rim = ("#fdba74", "#fed7aa") if dark else ("#ea580c", "#fdba74")
    pale = "#fef3c7"
    big = [lamp(clay, rim, orange, pale), rosette(magenta, orange, gold, gold), burst(gold, pale, 12, 6, 18), lamp(clay, rim, orange, pale),
           rosette(violet, magenta, gold, orange), flower(orange, gold, 8, 9, 5, 4), lamp(clay, rim, orange, pale), rosette(orange, gold, magenta, magenta),
           burst(magenta, pale, 10, 6, 16)]
    small = [sparkle(gold), circle(0, 0, 2.3, magenta), sparkle(orange, 5)]
    return big, small


# --- Vesak ----------------------------------------------------------------------------------------------------------

def lantern(paper, light, frame, tail):
    """A Vesak lantern: a paper diamond on a frame, with a smaller one at each side and streamers below."""
    return (line("M0,-22 V-31", frame, 1.3) + line("M0,22 C-3,29 3,33 0,40 M-6,18 C-10,25 -5,29 -8,36 M6,18 C10,25 5,29 8,36", tail, 2.1)
            + fill("M-22,0 L-29,-7 L-36,0 L-29,7 Z M22,0 L29,-7 L36,0 L29,7 Z", tail)
            + fill("M0,-22 L22,0 L0,22 L-22,0 Z", paper) + fill("M0,-12 L12,0 L0,12 L-12,0 Z", light) + line("M0,-22 V22 M-22,0 H22", frame, 1.1))


def lotus(petal, inner, leaf):
    blade = "M0,-24 C8,-13 8,-2 0,6 C-8,-2 -8,-13 0,-24 Z"
    side = lambda a, c: f'<g transform="translate(0 6) rotate({a}) translate(0 -6)">{fill(blade, c)}</g>'
    return (fill("M-30,8 C-16,4 16,4 30,8 C16,15 -16,15 -30,8 Z", leaf) + side(-66, petal) + side(66, petal) + side(-34, inner) + side(34, inner)
            + fill(blade, petal) + line("M0,-15 V1", inner, 1.3))


def bodhi(leaf, vein):
    return (fill("M0,-17 C12,-24 26,-9 15,6 C10,13 4,16 1.5,25 L0,33 L-1.5,25 C-4,16 -10,13 -15,6 C-26,-9 -12,-24 0,-17 Z", leaf)
            + line("M0,-15 V24 M0,-6 L-9,-1 M0,-6 L9,-1 M0,3 L-7,8 M0,3 L7,8", vein, 1.2) + line("M0,-17 V-25", leaf, 1.6))


def vesak(dark):
    blue = "#93c5fd" if dark else "#2563eb"
    yellow = "#fde047" if dark else "#eab308"
    red = "#fca5a5" if dark else "#dc2626"
    orange = "#fdba74" if dark else "#f97316"
    pink, inner = ("#f9a8d4", "#fce7f3") if dark else ("#f472b6", "#fbcfe8")
    green, vein = ("#86efac", "#14532d") if dark else ("#22c55e", "#dcfce7")
    light, frame = ("#fef9c3", "#fef3c7") if dark else ("#fef9c3", "#a16207")
    big = [lantern(yellow, light, frame, orange), lotus(pink, inner, green), bodhi(green, vein), lantern(blue, light, frame, yellow),
           lotus(pink, inner, green), lantern(red, light, frame, yellow), bodhi(green, vein), lantern(orange, light, frame, blue), lotus(pink, inner, green)]
    small = [circle(0, 0, 2.6, yellow), sparkle(yellow, 5), circle(0, 0, 1.8, orange)]
    return big, small


# --- Eid ------------------------------------------------------------------------------------------------------------

def moon_and_star(gold):
    return crescent(gold, 24, 20, 8, -3) + f'<g transform="translate(15 -6) rotate(12)">{star(gold, 5, 7.5, 3.1)}</g>'


def fanous(metal, glass, glow):
    """A lantern with a domed top, glass sides and a ring to hang it by."""
    return (f'<circle cx="0" cy="-30" r="3.6" fill="none" stroke="{metal}" stroke-width="1.6"/>'
            + fill("M-9,-14 C-9,-22 -4,-26 0,-27 C4,-26 9,-22 9,-14 Z", metal) + f'<rect x="-12" y="-15" width="24" height="4" rx="1.4" fill="{metal}"/>'
            + fill("M-10,-11 H10 L13,13 H-13 Z", glass) + fill("M-4,-8 H4 L5.5,10 H-5.5 Z", glow) + line("M-10,-11 L-13,13 M10,-11 L13,13", metal, 1.6)
            + f'<rect x="-15" y="13" width="30" height="4.5" rx="1.4" fill="{metal}"/>' + fill("M-7,17.5 H7 L4,23 H-4 Z", metal))


def tile_star(colour, heart):
    """An eight-pointed star: two squares, one turned an eighth."""
    square = f'<rect x="-13" y="-13" width="26" height="26" rx="1.5" fill="none" stroke="{colour}" stroke-width="2"/>'
    return square + turned(square, 45) + circle(0, 0, 4, heart)


def hanging_star(gold, string):
    return line("M0,-34 V-10", string, 1.1) + star(gold, 5, 10, 4.2)


def eid(dark):
    gold = "#fcd34d" if dark else "#d97706"
    teal = "#5eead4" if dark else "#0d9488"
    glass, glow = ("#99f6e4", "#fef9c3") if dark else ("#5eead4", "#fef9c3")
    mint = "#ccfbf1" if dark else "#14b8a6"
    big = [moon_and_star(gold), fanous(gold, glass, glow), tile_star(teal, gold), hanging_star(gold, mint), fanous(teal, "#fde68a", glow),
           moon_and_star(gold), tile_star(gold, teal), hanging_star(gold, mint), tile_star(teal, gold)]
    small = [star(gold, 5, 5, 2.1), circle(0, 0, 2.2, teal), sparkle(gold, 5)]
    return big, small


# --- Lunar New Year -------------------------------------------------------------------------------------------------

def red_lantern(silk, rib, gold):
    return (line("M0,-17 V-27", gold, 1.3) + line("M0,17 V24 M-2.5,24 V34 M0,24 V35 M2.5,24 V34", gold, 1.3)
            + ellipse(0, 0, 19, 15, silk) + f'<g clip-path="url(#lantern)">' + f'<ellipse cx="0" cy="0" rx="10.5" ry="15" fill="none" stroke="{rib}" stroke-width="1.3"/>'
            + line("M0,-15 V15", rib, 1.3) + "</g>"
            + f'<rect x="-8" y="-18.5" width="16" height="4.5" rx="1.4" fill="{gold}"/><rect x="-8" y="14" width="16" height="4.5" rx="1.4" fill="{gold}"/>')


def blossom(petal, heart, stamen):
    return (flower(petal, heart, 5, 8.6, 6.4, 3.2)
            + "".join(line(f"M0,0 L{n(polar(6.4, i * 72 + 36)[0])},{n(polar(6.4, i * 72 + 36)[1])}", stamen, 0.9) for i in range(5)))


def coin(gold, shade):
    return (circle(0, 0, 14, gold) + f'<circle cx="0" cy="0" r="10.6" fill="none" stroke="{shade}" stroke-width="1.2"/>'
            + f'<rect x="-4.2" y="-4.2" width="8.4" height="8.4" rx="0.8" fill="{shade}"/>')


def fan(silk, rib, gold):
    spokes = "".join(line(f"M0,14 L{n(polar(28, a)[0])},{n(14 + polar(28, a)[1])}", rib, 1.1) for a in (-52, -26, 0, 26, 52))
    left, right = polar(30, -66), polar(30, 66)
    return (fill(f"M0,14 L{n(left[0])},{n(14 + left[1])} A30,30 0 0 1 {n(right[0])},{n(14 + right[1])} Z", silk) + spokes + circle(0, 14, 3.2, gold))


def lunar(dark):
    red, rib = ("#f87171", "#fecaca") if dark else ("#dc2626", "#fca5a5")
    gold, shade = ("#fcd34d", "#b45309") if dark else ("#f59e0b", "#fef3c7")
    pink = "#fda4af" if dark else "#fb7185"
    big = [red_lantern(red, rib, gold), blossom(pink, gold, "#fff1f2"), coin(gold, shade), fan(red, rib, gold), red_lantern(red, rib, gold),
           blossom(pink, gold, "#fff1f2"), red_lantern(red, rib, gold), coin(gold, shade), blossom(pink, gold, "#fff1f2")]
    small = [sparkle(gold), circle(0, 0, 2.2, red), f'<g transform="scale(0.45)">{blossom(pink, gold, "#fff1f2")}</g>']
    return big, small


# --- The tile -------------------------------------------------------------------------------------------------------

# Where the nine large drawings go (x, y, turn, scale), spread unevenly so the repeat does not read as a grid, and
# where the small ones go between them (x, y, turn, scale, which of the three small drawings).
LARGE = [(78, 84, -10, 1.12), (262, 58, 8, 0.98), (410, 130, -6, 1.06), (178, 214, 6, 0.98), (338, 276, 12, 1.12),
         (66, 322, -14, 0.94), (216, 394, -4, 1.06), (424, 424, 10, 0.9), (28, 196, 4, 0.62)]
SMALL = [(165, 122, 0, 1.0, 0), (338, 52, 12, 0.8, 1), (462, 236, 0, 1.0, 2), (254, 152, 20, 0.7, 0), (108, 218, 0, 1.0, 1),
         (264, 304, 0, 1.0, 2), (140, 448, 15, 0.9, 0), (332, 376, 0, 0.8, 1), (24, 410, 0, 1.0, 2), (396, 204, 10, 0.75, 0),
         (452, 330, 0, 1.0, 1), (196, 306, 0, 0.8, 2), (20, 30, 0, 0.9, 0), (124, 24, 0, 1.0, 2), (300, 448, 0, 0.9, 1)]

DEFS = (f'<defs><clipPath id="egg"><path d="{EGG}"/></clipPath>'
        '<clipPath id="lantern"><ellipse cx="0" cy="0" rx="19" ry="15"/></clipPath></defs>')

# Each background's drawings, by the name app.css and the Background preference know it by.
BACKGROUNDS = {
    "halloween": halloween,
    "christmas": christmas,
    "newyear": new_year,
    "valentine": valentine,
    "easter": easter,
    "diwali": diwali,
    "vesak": vesak,
    "eid": eid,
    "lunarnewyear": lunar,
}


def place(inner, x, y, turn, scale, opacity):
    """One drawing, and again across each edge it comes near, so the tile repeats without a seam."""
    copies = []
    for dx in (-SIZE, 0, SIZE):
        for dy in (-SIZE, 0, SIZE):
            px, py = x + dx, y + dy
            if -70 < px < SIZE + 70 and -70 < py < SIZE + 70:
                copies.append(f'<g transform="translate({px} {py}) rotate({turn}) scale({scale})" opacity="{opacity}">{inner}</g>')
    return "".join(copies)


def tile_body(name, dark):
    big, small = BACKGROUNDS[name](dark)
    strong, soft = (0.62, 0.5) if dark else (0.5, 0.42)
    parts = [place(big[i], x, y, turn, scale, strong) for i, (x, y, turn, scale) in enumerate(LARGE)]
    parts += [place(small[which], x, y, turn, scale, soft) for x, y, turn, scale, which in SMALL]
    return DEFS + "".join(parts)


def tile(name, dark):
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{SIZE}" height="{SIZE}" viewBox="0 0 {SIZE} {SIZE}">'
            + tile_body(name, dark) + "</svg>\n")


# The page colours behind each tile (top, middle), as app.css has them: only for the preview sheet.
SKIES = {
    "halloween": (("#ffedd5", "#fff7ed"), ("#2a1245", "#160d26")),
    "christmas": (("#dcfce7", "#f0fdf4"), ("#0f2a1d", "#0b1a14")),
    "newyear": (("#ede9fe", "#f5f3ff"), ("#1e1b4b", "#0f0e2a")),
    "valentine": (("#ffe4e6", "#fff1f2"), ("#4c0519", "#2a0a14")),
    "easter": (("#fce7f3", "#fefce8"), ("#2e1f3a", "#1a1522")),
    "diwali": (("#ffedd5", "#fff7ed"), ("#3b0a2a", "#1f0a1a")),
    "vesak": (("#fef9c3", "#fefce8"), ("#0c1e3d", "#0a1326")),
    "eid": (("#ccfbf1", "#f0fdfa"), ("#042f2e", "#041c1c")),
    "lunarnewyear": (("#fee2e2", "#fef2f2"), ("#450a0a", "#240808")),
}


def preview(path, names):
    """A contact sheet: each background on its light and its dark page colours, with a card on top as in the app."""
    cell, cells = 360, []
    for row, name in enumerate(names):
        for column, dark in enumerate((False, True)):
            top, middle = SKIES[name][1 if dark else 0]
            end = "#0c0a09" if dark else "#f5f5f4"
            card, ink = ("#1c1917", "#e7e5e4") if dark else ("#ffffff", "#1c1917")
            key = f"{name}{column}"
            cells.append(
                f'<g transform="translate({column * (cell + 12)} {row * (cell + 12)})">'
                f'<defs><linearGradient id="g{key}" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="{top}"/>'
                f'<stop offset="0.45" stop-color="{middle}"/><stop offset="1" stop-color="{end}"/></linearGradient>'
                f'<pattern id="p{key}" width="300" height="300" patternUnits="userSpaceOnUse"><g transform="scale(0.625)">{tile_body(name, dark).replace("url(#egg)", f"url(#egg{key})").replace("url(#lantern)", f"url(#lantern{key})").replace(chr(34) + "egg" + chr(34), chr(34) + "egg" + key + chr(34)).replace(chr(34) + "lantern" + chr(34), chr(34) + "lantern" + key + chr(34))}</g></pattern></defs>'
                f'<rect width="{cell}" height="{cell}" fill="url(#g{key})"/><rect width="{cell}" height="{cell}" fill="url(#p{key})"/>'
                f'<rect x="14" y="250" width="{cell - 28}" height="80" rx="16" fill="{card}"/>'
                f'<text x="32" y="296" font-family="sans-serif" font-size="15" fill="{ink}">{name}</text></g>')
    width, height = 2 * cell + 12, len(names) * (cell + 12)
    path.write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">'
                    f'<rect width="{width}" height="{height}" fill="#78716c"/>' + "".join(cells) + "</svg>\n")


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    for name in BACKGROUNDS:
        for dark in (False, True):
            (OUT / f"{name}-{'dark' if dark else 'light'}.svg").write_text(tile(name, dark))
    if "--preview" in sys.argv:
        target = pathlib.Path(sys.argv[sys.argv.index("--preview") + 1])
        only = sys.argv[sys.argv.index("--only") + 1].split(",") if "--only" in sys.argv else list(BACKGROUNDS)
        preview(target, only)


if __name__ == "__main__":
    main()
