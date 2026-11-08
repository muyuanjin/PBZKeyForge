"""Shared data and public HTML readers for the publishing commands."""
import json
import re
from html.parser import HTMLParser
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
CONFIG = json.loads((ROOT / 'build/nexus-publishing.json').read_text(encoding='utf-8'))


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def write_json(path, data):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    temporary.replace(path)


def version_text(value):
    if not re.fullmatch(r'\d+\.\d+\.\d+', value):
        raise ValueError('Use a three-part release version, for example 1.3.2.')
    return value


class Node:
    def __init__(self, tag='', attrs=(), parent=None):
        self.tag, self.attrs, self.parent, self.children = tag, dict(attrs), parent, []

    def walk(self):
        yield self
        for child in self.children:
            if isinstance(child, Node):
                yield from child.walk()

    def text(self):
        if self.tag in ('script', 'style', 'textarea'):
            return ''
        return ' '.join(c.text() if isinstance(c, Node) else c for c in self.children)

    def has_class(self, name):
        return name in self.attrs.get('class', '').split()


class Document(HTMLParser):
    VOID = {'area', 'base', 'br', 'col', 'embed', 'hr', 'img', 'input', 'link', 'meta', 'param', 'source', 'track', 'wbr'}

    def __init__(self, html):
        super().__init__(convert_charrefs=True)
        self.root = self.current = Node()
        self.feed(html)

    def handle_starttag(self, tag, attrs):
        node = Node(tag, attrs, self.current)
        self.current.children.append(node)
        if tag not in self.VOID:
            self.current = node

    def handle_endtag(self, tag):
        node = self.current
        while node.parent:
            if node.tag == tag:
                self.current = node.parent
                return
            node = node.parent

    def handle_startendtag(self, tag, attrs):
        self.handle_starttag(tag, attrs)
        if tag not in self.VOID:
            self.handle_endtag(tag)

    def handle_data(self, text):
        self.current.children.append(text)

    def find(self, predicate):
        return [n for n in self.root.walk() if predicate(n)]


def public_gallery(html):
    return [n.attrs['src'].rsplit('/', 1)[-1] for n in Document(html).find(
        lambda n: n.tag == 'img' and n.attrs.get('id', '').startswith('mod-image-'))]


def public_version(html):
    doc = Document(html)
    values = []
    for item in doc.find(lambda n: n.has_class('stat-version')):
        values.extend(' '.join(n.text().split()) for n in item.walk() if n.has_class('stat'))
    if len(values) != 1:
        raise RuntimeError('Public mod header version is missing or ambiguous.')
    return values[0]


def public_files(html):
    category = None
    result = []
    for node in Document(html).root.walk():
        text = ' '.join(node.text().split()) if node.tag in ('h2', 'h3') else ''
        if text in ('Main files', 'Optional files', 'Old files', 'Old versions'):
            category = {'Main files': 'main', 'Optional files': 'optional'}.get(text)
        if node.tag == 'dt' and node.attrs.get('data-version'):
            result.append({'category': category, 'version': node.attrs['data-version'],
                           'id': node.attrs.get('data-id')})
    return result


def body_images(html):
    doc = Document(html)
    bodies = doc.find(lambda n: n.has_class('mod_description_container'))
    if len(bodies) != 1:
        raise RuntimeError('Public description container is missing or ambiguous.')
    return [n.attrs['src'] for n in bodies[0].walk() if n.tag == 'img' and n.attrs.get('src')]


def ref_for(snapshot, role, label, optional=False):
    matches = re.findall(r'(@e\d+) ' + re.escape(role) + ' "' + re.escape(label) + r'"(?:\s|$)', snapshot)
    if len(matches) == 1:
        return matches[0]
    if optional and not matches:
        return None
    raise RuntimeError('Expected one {} {!r}; found {}. Inspect the current page.'.format(role, label, len(matches)))
