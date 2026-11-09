"""Generate the project's fictional lettering atlas, outside Unity's Editor UI.

Needs Pillow and Personal/AssetResearch/NotoSansTC-Regular.otf (SIL OFL 1.1).
The output contains raster lettering; the original font is not redistributed.
"""
from pathlib import Path
import json
import sys
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'VR-Motor-Street-Riding'
sys.path.insert(0, str(PROJECT / 'Personal/CityBuildPython'))
from PIL import Image, ImageDraw, ImageFont

source = PROJECT / 'Personal/AssetResearch'
destination = PROJECT / 'Assets/MotorSimulatorAssets/Models/CitySignage'
destination.mkdir(parents=True, exist_ok=True)
font_path = source / 'NotoSansTC-Regular.otf'
if not font_path.exists():
    font_path.write_bytes(urllib.request.urlopen('https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/SubsetOTF/TC/NotoSansTC-Regular.otf', timeout=40).read())
(destination / 'FontLicense.txt').write_bytes(urllib.request.urlopen('https://raw.githubusercontent.com/notofonts/noto-cjk/main/Sans/LICENSE', timeout=30).read())
names = ['和平路', '民生路', '建國路', '光華路', '信義路', '市民路', '中正路', '長安路',
         '仁愛街', '青田街', '東華街', '新安街', '明德街', '中興街', '大同街', '建成街',
         '街角茶舖', '好日早餐', '城市書店', '民生藥局', '小巷食堂', '日常咖啡', '機車維修', '生活百貨']
font = ImageFont.truetype(str(font_path), 76)
image = Image.new('RGB', (2048, 1024), (34, 46, 43))
draw = ImageDraw.Draw(image)
palette = [(29, 75, 58), (128, 59, 40), (48, 70, 91), (155, 115, 44),
           (43, 90, 79), (92, 58, 43), (45, 66, 94), (112, 57, 51)]
for i, label in enumerate(names):
    x, y = i % 4 * 512, i // 4 * 128
    colour = (22, 75, 51) if i < 16 else palette[(i - 16) % 8]
    draw.rectangle((x, y, x + 511, y + 127), fill=colour)
    draw.rectangle((x + 6, y + 6, x + 505, y + 121), outline=(217, 223, 199), width=3)
    draw.text((x + 256, y + 62), label, font=font, fill=(244, 243, 218), anchor='mm', stroke_width=1)
image.save(destination / 'SignAtlas.png')
stencil = Image.new('RGBA', (512, 256), (255, 255, 255, 0))
ImageDraw.Draw(stencil).text((256, 120), '機車', font=ImageFont.truetype(str(font_path), 200), fill=(255, 255, 255, 255), anchor='mm', stroke_width=2)
stencil.save(destination / 'MotorcycleStencil.png')
(destination / 'source.json').write_text(json.dumps({
    'generator': 'Tools/create_city_signage.py', 'font': 'Noto Sans TC Regular',
    'font_source': 'https://github.com/notofonts/noto-cjk', 'font_license': 'SIL Open Font License 1.1',
    'labels': names, 'note': 'Fictional labels, not a map of real Taipei street positions.'
}, ensure_ascii=True, indent=2), encoding='utf-8')
print('Saved 24 fictional Chinese street/shop labels and the font license.')
