import sys
from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont
from fontTools import subset
src, wght, out, kind = sys.argv[1], int(sys.argv[2]), sys.argv[3], sys.argv[4]
f = TTFont(src)
inst = instantiateVariableFont(f, {"wght": wght}, updateFontNames=True)
ranges = [(0x20,0x7E),(0xA0,0xFF),(0x2000,0x206F),(0x2190,0x21FF),(0x20A9,0x20A9),(0x2460,0x24FF),(0x25A0,0x25FF),(0x2600,0x26FF),(0x2700,0x27BF),(0x3000,0x303F),(0x3130,0x318F),(0xAC00,0xD7A3),(0xFF01,0xFF5E),(0x2100,0x214F),(0x2200,0x22FF)]
if kind == 'latin': ranges = [(0x20,0x7E),(0xA0,0xFF),(0x2000,0x206F),(0x2190,0x21FF),(0x20A9,0x20A9),(0x2200,0x22FF)]
uni = [c for a,b in ranges for c in range(a,b+1)]
opt = subset.Options(); opt.layout_features=['*']; opt.name_IDs=['*']; opt.notdef_outline=True; opt.hinting=True
s = subset.Subsetter(opt); s.populate(unicodes=uni); s.subset(inst)
inst.save(out); print(out, wght)
