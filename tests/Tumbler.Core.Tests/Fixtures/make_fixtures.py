"""Regenerates the test PDFs. Needs: pip install reportlab pypdf"""
from reportlab.lib.pagesizes import letter, A4
from reportlab.pdfgen import canvas
from pypdf import PdfReader, PdfWriter

def text_doc(path, title, pages, size=letter):
    c = canvas.Canvas(path, pagesize=size)
    for i in range(pages):
        c.setFont("Helvetica-Bold", 24)
        c.drawString(72, size[1] - 100, f"{title} - page {i + 1}")
        c.setFont("Helvetica", 12)
        c.drawString(72, 650, "The quick brown fox jumps over the lazy dog.")
        c.drawString(72, 630, "Second line of sample text for highlighting.")
        c.rect(72, 300, 200, 100)
        c.showPage()
    c.save()

text_doc("sample.pdf", "Sample", 5)
text_doc("other.pdf", "Other", 3, A4)

# A copy where page 2 is already rotated, to test coordinate mapping.
r = PdfReader("sample.pdf"); w = PdfWriter()
for i, p in enumerate(r.pages):
    if i == 1: p.rotate(90)
    if i == 2: p.rotate(180)
    if i == 3: p.rotate(270)
    w.add_page(p)
with open("rotated.pdf", "wb") as f: w.write(f)

# Password protected copy (user password "secret").
r = PdfReader("sample.pdf"); w = PdfWriter()
for p in r.pages: w.add_page(p)
w.encrypt(user_password="secret", owner_password="owner", algorithm="AES-128")
with open("protected.pdf", "wb") as f: w.write(f)

# Locked against editing but opens without a password, like many government forms.
r = PdfReader("sample.pdf"); w = PdfWriter()
for p in r.pages: w.add_page(p)
w.encrypt(user_password="", owner_password="owner", algorithm="AES-256", permissions_flag=0)
with open("locked.pdf", "wb") as f: w.write(f)

# Damaged: the cross-reference table points at the wrong places.
data = bytearray(open("sample.pdf", "rb").read())
xref = data.rindex(b"startxref")
data[xref:] = b"startxref\n999999\n%%EOF\n"
with open("damaged.pdf", "wb") as f: f.write(data)
