"""Prepare and resume Nexus publication through gh, browser-skill and the Windows picker."""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time
from pathlib import Path

from publishing_common import (CONFIG, ROOT, Document, body_images, public_files,
                               public_gallery, public_version, read_json, ref_for, version_text, write_json)


def run(argv, json_output=False):
    result = subprocess.run([str(a) for a in argv], cwd=ROOT, capture_output=True,
                            encoding='utf-8', env={**os.environ, 'BSK_AUTO_START': '0'})
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip() or 'Command failed: ' + str(argv[0]))
    if json_output:
        data = json.loads(result.stdout)
        if isinstance(data, dict) and data.get('ok') is False:
            raise RuntimeError('Browser script failed: ' + str(data.get('error', data)))
        return data
    return result.stdout


def gh(*args):
    return run(['gh', *args, '--repo', CONFIG['repository']], json_output=True)


def plan_path(version):
    return ROOT / 'docs' / ('nexus-media-' + version + '.json')


def load_plan(version):
    plan = read_json(plan_path(version))
    if plan['version'] != version:
        raise ValueError('Media manifest version differs from requested version.')
    return plan


def validate_prepared(plan, stage):
    if stage == 'files':
        return
    expected = [scene + '-' + language for language in ('en', 'zh-Hans') for scene in (
        'enhancement-main', 'enhancement-main-gamepad', 'enhancement-main-playstation', 'enhancement-actions')]
    if [i['key'] for i in plan['images'] if i['visible']] != expected:
        raise ValueError('Media manifest must contain the eight full application views in project order.')
    if stage in ('media', 'finish'):
        missing = [i['source'] for i in plan['images'] if not (ROOT / i['source']).is_file()]
        if missing:
            raise ValueError('Missing prepared screenshots: ' + ', '.join(missing))
    if stage in ('pages', 'finish'):
        sticky = (ROOT / 'docs/nexus-pinned-post.bbcode.txt').read_text(encoding='utf-8-sig')
        short = (ROOT / 'docs/nexus-short-description.txt').read_text(encoding='utf-8-sig').strip()
        body = (ROOT / 'docs/nexus-description.bbcode.txt').read_text(encoding='utf-8-sig')
        if '[b]V' + plan['version'] + '[/b]' not in sticky or len(sticky) > 5000:
            raise ValueError('Prepare the current sticky version and keep it within 5000 characters before publication.')
        if not (0 < len(short) <= 350) or len(re.findall(r'\[img\]', body, re.I)) > 8:
            raise ValueError('Prepare a summary of 1–350 characters and at most eight body illustrations.')


def save_plan(plan):
    write_json(plan_path(plan['version']), plan)


def prepare(version, previous):
    previous = version_text(previous)
    old = load_plan(previous)
    path = plan_path(version)
    if path.exists():
        plan = load_plan(version)
        if plan['previous_version'] != previous:
            raise ValueError('Existing media plan has another previous version; preserve its checkpoints.')
    else:
        plan = {'version': version, 'previous_version': previous, 'images': []}
        for item in old['images']:
            source = 'assets/screenshots/{}/{}.png'.format(version, item['key'])
            plan['images'].append({**item, 'source': source,
                                   'title': item['title'].replace(previous, version), 'url': None})
    missing = [i['source'] for i in plan['images'] if not (ROOT / i['source']).is_file()]
    if missing:
        raise ValueError('Capture required screenshots first: ' + ', '.join(missing))
    save_plan(plan)
    print('Prepared', path.relative_to(ROOT), '(existing URLs and pending uploads preserved).')


