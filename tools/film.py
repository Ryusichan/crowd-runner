"""연속 촬영(docs/shots/burstN_XX.png)을 GIF 필름으로 묶는다.

반응(비틀거림 · 반동 · 쏟아짐 · 쓰러짐)은 움직임이라 정지 사진으로는 확인이 안 된다
(2026-10-20). BarrelShotTests 가 실시간 속도로 찍은 장면을 여기서 이어 붙인다.

    python tools/film.py            # docs/shots/burst1.gif, burst4.gif
"""
import glob
import os
import sys

from PIL import Image

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'docs', 'shots')


def main():
    made = 0
    for tag in sorted({os.path.basename(p).split('_')[0] for p in glob.glob(os.path.join(ROOT, 'burst*_*.png'))}):
        frames = sorted(glob.glob(os.path.join(ROOT, tag + '_*.png')))
        if not frames:
            continue
        imgs = [Image.open(f).convert('RGB') for f in frames]
        # 540x960 → 360x640 — 저장소에 싣기 가볍게
        imgs = [im.resize((360, 640), Image.LANCZOS) for im in imgs]
        out = os.path.join(ROOT, tag + '.gif')
        imgs[0].save(out, save_all=True, append_images=imgs[1:], duration=67, loop=0, optimize=True)
        print(out, len(imgs), 'frames', os.path.getsize(out) // 1024, 'KB')
        made += 1
    if made == 0:
        print('burst*_*.png 가 없다 — BarrelShotTests 를 먼저 돌려라')
        return 1
    return 0


if __name__ == '__main__':
    sys.exit(main())
