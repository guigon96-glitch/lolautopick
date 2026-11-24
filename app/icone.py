# Desenha o ícone do AutoPick (app/icone.ico e app/icone.png). Rode: python3 app/icone.py  (precisa de Pillow)
# Quadrado escuro arredondado, hexágono dourado e um "A" verde-água com uma trava (o "lock-in" do pick).
import math, os
from PIL import Image, ImageDraw, ImageFilter

T = 1024
FUNDO_A, FUNDO_B = (16, 24, 44), (6, 10, 20)
OURO, OURO_ESCURO, AGUA = (200, 155, 60), (120, 88, 30), (10, 200, 185)

def hexagono(cx, cy, r):
    return [(cx + r * math.cos(math.radians(60 * i - 90)), cy + r * math.sin(math.radians(60 * i - 90))) for i in range(6)]

img = Image.new('RGBA', (T, T), (0, 0, 0, 0))
# fundo com degradê vertical, cantos arredondados
grad = Image.new('RGBA', (T, T))
g = ImageDraw.Draw(grad)
for y in range(T):
    t = y / T
    g.line([(0, y), (T, y)], fill=tuple(int(FUNDO_A[i] + (FUNDO_B[i] - FUNDO_A[i]) * t) for i in range(3)) + (255,))
mascara = Image.new('L', (T, T), 0)
ImageDraw.Draw(mascara).rounded_rectangle([24, 24, T - 24, T - 24], radius=210, fill=255)
img.paste(grad, (0, 0), mascara)

d = ImageDraw.Draw(img)
c = T / 2
# brilho dourado atrás do hexágono
brilho = Image.new('RGBA', (T, T), (0, 0, 0, 0))
ImageDraw.Draw(brilho).polygon(hexagono(c, c + 10, 380), fill=OURO + (90,))
img.alpha_composite(brilho.filter(ImageFilter.GaussianBlur(40)))
d = ImageDraw.Draw(img)
# hexágono: borda dourada dupla
d.polygon(hexagono(c, c + 10, 370), fill=OURO)
d.polygon(hexagono(c, c + 10, 330), fill=OURO_ESCURO)
d.polygon(hexagono(c, c + 10, 312), fill=(12, 18, 34))
# "A" verde-água
topo, base, meia = c - 215, c + 205, 175
d.line([(c - meia, base), (c, topo), (c + meia, base)], fill=AGUA, width=78, joint='curve')
for x, y in [(c - meia, base), (c + meia, base), (c, topo)]:
    d.ellipse([x - 39, y - 39, x + 39, y + 39], fill=AGUA)
# trava: barra do "A" vira um cadeado dourado
d.rounded_rectangle([c - 118, c + 20, c + 118, c + 150], radius=26, fill=OURO)
d.arc([c - 70, c - 70, c + 70, c + 80], start=180, end=360, fill=OURO, width=34)
d.ellipse([c - 22, c + 62, c + 22, c + 106], fill=(12, 18, 34))

pasta = os.path.dirname(os.path.abspath(__file__))
final = img.resize((256, 256), Image.LANCZOS)
final.save(os.path.join(pasta, 'icone.png'))
final.save(os.path.join(pasta, 'icone.ico'), sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print('ok')
