from pathlib import Path
import copy
import json
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent
SOURCES = ROOT / 'sources'
SVG = 'http://www.w3.org/2000/svg'
ET.register_namespace('', SVG)


def canvas(width, height):
    return ET.Element('{' + SVG + '}svg', {'width': str(width), 'height': str(height), 'viewBox': f'0 0 {width} {height}'})


def append(target, name):
    for child in ET.parse(SOURCES / name).getroot():
        target.append(copy.deepcopy(child))


def crop(target, name, x, y, width, height, source_x=0):
    window = ET.SubElement(target, '{' + SVG + '}svg', {'x': str(x), 'y': str(y), 'width': str(width), 'height': str(height),
        'viewBox': f'{source_x} 0 {width} {height}', 'overflow': 'hidden'})
    append(window, name)


def write(name, node, width):
    path = ROOT / (name + '.svg')
    ET.ElementTree(node).write(path, encoding='utf-8', xml_declaration=True)
    subprocess.run(['magick', '-background', 'none', '-density', '192', str(path), '-resize', str(width) + 'x', str(ROOT / (name + '-svg.png'))], check=True)


xbox = canvas(750, 630.455)
append(xbox, 'xbox-one-base-white.svg')
for x, y, offset in [(540, 303, 0), (590, 253, 53), (490, 253, 106), (540, 202, 159)]:
    crop(xbox, 'xbox-one-buttons.svg', x, y, 53, 53, offset)
for x, y in [(144, 239), (432, 352)]:
    crop(xbox, 'xbox-one-stick.svg', x, y, 83, 83, 85)
write('xbox', xbox, 1600)

playstation = canvas(806, 598)
append(playstation, 'ds4-base-white.svg')
for x, y, offset in [(623, 273, 0), (681, 215, 56), (567, 215, 112), (623, 159, 168)]:
    crop(playstation, 'ds4-buttons.svg', x, y, 56, 56, offset)
for x, y in [(228, 308), (484, 308)]:
    crop(playstation, 'ds4-sticks.svg', x, y, 94, 94)
for x, y, width, height, offset in [(136, 181, 36, 52, 37), (136, 255, 36, 52, 0), (92, 225, 52, 36, 74.5), (165, 225, 52, 36, 126.5)]:
    crop(playstation, 'ds4-dpad.svg', x, y, width, height, offset)
write('playstation', playstation, 1600)

mouse = ET.parse(SOURCES / 'lucide-mouse.svg').getroot()
mouse.set('width', '480')
mouse.set('height', '576')
mouse.set('viewBox', '2 0 20 24')
mouse.set('stroke', '#BCC3C6')
mouse.set('stroke-width', '0.6')
for child in list(mouse):
    if child.tag.endswith('path'):
        mouse.remove(child)
list(mouse)[0].set('fill', '#242B30')
ET.SubElement(mouse, '{' + SVG + '}path', {'d': 'M12 2v3.2 M5.2 10h13.6', 'stroke-width': '0.5'})
ET.SubElement(mouse, '{' + SVG + '}rect', {'x': '11.15', 'y': '5.2', 'width': '1.7', 'height': '3.5', 'rx': '0.85', 'fill': '#161C20', 'stroke-width': '0.45'})
for y in [10, 14]:
    ET.SubElement(mouse, '{' + SVG + '}rect', {'x': '2.9', 'y': str(y), 'width': '1.4', 'height': '2.8', 'rx': '0.7', 'fill': '#303A40', 'stroke-width': '0.45'})
write('mouse', mouse, 800)
