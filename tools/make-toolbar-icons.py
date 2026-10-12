# Draws Tumbler's toolbar icons and saves them as strips of 16, 20, 24, 30 and 40 px
# icons in src/Tumbler/Icons. The app picks the strip that fits the screen's scaling.
# Needs: pip install cairosvg pillow. Run from anywhere: python tools/make-toolbar-icons.py
# The order of names here must match ToolbarIcons.Names in src/Tumbler/ToolbarIcons.vb.
import cairosvg, io, os
from PIL import Image
INK = "#334155"; ACCENT_Y = "#F5B800"; ACCENT_R = "#E5383B"; ACCENT_B = "#2F6BFF"
def S(body, extra=""):
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="24" height="24" fill="none" stroke="{INK}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">{extra}{body}</svg>'
P = lambda d, **kw: '<path d="%s"%s/>' % (d, ''.join(f' {k.replace("_","-")}="{v}"' for k, v in kw.items()))
icons = {
 "open": S(P("M3 7.5a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z")),
 "save": S(P("M5 4h11l4 4v11a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1z")+P("M8 4v5h7V4")+P("M7.5 20v-6h9v6")),
 "undo": S(P("M9 14L4 9l5-5")+P("M4 9h10.5a5.5 5.5 0 0 1 0 11H11")),
 "redo": S(P("M15 14l5-5-5-5")+P("M20 9H9.5a5.5 5.5 0 0 0 0 11H13")),
 "prev": S(P("M15 18l-6-6 6-6")),
 "next": S(P("M9 18l6-6-6-6")),
 "zoom-out": S('<circle cx="11" cy="11" r="7"/>'+P("M20.5 20.5l-4.6-4.6")+P("M8 11h6")),
 "zoom-in": S('<circle cx="11" cy="11" r="7"/>'+P("M20.5 20.5l-4.6-4.6")+P("M8 11h6")+P("M11 8v6")),
 "select": S(P("M5 3.5l6.8 16.5 2.4-6.9 6.8-2.5z", fill="#FFFFFF")),
 "highlight": S(P("M13 20h8", stroke=ACCENT_Y, stroke_width="3")+P("M9 11l-6 6v3h6l3-3")+P("M21 11l-4.6 4.6a2 2 0 0 1-2.8 0l-5.2-5.2a2 2 0 0 1 0-2.8L13 3")),
 "comment": S(P("M20 15a2 2 0 0 1-2 2H8l-4 4V6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2z")+P("M8 8.5h8", stroke=ACCENT_B)+P("M8 12.5h5", stroke=ACCENT_B)),
 "line": S(P("M5 19L19 5", stroke=ACCENT_R, stroke_width="2.6")),
 "thickness": S(P("M4 6h16", stroke_width="1")+P("M4 12h16", stroke_width="2.2")+P("M4 18.5h16", stroke_width="3.6")),
 "rotate-left": S(P("M3.5 12a8.5 8.5 0 1 0 2.6-6.1L3.5 8.5")+P("M3.5 3.5v5h5")),
 "rotate-right": S(P("M20.5 12a8.5 8.5 0 1 1-2.6-6.1l2.6 2.6")+P("M20.5 3.5v5h-5")),
 "rotate-all": S(P("M20.5 12a8.5 8.5 0 1 1-2.6-6.1l2.6 2.6")+P("M20.5 3.5v5h-5")+'<rect x="9" y="9" width="6" height="7" rx="1" stroke-width="1.5"/>'),
 "delete-pages": S(P("M4 6.5h16")+P("M9 6.5V4.5h6v2")+P("M6.5 6.5l.9 13a1 1 0 0 0 1 .9h7.2a1 1 0 0 0 1-.9l.9-13")+P("M10.2 10.5v6")+P("M13.8 10.5v6")),
 "insert-pages": S(P("M14 3H7a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V8z")+P("M14 3v5h5")+P("M12 11.5v6", stroke=ACCENT_B)+P("M9 14.5h6", stroke=ACCENT_B)),
}
names = list(icons)
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "Tumbler", "Icons")
sizes = [16, 20, 24, 30, 40]
for s in sizes:
    sheet = Image.new("RGBA", (s * len(names), s), (0, 0, 0, 0))
    for i, n in enumerate(names):
        png = cairosvg.svg2png(bytestring=icons[n].encode(), output_width=s, output_height=s)
        sheet.paste(Image.open(io.BytesIO(png)), (i * s, 0))
    sheet.save(os.path.join(OUT, f"toolbar-{s}.png"))
print(names)
