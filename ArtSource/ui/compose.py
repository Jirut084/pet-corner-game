"""Build logo / sign / poster / sparkle SVGs from the icon SVGs (run from /home/claude/art)."""
import re
from pathlib import Path

SVG = Path('svg')
INK = '#29241C'
CARD = '#FCF7ED'
STEP = {'feed': '#FFD640', 'bath': '#59A6FF', 'groom': '#E673E6', 'play': '#73D973', 'rest': '#FF9940'}
LABEL = {'feed': 'อาหาร', 'bath': 'อาบน้ำ', 'groom': 'แต่งขน', 'play': 'เล่น', 'rest': 'พักผ่อน'}


def inner(name):
    s = (SVG / f'{name}.svg').read_text()
    return re.sub(r'^.*?<svg[^>]*>|</svg>\s*$', '', s, flags=re.S)


def place(name, x, y, size, vb=256):
    return f'<svg x="{x}" y="{y}" width="{size}" height="{size}" viewBox="0 0 {vb} {vb}">{inner(name)}</svg>'


# ---------- house + paw mark (drawn in a 256 box) ----------
MARK = f'''
<g stroke="{INK}" stroke-width="14" stroke-linejoin="round" stroke-linecap="round">
  <path d="M52 120 V214 Q52 230 68 230 H188 Q204 230 204 214 V120" fill="{CARD}"/>
  <path d="M24 128 L128 34 L232 128" fill="none" stroke="{INK}" stroke-width="40"/>
  <path d="M24 128 L128 34 L232 128" fill="none" stroke="#D9594D" stroke-width="16"/>
</g>
<g fill="#D99E26" stroke="{INK}" stroke-width="9" stroke-linejoin="round">
  <ellipse cx="128" cy="184" rx="34" ry="28"/>
  <ellipse cx="86" cy="146" rx="13" ry="17" transform="rotate(-20 86 146)"/>
  <ellipse cx="113" cy="126" rx="13" ry="17" transform="rotate(-6 113 126)"/>
  <ellipse cx="143" cy="126" rx="13" ry="17" transform="rotate(6 143 126)"/>
  <ellipse cx="170" cy="146" rx="13" ry="17" transform="rotate(20 170 146)"/>
</g>'''
(SVG / 'mark.svg').write_text(f'<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">{MARK}</svg>')


def dots(x, y, r, gap):
    return ''.join(f'<circle cx="{x + i * gap}" cy="{y}" r="{r}" fill="{c}" stroke="{INK}" stroke-width="{max(3, r // 3)}"/>'
                   for i, c in enumerate(STEP.values()))


# ---------- logo 1200x400 ----------
logo = f'''<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="400" viewBox="0 0 1200 400">
 {place('mark', 20, 44, 312)}
 <text x="356" y="236" font-family="Mitr" font-weight="600" font-size="168" letter-spacing="2"
       fill="{INK}" stroke="{INK}" stroke-width="2">PET</text>
 <text x="360" y="336" font-family="Mitr" font-weight="600" font-size="108" letter-spacing="14" fill="{INK}">CORNER</text>
 {dots(778, 196, 22, 66)}
</svg>'''
(SVG / 'logo.svg').write_text(logo)

# ---------- shop sign 1024x384 ----------
sign = f'''<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="384" viewBox="0 0 1024 384">
 <rect x="10" y="10" width="1004" height="364" rx="44" fill="#D9A86A" stroke="{INK}" stroke-width="16"/>
 <g stroke="#B9844A" stroke-width="6" stroke-linecap="round" opacity=".8">
  <path d="M60 110 H300 M420 96 H700 M780 120 H960 M70 290 H260 M560 300 H930"/>
 </g>
 <rect x="56" y="52" width="912" height="280" rx="28" fill="{CARD}" stroke="{INK}" stroke-width="12"/>
 {place('mark', 92, 82, 220)}
 <text x="340" y="192" font-family="Mitr" font-weight="600" font-size="100" fill="{INK}">PET CORNER</text>
 <text x="344" y="262" font-family="Mitr" font-weight="500" font-size="46" fill="#6A6152">ร้านรับฝากดูแลสัตว์เลี้ยง</text>
 {dots(358, 302, 13, 40)}
 <g fill="#B9844A" stroke="{INK}" stroke-width="6"><circle cx="40" cy="40" r="10"/><circle cx="984" cy="40" r="10"/><circle cx="40" cy="344" r="10"/><circle cx="984" cy="344" r="10"/></g>
</svg>'''
(SVG / 'sign_shop.svg').write_text(sign)