class Browser:
    def __init__(self, browser_id):
        self.exe = shutil.which('bsk')
        if not self.exe:
            raise RuntimeError('bsk is missing. Read browser-skill environment guidance.')
        self.session = None
        run([self.exe, 'daemon', 'start', '--port', '28000'])
        run([self.exe, 'status', '--json'], True)
        info = run([self.exe, 'session', 'start', '--browser', browser_id, '--json'], True)
        self.session = info['session_id']
        # A new task tab is about:blank and has no observable page frame yet.
        # BrowserSkill permits its initial navigation before the first sync.
        try:
            self.call('navigate', CONFIG['page'], json_output=False)
            self.sync()
        except Exception:
            self.close()
            raise

    def call(self, *args, json_output=True):
        return run([self.exe, *args, '--session', self.session] + (['--json'] if json_output else []), json_output)

    def sync(self):
        state = self.call('cooperate', 'sync', '--settle')
        if not state.get('context_id'):
            raise RuntimeError('Browser control is unavailable. Follow browser-skill cooperation recovery.')
        return state

    def action(self, command, *args):
        state = self.sync()
        return self.call(command, *args, '--context', state['context_id'], json_output=False)

    def navigate(self, url):
        state = self.sync()
        self.call('navigate', url, '--context', state['context_id'],
                  '--observation', state['observation_id'], json_output=False)
        self.sync()

    def snapshot(self):
        return self.call('snapshot')['text']

    def activate(self, role, label):
        target = ref_for(self.snapshot(), role, label)
        # Keyboard activation avoids floating-menu relocation while the page scrolls.
        self.action('press', 'Enter', '--ref', target)

    def evaluate(self, expression):
        state = self.sync()
        data = self.call('evaluate', expression, '--context', state['context_id'],
                         '--observation', state['observation_id'])
        return data['value']

    def html(self, name):
        path = ROOT / 'dist/publishing' / name
        path.parent.mkdir(parents=True, exist_ok=True)
        state = self.sync()
        self.call('get-html', '--max-bytes', '1500000', '--out', path, '--quiet',
                  '--context', state['context_id'], '--observation', state['observation_id'],
                  json_output=False)
        html = path.read_text(encoding='utf-8')
        if not html.rstrip().endswith('</html>'):
            raise RuntimeError('Incomplete page HTML; inspect the current page before proceeding.')
        return html

    def wait(self, predicate, message, seconds=30):
        deadline = time.monotonic() + seconds
        while True:
            result = predicate()
            if result:
                return result
            if time.monotonic() >= deadline:
                raise RuntimeError(message + ' Inspect the website before resuming; do not repeat an uncertain submission.')
            time.sleep(0.5)

    def close(self):
        if self.session:
            run([self.exe, 'session', 'stop', self.session])
            self.session = None


MEDIA_STATE = r'''(() => ({
 images: Array.from(document.querySelectorAll('li')).filter(li => li.querySelector('img[src*="/images/402/"]')).map(li => ({
   url: li.querySelector('img').src,
   title: li.querySelector('img').alt,
   hidden: Array.from(li.querySelectorAll('span')).some(s => s.textContent.trim() === 'Hidden'),
   thumbnail: Array.from(li.querySelectorAll('span')).some(s => s.textContent.trim() === 'Thumbnail')
 })),
 hiddenExpanded: Array.from(document.querySelectorAll('button')).find(b => b.textContent.startsWith('Hidden images'))?.getAttribute('aria-expanded')
}))()'''


def media_state(browser):
    state = browser.evaluate(MEDIA_STATE)
    if state['hiddenExpanded'] == 'false':
        text = browser.snapshot()
        match = re.search(r'(@e\d+) button "Hidden images \(', text)
        if not match:
            raise RuntimeError('Hidden images control changed; inspect Media.')
        browser.action('press', 'Enter', '--ref', match[1])
        state = browser.evaluate(MEDIA_STATE)
    return state['images']


def image_selector(url):
    return 'li:has(img[src=' + json.dumps(url) + '])'


def menu(browser, url, label):
    browser.action('click', image_selector(url) + ' button[aria-haspopup="menu"]')
    browser.activate('menuitem', label)


def reconcile_pending(plan, images):
    pending = plan.get('pending_upload')
    if not pending:
        return
    new = [i for i in images if i['url'] not in pending['before']]
    if len(new) != 1:
        raise RuntimeError('An upload is pending. Inspect the native picker and Media; resolve its one new URL in the manifest before resuming.')
    item = next(i for i in plan['images'] if i['key'] == pending['key'])
    item['url'] = new[0]['url']
    del plan['pending_upload']
    save_plan(plan)


