"""Draws the No Blasphemy icon (../icon.png, 256 x 256): a speech bubble with a halo, saying "gosh!".
    python -I mods/NoBlasphemy/artwork/make_icon.py
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

S = 1024
here = Path(__file__).resolve().parent
font = lambda size: ImageFont.truetype("C:/Windows/Fonts/segoeuib.ttf", size)
img = Image.new("RGB", (S, S))
d = ImageDraw.Draw(img)
for y in range(S):   # spore-cave gradient: deep teal to fungus purple
    t = y / S
    d.line([(0, y), (S, y)], fill=(int(24 + 40 * t), int(52 - 20 * t), int(56 + 34 * t)))
glow = Image.new("RGBA", (S, S))
ImageDraw.Draw(glow).ellipse((262, 70, 762, 250), outline=(255, 214, 90, 255), width=60)
img.paste(glow.filter(ImageFilter.GaussianBlur(28)), (0, 0), glow.filter(ImageFilter.GaussianBlur(28)))
d = ImageDraw.Draw(img)
d.ellipse((292, 100, 732, 220), outline=(255, 214, 90), width=34)                       # halo
d.rounded_rectangle((132, 250, 892, 700), radius=120, fill=(250, 248, 240))            # bubble
d.polygon([(300, 690), (420, 690), (250, 830)], fill=(250, 248, 240))                  # its tail
d.text((512, 470), "gosh!", font=font(250), fill=(40, 56, 60), anchor="mm")
d.text((512, 905), "NO BLASPHEMY", font=font(112), fill=(255, 214, 90), anchor="mm")
img.resize((256, 256), Image.LANCZOS).save(here.parent / "icon.png")
print("icon.png written")