# ---------- poster: services 512x768 ----------
rows = ''
for i, k in enumerate(STEP):
    y = 200 + i * 100
    rows += f'''<rect x="44" y="{y}" width="424" height="84" rx="22" fill="{CARD}" stroke="{INK}" stroke-width="7"/>
 <circle cx="96" cy="{y + 44}" r="34" fill="{STEP[k]}" fill-opacity=".28"/>
 {place('icon_' + k, 64, y + 12, 64)}
 <text x="152" y="{y + 58}" font-family="Mitr" font-weight="600" font-size="38" fill="{INK}">{LABEL[k]}</text>'''
poster1 = f'''<svg xmlns="http://www.w3.org/2000/svg" width="512" height="768" viewBox="0 0 512 768">
 <rect x="8" y="8" width="496" height="752" rx="28" fill="#F3E3BF" stroke="{INK}" stroke-width="14"/>
 <rect x="8" y="8" width="496" height="170" rx="28" fill="#D9594D" stroke="{INK}" stroke-width="14"/>
 <rect x="15" y="150" width="482" height="22" fill="#D9594D"/>
 <path d="M8 178 H504" stroke="{INK}" stroke-width="14"/>
 <text x="256" y="92" text-anchor="middle" font-family="Mitr" font-weight="600" font-size="62" fill="{CARD}">บริการของเรา</text>
 <text x="256" y="142" text-anchor="middle" font-family="Mitr" font-weight="500" font-size="30" fill="{CARD}" fill-opacity=".9">ดูแลครบ 5 อย่าง</text>
 {rows}
 <text x="256" y="742" text-anchor="middle" font-family="Mitr" font-weight="600" font-size="26" fill="{INK}">PET CORNER</text>
</svg>'''
(SVG / 'poster_services.svg').write_text(poster1)

# ---------- poster: home theme 512x768 ----------
paws = ''
for (x, y, r, a) in [(70, 120, 18, -20), (430, 96, 14, 25), (456, 470, 16, 15), (60, 560, 13, -30), (400, 300, 11, 10)]:
    paws += f'''<g transform="translate({x} {y}) rotate({a})" fill="#D99E26" fill-opacity=".45">
   <ellipse cx="0" cy="{r * .6:.1f}" rx="{r}" ry="{r * .82:.1f}"/>
   <circle cx="{-r}" cy="{-r * .55:.1f}" r="{r * .38:.1f}"/><circle cx="{-r * .38:.1f}" cy="{-r:.1f}" r="{r * .38:.1f}"/>
   <circle cx="{r * .38:.1f}" cy="{-r:.1f}" r="{r * .38:.1f}"/><circle cx="{r}" cy="{-r * .55:.1f}" r="{r * .38:.1f}"/></g>'''
poster2 = f'''<svg xmlns="http://www.w3.org/2000/svg" width="512" height="768" viewBox="0 0 512 768">
 <rect x="8" y="8" width="496" height="752" rx="28" fill="#DCEBF7" stroke="{INK}" stroke-width="14"/>
 {paws}
 <text x="256" y="104" text-anchor="middle" font-family="Mitr" font-weight="600" font-size="52" fill="{INK}">บ้านชั่วคราว</text>
 <text x="256" y="166" text-anchor="middle" font-family="Mitr" font-weight="600" font-size="46" fill="#D9594D">ของเพื่อนตัวน้อย</text>
 {place('mark', 106, 196, 300)}
 <text x="256" y="566" text-anchor="middle" font-family="Mitr" font-weight="500" font-size="31" fill="{INK}">ดูแลเหมือนอยู่บ้าน</text>
 <text x="256" y="610" text-anchor="middle" font-family="Mitr" font-weight="500" font-size="31" fill="{INK}">จนกว่าเจ้าของจะมารับกลับ</text>
 {dots(124, 668, 16, 66)}
 <text x="256" y="738" text-anchor="middle" font-family="Mitr" font-weight="600" font-size="26" fill="{INK}">PET CORNER</text>
</svg>'''
(SVG / 'poster_home.svg').write_text(poster2)

# ---------- sparkle particle texture 128x128 (white, tinted by particle colour) ----------
sparkle = '''<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128">
 <defs><radialGradient id="g"><stop offset="0" stop-color="#fff" stop-opacity=".85"/><stop offset=".35" stop-color="#fff" stop-opacity=".25"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient></defs>
 <circle cx="64" cy="64" r="62" fill="url(#g)"/>
 <path d="M64 6 Q70 58 122 64 Q70 70 64 122 Q58 70 6 64 Q58 58 64 6 Z" fill="#fff"/>
</svg>'''
(SVG / 'Sparkle.svg').write_text(sparkle)
print('ok')
