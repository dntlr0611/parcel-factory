import os
W='#fff'
I = {
 # 도구 (시안 ICONS, viewBox 16)
 'tool-conveyor': ('0 0 16 16', f'<path d="M2 8h9M8 4l4 4-4 4" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'),
 'tool-fast': ('0 0 16 16', f'<path d="M1 8h6M6 4l4 4-4 4M8 4l4 4-4 4" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'),
 'tool-table': ('0 0 16 16', f'<rect x="2" y="3" width="12" height="9" rx="1.5" fill="none" stroke="{W}" stroke-width="1.8"/><path d="M5 6.5h6M5 9h4" stroke="{W}" stroke-width="1.6" stroke-linecap="round"/>'),
 'tool-pallet': ('0 0 16 16', f'<rect x="3" y="7" width="10" height="5" rx="1" fill="none" stroke="{W}" stroke-width="1.6"/><path d="M2 13.5h12" stroke="{W}" stroke-width="1.6" stroke-linecap="round"/><rect x="5" y="3" width="6" height="4" rx="0.8" fill="{W}"/>'),
 'tool-splitter': ('0 0 16 16', f'<path d="M2 8h5l4-4M7 8l4 4" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'),
 'tool-bridge': ('0 0 16 16', f'<path d="M1.5 5.5h9M8 3l3 2.5L8 8" fill="none" stroke="{W}" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/><path d="M4 8v6M12 8v6M2 14.5h12" fill="none" stroke="{W}" stroke-width="1.5" stroke-linecap="round"/>'),
 'tool-scanner': ('0 0 16 16', f'<path d="M3 12V5a2 2 0 0 1 2-2h6a2 2 0 0 1 2 2v7" fill="none" stroke="{W}" stroke-width="1.8"/><path d="M5 8h6" stroke="#ff6b6b" stroke-width="2" stroke-linecap="round"/>'),
 # 상단 버튼 (시안, viewBox 24)
 'ui-pause': ('0 0 24 24', f'<rect x="6" y="5" width="4" height="14" rx="1" fill="{W}"/><rect x="14" y="5" width="4" height="14" rx="1" fill="{W}"/>'),
 'ui-play': ('0 0 24 24', f'<path d="M8 5.5v13a1 1 0 0 0 1.5.86l10.5-6.5a1 1 0 0 0 0-1.72L9.5 4.64A1 1 0 0 0 8 5.5z" fill="{W}"/>'),
 'ui-shop': ('0 0 24 24', f'<path d="M3 4h2l2.4 11h11.2L21 8H7" fill="none" stroke="{W}" stroke-width="2" stroke-linejoin="round" stroke-linecap="round"/><circle cx="9" cy="19" r="1.5" fill="{W}"/><circle cx="17" cy="19" r="1.5" fill="{W}"/>'),
 'ui-status': ('0 0 24 24', f'<circle cx="12" cy="8" r="3.6" fill="none" stroke="{W}" stroke-width="2"/><path d="M5 20c0-3.6 3.1-6 7-6s7 2.4 7 6" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round"/>'),
 'ui-help': ('0 0 24 24', f'<circle cx="12" cy="12" r="9" fill="none" stroke="{W}" stroke-width="2"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.6 2.2c-.7.4-1.1.9-1.1 1.8" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round"/><circle cx="12" cy="17" r="1.1" fill="{W}"/>'),
 # 시작 화면 · 파일 (같은 선 굵기로 새로 그림)
 'ui-save': ('0 0 24 24', f'<path d="M5 4h11l3 3v12a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V4z" fill="none" stroke="{W}" stroke-width="2" stroke-linejoin="round"/><path d="M8 4v5h7V4M8 20v-6h8v6" fill="none" stroke="{W}" stroke-width="2" stroke-linejoin="round"/>'),
 'ui-load': ('0 0 24 24', f'<path d="M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z" fill="none" stroke="{W}" stroke-width="2" stroke-linejoin="round"/>'),
 'ui-new': ('0 0 24 24', f'<path d="M12 5v14M5 12h14" fill="none" stroke="{W}" stroke-width="2.4" stroke-linecap="round"/>'),
 'ui-exit': ('0 0 24 24', f'<path d="M10 4H6a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h4" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round"/><path d="M14 8l4 4-4 4M18 12H9" fill="none" stroke="{W}" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>'),
 'ui-close': ('0 0 24 24', f'<path d="M6 6l12 12M18 6L6 18" fill="none" stroke="{W}" stroke-width="2.2" stroke-linecap="round"/>'),
 'ui-check': ('0 0 24 24', f'<path d="M5 12.5l4.5 4.5L19 7.5" fill="none" stroke="{W}" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/>'),
 'ui-parcel': ('0 0 24 24', f'<path d="M12 3l8 4.5v9L12 21l-8-4.5v-9L12 3z" fill="none" stroke="{W}" stroke-width="1.8" stroke-linejoin="round"/><path d="M4 7.5l8 4.5 8-4.5M12 12v9M8 5.3l8 4.5" fill="none" stroke="{W}" stroke-width="1.8" stroke-linejoin="round"/>'),
}
for k,(vb,body) in I.items():
    open(f'svg/{k}.html','w').write(f'<html><body style="margin:0;background:transparent"><svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" width="64" height="64" style="display:block">{body}</svg></body></html>')