def upload_and_style(browser, plan):
    browser.navigate(CONFIG['editor'] + '/media')
    browser.wait(lambda: 'Add image(s)' in browser.snapshot(), 'Media editor did not load.')
    # Enough room for all nine gallery entries to remain visible during a drag.
    state = browser.sync()
    browser.call('emulate', '--width', '1692', '--height', '1800', '--no-mobile', '--no-touch',
                 '--context', state['context_id'], '--observation', state['observation_id'], json_output=False)
    images = media_state(browser)
    reconcile_pending(plan, images)
    for item in plan['images']:
        images = media_state(browser)
        named = [i for i in images if i['title'] == item['title']]
        if not item['url'] and len(named) == 1:
            item['url'] = named[0]['url']
            save_plan(plan)
        if item['url']:
            if not any(i['url'] == item['url'] for i in images):
                raise RuntimeError('Recorded media URL is absent: ' + item['key'])
        else:
            if any(i['title'] in ('', 'No title', 'Gallery image') for i in images):
                raise RuntimeError('Untitled images exist. Identify them before uploading to avoid duplicates.')
            plan['pending_upload'] = {'key': item['key'], 'before': [i['url'] for i in images]}
            save_plan(plan)
            browser.activate('button', 'Add image(s)')
            run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                 ROOT / 'build/select-upload-files.ps1', '-WindowTitle', CONFIG['window_title'],
                 '-Files', ROOT / item['source']])
            images = browser.wait(lambda: new_upload(browser, plan), 'Image upload did not finish.', seconds=60)
            reconcile_pending(plan, images)
        current = next(i for i in media_state(browser) if i['url'] == item['url'])
        if current['title'] != item['title']:
            menu(browser, item['url'], 'Edit title')
            browser.action('fill', ref_for(browser.snapshot(), 'textbox', 'Title'), '--value', item['title'])
            browser.activate('button', 'Save')
            browser.wait(lambda: any(i['url'] == item['url'] and i['title'] == item['title']
                                     for i in media_state(browser)), 'Image title was not saved.')
        if current['hidden'] == item['visible']:
            menu(browser, item['url'], 'Hide from gallery' if current['hidden'] is False else 'Show in gallery')
            browser.wait(lambda: any(i['url'] == item['url'] and i['hidden'] != item['visible']
                                     for i in media_state(browser)), 'Image visibility was not saved.')
        print('Media ready:', item['title'], flush=True)
    first = plan['images'][0]
    if not next(i for i in media_state(browser) if i['url'] == first['url'])['thumbnail']:
        menu(browser, first['url'], 'Set as thumbnail')
    # Do not delete old screenshots until the public description uses the new URLs.
    render_sources(plan)


def new_upload(browser, plan):
    images = media_state(browser)
    return images if any(i['url'] not in plan['pending_upload']['before'] for i in images) else None


def render_sources(plan):
    old = {i['key']: i for i in load_plan(plan['previous_version'])['images']}
    path = ROOT / 'docs/nexus-description.bbcode.txt'
    text = path.read_text(encoding='utf-8-sig')
    for item in plan['images']:
        if old[item['key']]['url']:
            text = text.replace(old[item['key']]['url'], item['url'])
    if len(re.findall(r'\[img\]', text, re.I)) > 8:
        raise ValueError('Homepage exceeds eight illustrations.')
    path.write_text(text, encoding='utf-8')
    lines = ['# Nexus media ' + plan['version'], '',
             'English PC is the thumbnail. Gallery order is English PC, Xbox, PlayStation, actions; '
             'Simplified Chinese in the same order; banner last. Body illustrations stay hidden.', '',
             '| Order | Screenshot | Public image | Visibility |', '| --- | --- | --- | --- |']
    for i, item in enumerate(plan['images']):
        order = str(i + 1) if item['visible'] else 'Body'
        lines.append('| {} | [{}](../{}) | [{}]({}) | {} |'.format(
            order, item['key'], item['source'], item['title'], item['url'],
            'Gallery' if item['visible'] else 'Hidden'))
    lines.append('| 9 | Banner | [Banner](' + CONFIG['banner'] + ') | Gallery |')
    (ROOT / 'docs' / ('nexus-media-' + plan['version'] + '.md')).write_text('\n'.join(lines) + '\n', encoding='utf-8')


