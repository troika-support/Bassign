#!/usr/bin/env python3
"""Verify WCAG contrast for the design-system tokens. Run: python3 contrast-check.py
AAA = 7:1 for normal text, 4.5:1 for large/UI. Keep text pairs at AAA."""

def lum(hexc):
    hexc = hexc.lstrip('#')
    r, g, b = (int(hexc[i:i+2], 16) / 255 for i in (0, 2, 4))
    def f(c): return c / 12.92 if c <= 0.03928 else ((c + 0.055) / 1.055) ** 2.4
    R, G, B = f(r), f(g), f(b)
    return 0.2126 * R + 0.7152 * G + 0.0722 * B

def ratio(a, b):
    la, lb = lum(a), lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)

def lvl(r, large=False):
    if large:
        return "AAA" if r >= 4.5 else "AA" if r >= 3 else "FAIL"
    return "AAA" if r >= 7 else "AA" if r >= 4.5 else "FAIL"

# (fg, bg, label, is_large_or_ui)
pairs = [
    ("#16191D", "#FFFFFF", "ink on bg", False),
    ("#44505E", "#FFFFFF", "secondary on bg", False),
    ("#4C5864", "#FFFFFF", "muted on bg", False),
    ("#FFFFFF", "#1D4ED8", "white on accent (button)", True),
    ("#0B7A3B", "#FFFFFF", "success text on bg", True),
    ("#B0520A", "#FFFFFF", "warning text on bg", True),
    ("#C01B1B", "#FFFFFF", "danger text on bg", True),
    # dark
    ("#E7EAEE", "#15181C", "ink on dark bg", False),
    ("#AEB6C0", "#15181C", "secondary on dark bg", False),
    ("#9AA4B0", "#15181C", "muted on dark bg", False),
    ("#7AA2FF", "#15181C", "accent text on dark bg", False),
    ("#56D08A", "#15181C", "success on dark bg", False),
    ("#F0A24B", "#15181C", "warning on dark bg", False),
    ("#FF8585", "#15181C", "danger on dark bg", False),
]
print(f"{'pair':<36}{'ratio':>7}  {'level':>5}")
print("-" * 52)
for fg, bg, label, large in pairs:
    r = ratio(fg, bg)
    tag = " (large/UI)" if large else ""
    print(f"{label + tag:<36}{r:>6.2f}  {lvl(r, large):>5}")
