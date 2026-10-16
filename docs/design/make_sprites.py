"""Parcel Factory 도트 스프라이트 생성기. 기준 해상도: 타일 24x18 (게임에서는 2배 = 48x36)."""
import os, sys
from PIL import Image, ImageDraw, ImageFont

OUT = sys.argv[1] if len(sys.argv) > 1 else "."
os.makedirs(OUT, exist_ok=True)

PAL = {
    '_': None,
    'k': (42, 24, 54),      # 외곽선
    's': (217, 166, 106), 'S': (196, 143, 85), 'z': (232, 189, 130), 'g': (138, 90, 46),
    't': (63, 163, 163), 'T': (42, 126, 126), 'u': (95, 208, 200), 'd': (30, 90, 90), 'D': (22, 68, 68),
    'r': (178, 74, 90), 'R': (106, 46, 58), 'q': (208, 106, 122),
    'p': (106, 82, 168), 'P': (62, 44, 110), 'l': (154, 130, 216),
    'w': (176, 122, 68), 'W': (122, 78, 42), 'o': (245, 240, 230), 'O': (216, 208, 192),
    'b': (78, 134, 232), 'B': (42, 78, 158), 'c': (138, 180, 255),
    'G': (63, 184, 90), 'H': (30, 90, 46), 'h': (82, 190, 104),
    'E': (217, 90, 74), 'e': (106, 42, 34), 'F': (240, 122, 106),
    'L': (255, 140, 26), 'M': (255, 210, 63), 'N': (58, 31, 71),
    'x': (91, 45, 110), 'X': (74, 36, 88), 'y': (232, 193, 77), 'Y': (201, 163, 58), 'v': (122, 74, 142),
    'a': (232, 184, 106), 'A': (154, 106, 58), 'm': (196, 138, 74), 'n': (245, 230, 200),
    'f': (240, 201, 160), 'i': (217, 167, 122), 'j': (255, 140, 26), 'J': (217, 110, 16),
    'C': (46, 139, 201), 'V': (27, 90, 142), 'Q': (42, 58, 110),
    '1': (255, 255, 255), '2': (255, 90, 90), '3': (201, 71, 47),
}

def new(w, h):
    return Image.new("RGBA", (w, h), (0, 0, 0, 0))

def put(img, x, y, ch):
    c = PAL[ch]
    if c is None: return
    if 0 <= x < img.width and 0 <= y < img.height:
        img.putpixel((x, y), c + (255,))

def rect(img, x0, y0, w, h, ch):
    for y in range(y0, y0 + h):
        for x in range(x0, x0 + w):
            put(img, x, y, ch)

def ascii_blit(img, rows, x0, y0):
    for dy, row in enumerate(rows):
        for dx, ch in enumerate(row):
            put(img, x0 + dx, y0 + dy, ch)

def outline(img, ch='k'):
    """투명 픽셀 중 불투명 픽셀과 맞닿은 곳을 외곽선 색으로 채운다 (4방향)."""
    src = img.copy()
    for y in range(img.height):
        for x in range(img.width):
            if src.getpixel((x, y))[3] != 0: continue
            for dx, dy in ((1,0),(-1,0),(0,1),(0,-1)):
                nx, ny = x + dx, y + dy
                if 0 <= nx < img.width and 0 <= ny < img.height and src.getpixel((nx, ny))[3] != 0:
                    put(img, x, y, ch); break
    return img

TW, TH = 24, 18

# ---------- 바닥 ----------
def floor_tile():
    im = new(TW, TH)
    rect(im, 0, 0, TW, TH, 's')
    rect(im, 0, 0, TW, 1, 'g'); rect(im, 0, 0, 1, TH, 'g')          # 줄눈
    rect(im, 1, 1, TW - 1, 1, 'z')                                   # 하이라이트
    for (x, y) in ((6, 6), (15, 4), (10, 12), (19, 13), (4, 15)):     # 모래 알갱이
        put(im, x, y, 'S')
    return im