def update_pages(browser, plan):
    version = plan['version']
    description = (ROOT / 'docs/nexus-description.bbcode.txt').read_text(encoding='utf-8-sig')
    sticky = (ROOT / 'docs/nexus-pinned-post.bbcode.txt').read_text(encoding='utf-8-sig')
    short = (ROOT / 'docs/nexus-short-description.txt').read_text(encoding='utf-8-sig').strip()
    if not (0 < len(short) <= 350) or len(sticky) > 5000 or '[b]V' + version + '[/b]' not in sticky:
        raise ValueError('Short description or sticky version/length is invalid.')
    browser.navigate(CONFIG['editor'] + '/general')
    browser.wait(lambda: 'Mod version' in browser.snapshot(), 'General editor did not load.')
    browser.html(version + '/general-before.html')
    browser.action('fill', '#mod-version', '--value', version)
    browser.action('fill', '#short-description', '--value', short)
    browser.activate('link', 'View source (Ctrl+Shift+S)')
    browser.action('fill', 'textarea[dir="ltr"][placeholder="Describe your mod in detail..."]', '--value', description)
    browser.activate('link', 'View source (Ctrl+Shift+S)')
    # General has two Save buttons; the first is the fixed header control.
    save_buttons = re.findall(r'(@e\d+) button "Save"(?! \[disabled\])', browser.snapshot())
    if save_buttons:
        browser.action('press', 'Enter', '--ref', save_buttons[0])
    browser.wait(lambda: 'button "Save" [disabled]' in browser.snapshot(), 'General save did not complete.')
    browser.navigate(CONFIG['page'] + '?tab=posts')
    browser.wait(lambda: 'Edit post' in browser.snapshot(), 'Posts did not load.')
    browser.html(version + '/posts-before.html')
    existing = browser.evaluate('document.querySelector(' + json.dumps('#edit-comment-post-' + CONFIG['sticky']) + ')?.value')
    if existing and existing.replace('\r\n', '\n').strip() == sticky.replace('\r\n', '\n').strip():
        print('Homepage saved; existing sticky already matches.', flush=True)
        return
    browser.action('click', '#edit-comment-' + CONFIG['sticky'])
    browser.activate('button', '[BBCODE]')
    browser.action('fill', '#edit-comment-post-' + CONFIG['sticky'], '--value', sticky)
    browser.activate('button', '[BBCODE]')
    browser.activate('link', 'Submit')
    browser.wait(lambda: ref_for(browser.snapshot(), 'link', 'Submit', optional=True) is None,
                 'Sticky submission did not finish.')
    print('Homepage and existing sticky saved.', flush=True)


def cleanup_and_order(browser, plan):
    browser.navigate(CONFIG['page'])
    browser.wait(lambda: 'About this mod' in browser.snapshot(), 'Public description did not load.')
    embedded = body_images(browser.html(plan['version'] + '/description-before-cleanup.html'))
    previous = load_plan(plan['previous_version'])['images']
    obsolete = {i['url'] for i in previous if i['url']} - {i['url'] for i in plan['images']}
    if obsolete.intersection(embedded):
        raise RuntimeError('Public description still references old media; save it before deletion.')
    browser.navigate(CONFIG['editor'] + '/media')
    browser.wait(lambda: 'Add image(s)' in browser.snapshot(), 'Media editor did not load.')
    state = browser.sync()
    browser.call('emulate', '--width', '1692', '--height', '1800', '--no-mobile', '--no-touch',
                 '--context', state['context_id'], '--observation', state['observation_id'], json_output=False)
    for item in media_state(browser):
        if item['url'] in obsolete:
            browser.action('click', image_selector(item['url']) + ' button[aria-label="Delete image"]')
            browser.activate('button', 'Delete')
            browser.wait(lambda: all(i['url'] != item['url'] for i in media_state(browser)), 'Image deletion did not complete.')
    desired = [i['url'] for i in plan['images'] if i['visible']] + [CONFIG['banner']]
    current = [i['url'] for i in media_state(browser) if not i['hidden']]
    if set(current) != set(desired):
        raise RuntimeError('Unmapped gallery images exist or the banner is missing. Inspect Media; only previous-version images are deleted automatically.')
    for position, url in enumerate(desired):
        current = [i['url'] for i in media_state(browser) if not i['hidden']]
        if current[position] == url:
            continue
        browser.action('drag', image_selector(url), '--to', image_selector(current[position]),
                       '--hold', '300ms', '--duration', '800ms')
        browser.wait(lambda: [i['url'] for i in media_state(browser) if not i['hidden']][position] == url,
                     'Gallery drag did not produce the requested order.')
    buttons = re.findall(r'(@e\d+) button "Save"(?! \[disabled\])', browser.snapshot())
    if buttons:
        browser.action('press', 'Enter', '--ref', buttons[0])
        browser.wait(lambda: 'button "Save" [disabled]' in browser.snapshot(), 'Media save did not complete.')
    print('Gallery saved: eight application views, banner last.', flush=True)