print(' '.join(I))


# 항공 택배(스펙 14): 흰 비행기 아이콘과 상자 윗면 마크(파란 둥근 사각 + 흰 테두리, 128px)
PLANE='M12 2.5c.9 0 1.5.8 1.5 1.8V9l7.5 4.3v2.2l-7.5-2.2v4.6l2 1.5V21l-3.5-1-3.5 1v-1.6l2-1.5v-4.6L3 15.5v-2.2L10.5 9V4.3c0-1 .6-1.8 1.5-1.8z'
open('svg/ui-air.html','w').write(f'<html><body style="margin:0;background:transparent"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="64" height="64" style="display:block"><path d="{PLANE}" fill="#fff"/></svg></body></html>')
open('svg/mark-air.html','w').write(f'<html><body style="margin:0;background:transparent"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="128" height="128" style="display:block"><rect x="1.5" y="1.5" width="29" height="29" rx="6" fill="#2f86eb" stroke="#fff" stroke-width="2"/><g transform="translate(5 5) scale(0.9167)"><path d="{PLANE}" fill="#fff"/></g></svg></body></html>')
# mark-air는 --window-size=128,128로 굽는다.

# 취급주의(스펙 17): ui-fragile(64), mark-fragile(128), mark-crack(128). 원본은 scratchpad fragile.py와 같다.
# GLASS='<path d="M6.5 3h11l-.8 5.6a4.75 4.75 0 0 1-9.4 0z" fill="none" stroke="#fff" stroke-width="1.9" stroke-linejoin="round"/><path d="M10.6 3.6l1.6 2.4-1.5 1.6 1.7 2.2" fill="none" stroke="#fff" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/><path d="M12 13.4V19M8 20.5h8" fill="none" stroke="#fff" stroke-width="1.9" stroke-linecap="round"/>'
# open('svg/ui-fragile.html','w').write(f'<html><body style="margin:0;background:transparent"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" width="64" height="64" style="display:block">{GLASS}</svg></body></html>')
# open('svg/mark-fragile.html','w').write(f'<html><body style="margin:0;background:transparent"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32" width="128" height="128" style="display:block"><rect x="1.5" y="1.5" width="29" height="29" rx="6" fill="#f28b30" stroke="#fff" stroke-width="2"/><g transform="translate(4 4)">{GLASS}</g></svg></body></html>')
# CRACK='<g fill="none" stroke="#2a1d12" stroke-linecap="round" stroke-linejoin="round" opacity="0.85"><path d="M10 6l14 18-4 10 16 12-6 14 22 20" stroke-width="2.4"/><path d="M24 24l14-6 10 6" stroke-width="1.8"/><path d="M36 46l20-4 8 10" stroke-width="1.8"/><path d="M50 60l4 22 18 8" stroke-width="2.2"/><path d="M60 52l26-10 12 6 18-4" stroke-width="2"/><path d="M98 48l6 20 14 6" stroke-width="1.6"/><path d="M72 90l-10 18 4 14" stroke-width="1.8"/><path d="M86 42l2-18 16-12" stroke-width="1.6"/></g>'
# open('svg/mark-crack.html','w').write(f'<html><body style="margin:0;background:transparent"><svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 128 128" width="128" height="128" style="display:block">{CRACK}</svg></body></html>')