def lava_tile(frame=0):
    """미해금 부지: 어두운 미개발 바닥에 용암 균열이 살짝 비친다."""
    im = new(TW, TH)
    rect(im, 0, 0, TW, TH, 'N')
    rect(im, 0, 0, TW, 1, 'k'); rect(im, 0, 0, 1, TH, 'k')
    cracks = [(5, 5), (6, 5), (7, 6), (8, 6), (15, 11), (16, 11), (17, 12), (12, 3), (19, 7), (3, 13), (4, 13)]
    for i, (x, y) in enumerate(cracks):
        put(im, x, y, 'M' if (i + frame) % 3 == 0 else 'L')
    return im

# ---------- 컨베이어 ----------
ARROW = {
    'E': ["__1__", "___1_", "11111", "___1_", "__1__"],
    'W': ["__1__", "_1___", "11111", "_1___", "__1__"],
    'N': ["__1__", "_111_", "1_1_1", "__1__", "__1__"],
    'S': ["__1__", "__1__", "1_1_1", "_111_", "__1__"],
}

def belt_tile(direction='E', fast=False, frame=0, front=3):
    im = new(TW, TH + front)
    body, stripe, railA, railB, side, sideD = ('r', 'R', 'q', 'R', 'R', 'k') if fast else ('t', 'd', 'u', 'T', 'd', 'D')
    rect(im, 0, 0, TW, TH, body)
    if direction in 'EW':
        for x in range(TW):
            if ((x + (frame * 2 if direction == 'E' else -frame * 2)) // 3) % 2 == 0:
                rect(im, x, 2, 1, TH - 4, stripe)
        rect(im, 0, 0, TW, 2, railA); rect(im, 0, TH - 2, TW, 2, railB)
    else:
        for y in range(TH):
            if ((y + (frame * 2 if direction == 'S' else -frame * 2)) // 3) % 2 == 0:
                rect(im, 2, y, TW - 4, 1, stripe)
        rect(im, 0, 0, 2, TH, railA); rect(im, TW - 2, 0, 2, TH, railB)
    ascii_blit(im, ARROW[direction], 10, 7)
    rect(im, 0, TH, TW, front, side); rect(im, 0, TH + front - 1, TW, 1, sideD)
    rect(im, 0, 0, 1, TH + front, 'k'); rect(im, TW - 1, 0, 1, TH + front, 'k')
    return im

def splitter_tile(direction='E', front=4):
    im = new(TW, TH + front)
    rect(im, 0, 0, TW, TH, 'p')
    rect(im, 0, 0, TW, 2, 'l'); rect(im, 0, 0, 2, TH, 'l')
    rect(im, 0, TH - 2, TW, 2, 'P'); rect(im, TW - 2, 0, 2, TH, 'P')
    y_shape = {
        'E': ["1_______1", "_1_____1_", "__1___1__", "111111111", "__1___1__", "_1_____1_", "1_______1"],
    }['E']
    # 들어오는 쪽에서 좌우로 갈라지는 Y: 방향별로 회전
    base = Image.new("RGBA", (9, 7), (0, 0, 0, 0)); ascii_blit(base, y_shape, 0, 0)
    glyph = {'E': base, 'W': base.transpose(Image.FLIP_LEFT_RIGHT), 'N': base.rotate(90, expand=True), 'S': base.rotate(-90, expand=True)}[direction]
    im.alpha_composite(glyph, ((TW - glyph.width) // 2, (TH - glyph.height) // 2))
    rect(im, 0, TH, TW, front, 'P'); rect(im, 0, TH + front - 1, TW, 1, 'k')
    rect(im, 0, 0, 1, TH + front, 'k'); rect(im, TW - 1, 0, 1, TH + front, 'k')
    return im

def table_tile(direction='E', front=5):
    im = new(TW, TH + front)
    rect(im, 0, 0, TW, TH, 'w')
    rect(im, 0, 0, TW, 1, 'z'); rect(im, 0, 0, 1, TH, 'z')
    rect(im, 4, 3, 16, 12, 'o'); rect(im, 4, 14, 16, 1, 'O')        # 검사지
    rect(im, 6, 6, 10, 1, 'k'); rect(im, 6, 9, 7, 1, 'k'); rect(im, 6, 12, 9, 1, '3')
    put(im, 20, 4, 'k'); put(im, 20, 5, 'k')                           # 펜
    rect(im, 0, TH, TW, front, 'W'); rect(im, 0, TH + front - 1, TW, 1, 'k')
    rect(im, 2, TH + 1, 2, front - 2, 'k'); rect(im, TW - 4, TH + 1, 2, front - 2, 'k')  # 다리
    rect(im, 0, 0, 1, TH + front, 'k'); rect(im, TW - 1, 0, 1, TH + front, 'k')
    a = Image.new("RGBA", (5, 5)); ascii_blit(a, ARROW[direction], 0, 0)
    im.alpha_composite(a, (17, 12))
    return im

def scanner_tile(direction='E', frame=0):
    im = belt_tile(direction, False, frame, front=3)
    if direction in 'EW':
        rect(im, 9, 0, 6, TH, 'b'); rect(im, 9, 0, 1, TH, 'B'); rect(im, 14, 0, 1, TH, 'B')
        rect(im, 11, 2, 2, TH - 4, '2' if frame == 0 else 'c')
    else:
        rect(im, 0, 6, TW, 6, 'b'); rect(im, 0, 6, TW, 1, 'B'); rect(im, 0, 11, TW, 1, 'B')
        rect(im, 2, 8, TW - 4, 2, '2' if frame == 0 else 'c')
    return im

# ---------- 도크 ----------
TRUCK = [
    "_1111111111111______",
    "_1111111111111OOO___",
    "_1111111111111OOOO__",
    "_1111111111111OOOO__",
    "_1111111111111OOOO__",
    "_1111111111111OOOO__",
    "__kk_______kk__kk___",
    "__kk_______kk__kk___",
]

def dock_tile(kind='inlet', front=6):
    top, edge, side = {'inlet': ('G', 'h', 'H'), 'outlet': ('b', 'c', 'B'), 'return': ('E', 'F', 'e')}[kind]
    im = new(TW, TH + front + 10)
    oy = 10
    rect(im, 0, oy, TW, TH, top)
    rect(im, 0, oy, TW, 2, edge); rect(im, 0, oy, 2, TH, edge)
    rect(im, 0, oy + TH - 2, TW, 2, side); rect(im, TW - 2, oy, 2, TH, side)
    for x in range(2, TW - 2, 4): rect(im, x, oy + TH - 5, 2, 1, edge)   # 바닥 홈
    rect(im, 0, oy + TH, TW, front, side); rect(im, 0, oy + TH + front - 1, TW, 1, 'k')
    rect(im, 0, oy, 1, TH + front, 'k'); rect(im, TW - 1, oy, 1, TH + front, 'k')
    truck = Image.new("RGBA", (20, 8)); ascii_blit(truck, TRUCK, 0, 0); outline(truck)
    im.alpha_composite(truck, (2, 2))
    return im

# ---------- 택배 ----------
PARCEL = [
    "_aaaannaaaa_",
    "aaaaannaaaaa",
    "aaaaannaaaaa",
    "nnnnnnnnnnnn",
    "aaaaannaaaaa",
    "aaaaannaaaaa",
    "_aaaannaaaa_",
    "mmmmmnnmmmmm",
    "mmmmmnnmmmmm",
    "AAAAAnnAAAAA",
    "AAAAAnnAAAAA",
]

def parcel(rejected=False):
    im = new(12, 11); ascii_blit(im, PARCEL, 0, 0)
    if rejected:
        rect(im, 7, 1, 4, 4, '3'); rect(im, 8, 2, 2, 2, '1')
    return outline(im)

# ---------- 작업자 ----------
WORKER_S = [
    "_____CCCCCC_____",
    "____CCCCCCCC____",
    "___CCCCCCCCCC___",
    "___VVVVVVVVVV___",
    "____ffffffff____",
    "___ffffffffff___",
    "___ffkffffkff___",
    "___ffffffffff___",
    "____ffiiiiff____",
    "_____ffffff_____",
    "_____jjjjjj_____",
    "___jjjjjjjjjj___",
    "__fjjjjjjjjjjf__",
    "__fjjjjJJjjjjf__",
    "__fjjjjJJjjjjf__",
    "___jjjjjjjjjj___",
    "___QQQQQQQQQQ___",
    "___QQQQ__QQQQ___",
    "___QQQQ__QQQQ___",
    "___QQQQ__QQQQ___",
    "___kkkk__kkkk___",
]
WORKER_N = [
    "_____CCCCCC_____",
    "____CCCCCCCC____",
    "___CCCCCCCCCC___",
    "___CCCCCCCCCC___",
    "____iiiiiiii____",
    "___iiiiiiiiii___",
    "___iiiiiiiiii___",
    "___iiiiiiiiii___",
    "____iiiiiiii____",
    "_____ffffff_____",
    "_____jjjjjj_____",
    "___jjjjjjjjjj___",
    "__fjjjjjjjjjjf__",
    "__fjjjjjjjjjjf__",
    "__fjjjjjjjjjjf__",
    "___jjjjjjjjjj___",
    "___QQQQQQQQQQ___",
    "___QQQQ__QQQQ___",
    "___QQQQ__QQQQ___",
    "___QQQQ__QQQQ___",
    "___kkkk__kkkk___",
]
WORKER_E = [
    "______CCCCC_____",
    "_____CCCCCCC____",
    "_____CCCCCCCC___",
    "_____VVVVVVVVV__",
    "______ffffff____",
    "_____fffffff____",
    "_____fffffkf____",
    "_____fffffff____",
    "______ffiiff____",
    "______ffff______",
    "______jjjj______",
    "_____jjjjjjj____",
    "_____jjjjjjjf___",
    "_____jjJJjjjf___",
    "_____jjjjjjjf___",
    "_____jjjjjjj____",
    "_____QQQQQQQ____",
    "_____QQQ_QQQ____",
    "_____QQQ_QQQ____",
    "_____QQQ_QQQ____",
    "_____kkk_kkk____",
]

def worker(facing='S', frame=0):
    rows = {'S': WORKER_S, 'N': WORKER_N, 'E': WORKER_E, 'W': WORKER_E}[facing]
    im = new(16, 21); ascii_blit(im, rows, 0, 0)
    if frame == 1:  # 걷기: 한쪽 다리 들기
        for y in (19, 20):
            for x in range(3, 8): put(im, x, y, '_') if False else None
        rect(im, 3, 20, 4, 1, '_') if False else None
        im2 = new(16, 21)
        for y in range(21):
            for x in range(16):
                p = im.getpixel((x, y))
                if p[3] and not (y >= 19 and x < 8): im2.putpixel((x, y), p)
                elif p[3] and y >= 19 and x < 8 and y == 19: im2.putpixel((x, y - 1), p)
        im = im2
    if facing == 'W': im = im.transpose(Image.FLIP_LEFT_RIGHT)
    return outline(im)

# ---------- 벽 ----------
def wall_tile():
    im = new(TW, 28)
    rect(im, 0, 0, TW, 4, 'v'); rect(im, 0, 3, TW, 1, 'X')
    rect(im, 0, 4, TW, 24, 'x')
    rect(im, 0, 4, 1, 24, 'v'); rect(im, TW - 1, 4, 1, 24, 'X')
    rect(im, 0, 15, TW, 3, 'y'); rect(im, 0, 18, TW, 1, 'Y')
    rect(im, 10, 7, 4, 5, 'k'); rect(im, 11, 8, 2, 3, 'X')   # 장식 홈
    rect(im, 0, 27, TW, 1, 'k')
    return im

# ---------- 조립 ----------
def up(im, k):
    return im.resize((im.width * k, im.height * k), Image.NEAREST)

def font(size):
    for p in ("/System/Library/Fonts/AppleSDGothicNeo.ttc", "/System/Library/Fonts/Supplemental/AppleGothic.ttf"):
        if os.path.exists(p):
            try: return ImageFont.truetype(p, size)
            except Exception: pass
    return ImageFont.load_default()

def sprite_sheet():
    K = 4
    items = [
        ("바닥", floor_tile()), ("미해금(용암) 1", lava_tile(0)), ("미해금(용암) 2", lava_tile(1)),
        ("레일 → 1", belt_tile('E', False, 0)), ("레일 → 2", belt_tile('E', False, 1)),
        ("레일 ↓", belt_tile('S')), ("레일 ↑", belt_tile('N')), ("레일 ←", belt_tile('W')),
        ("고속 레일 →", belt_tile('E', True, 0)), ("고속 레일 ↓", belt_tile('S', True, 0)),
        ("분배기 →", splitter_tile('E')), ("분배기 ↓", splitter_tile('S')),
        ("검사대 →", table_tile('E')), ("자동 검사기 → 1", scanner_tile('E', 0)), ("자동 검사기 → 2", scanner_tile('E', 1)),
        ("입고구", dock_tile('inlet')), ("출고구", dock_tile('outlet')), ("반품구", dock_tile('return')),
        ("택배", parcel()), ("택배(거부)", parcel(True)),
        ("작업자 앞", worker('S')), ("작업자 뒤", worker('N')), ("작업자 오른쪽", worker('E')), ("작업자 왼쪽", worker('W')), ("작업자 걷기", worker('S', 1)),
        ("뒷벽", wall_tile()),
    ]
    cols = 6; cell_w, cell_h = 150, 190
    rows = (len(items) + cols - 1) // cols
    sheet = Image.new("RGBA", (cols * cell_w + 20, rows * cell_h + 60), (27, 16, 36, 255))
    d = ImageDraw.Draw(sheet)
    f = font(16); ft = font(22)
    d.text((16, 14), "Parcel Factory 도트 스프라이트 시안 (기준 24×18, ×4 확대)", font=ft, fill=(245, 230, 200))
    for i, (label, im) in enumerate(items):
        cx = 20 + (i % cols) * cell_w; cy = 56 + (i // cols) * cell_h
        d.rectangle((cx - 6, cy - 6, cx + cell_w - 16, cy + cell_h - 24), outline=(58, 34, 72))
        big = up(im, K)
        sheet.alpha_composite(big, (cx + (cell_w - 22 - big.width) // 2, cy + (cell_h - 60 - big.height) // 2))
        d.text((cx, cy + cell_h - 52), label, font=f, fill=(245, 230, 200))
    return sheet

def scene(frame=0):
    W, H = 12, 12
    cells = {}
    for y in range(H):
        for x in range(W):
            cells[(x, y)] = ('lava' if (x >= 6 or y >= 6) else 'floor', 'E')
    layout = {
        (0, 2): ('inlet', 'E'), (1, 2): ('scanner', 'E'), (2, 2): ('table', 'E'), (3, 2): ('splitter', 'E'),
        (3, 3): ('belt', 'N'), (3, 4): ('belt', 'E'), (4, 4): ('belt', 'E'), (5, 4): ('return', 'E'),
        (3, 1): ('belt', 'E'), (4, 1): ('belt', 'N'), (4, 2): ('fast', 'E'), (5, 2): ('outlet', 'E'),
        (0, 0): ('belt', 'E'), (1, 0): ('belt', 'E'), (2, 0): ('belt', 'N'), (2, 1): ('belt', 'N'),
    }
    cells.update(layout)
    WALL = 28
    base = Image.new("RGBA", (W * TW, H * TH + WALL + 6), (27, 16, 36, 255))
    for x in range(W): base.alpha_composite(wall_tile(), (x * TW, 0))
    oy = WALL
    sy = lambda gy: oy + (H - 1 - gy) * TH
    for gy in range(H - 1, -1, -1):
        for gx in range(W):
            kind, _ = cells[(gx, gy)]
            base.alpha_composite(lava_tile(frame) if kind == 'lava' else floor_tile(), (gx * TW, sy(gy)))
    parcels = {(1, 2): False, (2, 2): False, (3, 4): True, (4, 2): False, (1, 0): False}
    worker_at, facing = (2, 3), 'S'
    for gy in range(H - 1, -1, -1):
        for gx in range(W):
            kind, direction = cells[(gx, gy)]
            if kind == 'belt': im, dy = belt_tile(direction, False, frame), 0
            elif kind == 'fast': im, dy = belt_tile(direction, True, frame), 0
            elif kind == 'splitter': im, dy = splitter_tile(direction), 0
            elif kind == 'table': im, dy = table_tile(direction), 0
            elif kind == 'scanner': im, dy = scanner_tile(direction, frame), 0
            elif kind in ('inlet', 'outlet', 'return'): im, dy = dock_tile(kind), -10
            else: continue
            base.alpha_composite(im, (gx * TW, sy(gy) + dy))
            if (gx, gy) in parcels:
                p = parcel(parcels[(gx, gy)])
                lift = 6 if kind == 'table' else 4
                base.alpha_composite(p, (gx * TW + 6, sy(gy) + 3 - lift))
        if (worker_at[1]) == gy:
            wimg = worker(facing)
            base.alpha_composite(wimg, (worker_at[0] * TW + 4, sy(gy) - 6))
            # 바라보는 칸 커서
            fx, fy = worker_at[0], worker_at[1] - 1
            d = ImageDraw.Draw(base)
            d.rectangle((fx * TW, sy(fy), fx * TW + TW - 1, sy(fy) + TH - 1), outline=(255, 224, 138), width=1)
    big = up(base, 3)
    # 간단한 HUD
    hud = Image.new("RGBA", (big.width, big.height + 48), (27, 16, 36, 255))
    d = ImageDraw.Draw(hud)
    d.rectangle((0, 0, big.width, 44), fill=(42, 24, 54)); d.rectangle((0, 44, big.width, 47), fill=(232, 193, 77))
    f = font(18)
    d.rectangle((12, 8, 170, 36), fill=(232, 193, 77)); d.text((20, 12), "PARCEL FACTORY", font=font(16), fill=(42, 24, 54))
    d.text((190, 11), "₩ 100", font=f, fill=(255, 224, 138)); d.text((290, 11), "배송 0   소실 0", font=f, fill=(245, 230, 200))
    d.text((big.width - 300, 11), "작업자: 빈손   [II] [1x] [2x] [상점]", font=f, fill=(245, 230, 200))
    hud.alpha_composite(big, (0, 48))
    return hud

sprite_sheet().convert("RGB").save(os.path.join(OUT, "sprite-sheet.png"))
frames = [scene(0).convert("RGB"), scene(1).convert("RGB")]
frames[0].save(os.path.join(OUT, "scene-mock.png"))
frames[0].save(os.path.join(OUT, "scene-mock.gif"), save_all=True, append_images=[frames[1]], duration=220, loop=0)
# 원본 해상도 시트도 저장(게임용)
raw = Image.new("RGBA", (TW * 8, 60), (0, 0, 0, 0))
for i, im in enumerate([floor_tile(), lava_tile(0), belt_tile('E'), belt_tile('S'), belt_tile('E', True), splitter_tile('E'), table_tile('E'), scanner_tile('E')]):
    raw.alpha_composite(im, (i * TW, 0))
raw.alpha_composite(dock_tile('inlet'), (0, 26)); raw.alpha_composite(dock_tile('outlet'), (24, 26)); raw.alpha_composite(dock_tile('return'), (48, 26))
raw.alpha_composite(parcel(), (80, 30)); raw.alpha_composite(parcel(True), (96, 30))
raw.alpha_composite(worker('S'), (116, 28)); raw.alpha_composite(worker('N'), (134, 28)); raw.alpha_composite(worker('E'), (152, 28)); raw.alpha_composite(wall_tile(), (172, 28))
raw.save(os.path.join(OUT, "sprites-1x.png"))
print("ok")