def github_release(version):
    release = gh('release', 'view', 'v' + version, '--json', 'url,isDraft,isPrerelease,assets')
    expected = {'PBZKeyForge-v' + version + '.zip', 'PBZKeyForge-v' + version + '-presets-only.zip'}
    if release['isDraft'] or release['isPrerelease'] or not expected.issubset({a['name'] for a in release['assets'] if a['size'] > 0}):
        raise RuntimeError('GitHub must expose the final release and both nonempty archives first.')
    return release


def file_versions(browser, version):
    browser.navigate(CONFIG['page'] + '?tab=files')
    browser.wait(lambda: 'Main files' in browser.snapshot(), 'Public Files did not load.')
    return public_files(browser.html(version + '/files.html'))


def publish_files(browser, version):
    github_release(version)
    status_path = ROOT / 'dist/publishing' / version / 'file-uploads.json'
    state = read_json(status_path) if status_path.exists() else {}
    checked_runtime = False
    for category, suffix in [('main', ''), ('optional', '-presets-only')]:
        if any(f['category'] == category and f['version'] == version for f in file_versions(browser, version)):
            print('Already public:', category, version, flush=True)
            state[category] = {'public': True}
            write_json(status_path, state)
            continue
        title = 'Nexus v{} / {}'.format(version, category)
        runs = gh('run', 'list', '--workflow', 'publish-nexusmods.yml', '--limit', '100',
                  '--json', 'databaseId,displayTitle,status,conclusion')
        matching = [r for r in runs if r['displayTitle'] == title]
        if not matching:
            if category in state:
                raise RuntimeError('Upload dispatch has an uncertain result. Inspect Actions and Files; do not dispatch again.')
            if not checked_runtime:
                run(['powershell.exe', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
                     ROOT / 'build/check-runtime-updates.ps1', '-Release'])
                checked_runtime = True
            state[category] = {'dispatch_pending': True}
            write_json(status_path, state)
            # Preserve the argument list: no shell interpolation or multiline quoting.
            run(['gh', 'workflow', 'run', 'publish-nexusmods.yml', '--repo', CONFIG['repository'],
                 '-f', 'tag=v' + version, '-f', 'version=' + version,
                 '-f', 'asset_name=PBZKeyForge-v' + version + suffix + '.zip',
                 '-f', 'file_id=' + CONFIG['file_ids'][category], '-f', 'category=' + category,
                 '-f', 'archive_existing_version=false', '-f', 'allow_mod_manager_download=false'])
            matching = browser.wait(lambda: [r for r in gh('run', 'list', '--workflow', 'publish-nexusmods.yml',
                                     '--limit', '30', '--json', 'databaseId,displayTitle,status,conclusion')
                                     if r['displayTitle'] == title], 'Dispatched upload run is not visible.', seconds=60)
        task = matching[0]
        state[category] = {'run_id': task['databaseId']}
        write_json(status_path, state)
        while task['status'] != 'completed':
            print('Waiting for Nexus upload:', category, task['databaseId'], flush=True)
            time.sleep(10)
            browser.sync()
            task = gh('run', 'view', str(task['databaseId']), '--json', 'databaseId,status,conclusion')
        if task['conclusion'] != 'success':
            raise RuntimeError('Nexus upload run {} ended {}. Inspect its upload step and public Files before any retry.'.format(task['databaseId'], task['conclusion']))
        browser.wait(lambda: any(f['category'] == category and f['version'] == version
                                for f in file_versions(browser, version)), 'Upload succeeded but public Files is not updated.', seconds=60)
    print('Both Nexus files are public.', flush=True)


def verify(browser, plan):
    version = plan['version']
    github_release(version)
    files = file_versions(browser, version)
    for category in ('main', 'optional'):
        if not any(f['version'] == version and f['category'] == category for f in files):
            raise RuntimeError(category + ' file is not public at ' + version)
    browser.navigate(CONFIG['page'] + '?tab=images')
    expected = [i['url'].rsplit('/', 1)[-1] for i in plan['images'] if i['visible']] + [CONFIG['banner'].rsplit('/', 1)[-1]]
    browser.wait(lambda: public_gallery(browser.html(version + '/images.html')) == expected,
                 'Public gallery differs from the 8+1 manifest.')
    browser.navigate(CONFIG['page'])
    browser.wait(lambda: 'About this mod' in browser.snapshot(), 'Public description did not load.')
    html = browser.html(version + '/description.html')
    doc = Document(html)
    thumb = doc.find(lambda n: n.tag == 'meta' and n.attrs.get('property') == 'og:image')
    if len(thumb) != 1 or thumb[0].attrs['content'].rsplit('/', 1)[-1] != expected[0]:
        raise RuntimeError('Public thumbnail differs from English PC.')
    intended = re.findall(r'\[img\](.*?)\[/img\]', (ROOT / 'docs/nexus-description.bbcode.txt').read_text(encoding='utf-8-sig'), re.I)
    if not intended or len(intended) > 8 or body_images(html) != intended:
        raise RuntimeError('Public description illustrations differ from the source.')
    loaded = browser.wait(lambda: loaded_images(browser, intended), 'Embedded illustrations are not loaded.')
    if public_version(html) != version:
        raise RuntimeError('Public mod header version differs from the release.')
    browser.navigate(CONFIG['page'] + '?tab=posts')
    browser.wait(lambda: 'Supported game versions' in browser.snapshot(), 'Public Posts did not load.')
    posts = Document(browser.html(version + '/posts.html'))
    edit = posts.find(lambda n: n.attrs.get('id') == 'edit-comment-' + CONFIG['sticky'])
    if len(edit) != 1:
        raise RuntimeError('Original sticky post is missing.')
    post = edit[0]
    while post.parent and not post.has_class('comment'):
        post = post.parent
    if not post.has_class('comment-sticky'):
        raise RuntimeError('The original announcement is not a sticky post.')
    text = post.text()
    entry = re.search(r'\[b\]V' + re.escape(version) + r'\[/b\].*?\n([^\[]+)',
                      (ROOT / 'docs/nexus-pinned-post.bbcode.txt').read_text(encoding='utf-8-sig'), re.S)
    if not entry or ' '.join(entry[1].split()) not in ' '.join(text.split()):
        raise RuntimeError('Public sticky version, content or sticky status differs from the source.')
    report = {'version': version, 'github': True, 'nexus_files': True, 'gallery_order': expected,
              'thumbnail': expected[0], 'body_images_loaded': len(loaded), 'sticky': CONFIG['sticky']}
    write_json(ROOT / 'dist/publishing' / version / 'verified.json', report)
    print(json.dumps(report, ensure_ascii=False, indent=2), flush=True)


def loaded_images(browser, intended):
    values = browser.evaluate('Array.from(document.images).map(i => ({src:i.src,complete:i.complete,width:i.naturalWidth}))')
    matches = [i for i in values if i['src'] in intended and i['complete'] and i['width'] > 0]
    return matches if len(matches) == len(intended) else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('stage', choices=['prepare', 'files', 'media', 'pages', 'cleanup', 'verify', 'finish'])
    parser.add_argument('--version', required=True, type=version_text)
    parser.add_argument('--previous-version', type=version_text)
    parser.add_argument('--browser', help='Explicit browser-skill instance ID or verified unique label')
    args = parser.parse_args()
    if args.stage == 'prepare':
        if not args.previous_version:
            parser.error('prepare needs --previous-version (the previous public version).')
        prepare(args.version, args.previous_version)
        return
    if not args.browser:
        parser.error('Browser stages require --browser; read browser-skill before starting.')
    plan = load_plan(args.version)
    validate_prepared(plan, args.stage)
    browser = Browser(args.browser)
    try:
        if args.stage in ('files', 'finish'):
            publish_files(browser, args.version)
        if args.stage in ('media', 'finish'):
            upload_and_style(browser, plan)
        if args.stage in ('pages', 'finish'):
            update_pages(browser, plan)
        if args.stage in ('cleanup', 'finish'):
            cleanup_and_order(browser, plan)
        if args.stage in ('verify', 'finish'):
            verify(browser, plan)
    finally:
        browser.close()


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, ValueError, KeyError, OSError) as error:
        print('STOP:', error, file=sys.stderr)
        sys.exit(1)
