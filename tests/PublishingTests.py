"""Publishing decisions use website evidence; uncertain writes are never replayed."""
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'build'))
from publishing_common import body_images, public_files, public_gallery, public_version, ref_for


def module(name, filename):
    spec = importlib.util.spec_from_file_location(name, Path(__file__).resolve().parents[1] / 'build' / filename)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


publish = module('publish_nexus', 'publish-nexus.py')
privacy = module('release_privacy', 'check-release-privacy.py')


class PublishingTests(unittest.TestCase):
    def test_files_distinguish_current_categories_from_archived_versions(self):
        html = '<h2>Main files</h2><dl><dt data-id="1" data-version="1.3.2"></dt></dl>' \
               '<h2>Optional files</h2><dl><dt data-id="2" data-version="1.3.1"></dt></dl>' \
               '<h2>Old files</h2><dl><dt data-id="3" data-version="1.3.2"></dt></dl>'
        files = public_files(html)
        self.assertEqual([(f['category'], f['version']) for f in files],
                         [('main', '1.3.2'), ('optional', '1.3.1'), (None, '1.3.2')])

    def test_gallery_ignores_header_carousel_and_preserves_order(self):
        self.assertEqual(public_gallery('<img src="header.png"><img id="mod-image-1" src="x/b.png">'
                                        '<img id="mod-image-2" src="x/a.png">'), ['b.png', 'a.png'])

    def test_header_version_is_not_inferred_from_new_changelog_text(self):
        html = '<li class="stat-version"><div class="titlestat">Version</div>' \
               '<div class="stat">1.3.1</div></li><div>Version 1.3.2 improves performance</div>'
        self.assertEqual(public_version(html), '1.3.1')

    def test_body_does_not_count_gallery_or_script_images(self):
        html = '<img src="gallery.png"><script>"<img src=old.png>"</script>' \
               '<div class="container mod_description_container"><p><img src="body.png"></p></div>'
        self.assertEqual(body_images(html), ['body.png'])
        with self.assertRaisesRegex(RuntimeError, 'container'):
            body_images('<div>Loading</div>')

    def test_refs_keep_menu_and_modal_save_distinct(self):
        self.assertEqual(ref_for('@e1 menuitem "Edit title"\n@e2 button "Save"\n', 'button', 'Save'), '@e2')
        with self.assertRaisesRegex(RuntimeError, 'found 2'):
            ref_for('@e1 button "Save"\n@e2 button "Save"\n', 'button', 'Save')

    def test_pending_upload_records_one_new_url_without_uploading_again(self):
        plan = {'images': [{'key': 'pc', 'url': None}], 'pending_upload': {'key': 'pc', 'before': ['old']}}
        with patch.object(publish, 'save_plan') as save:
            publish.reconcile_pending(plan, [{'url': 'old'}, {'url': 'new'}])
        self.assertEqual(plan['images'][0]['url'], 'new')
        self.assertNotIn('pending_upload', plan)
        save.assert_called_once_with(plan)

    def test_pending_upload_without_unique_result_preserves_checkpoint(self):
        for images in ([{'url': 'old'}], [{'url': 'new1'}, {'url': 'new2'}]):
            plan = {'images': [{'key': 'pc', 'url': None}], 'pending_upload': {'key': 'pc', 'before': ['old']}}
            with self.assertRaisesRegex(RuntimeError, 'pending'), patch.object(publish, 'save_plan') as save:
                publish.reconcile_pending(plan, images)
            save.assert_not_called()
            self.assertIn('pending_upload', plan)

    def test_prepare_resume_does_not_erase_uploaded_urls(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'docs').mkdir()
            (root / 'new.png').write_bytes(b'fixture')
            old = {'version': '1.3.1', 'images': [{'key': 'pc', 'source': 'old.png', 'title': '1.3.1', 'url': 'old', 'visible': True}]}
            plan = {'version': '1.3.2', 'previous_version': '1.3.1', 'images': [
                {'key': 'pc', 'source': 'new.png', 'title': '1.3.2', 'url': 'already-uploaded', 'visible': True}]}
            for v, value in [('1.3.1', old), ('1.3.2', plan)]:
                (root / 'docs' / ('nexus-media-' + v + '.json')).write_text(json.dumps(value))
            with patch.object(publish, 'ROOT', root):
                publish.prepare('1.3.2', '1.3.1')
            self.assertEqual(json.loads((root / 'docs/nexus-media-1.3.2.json').read_text()), plan)

    def test_already_public_files_never_dispatch_action(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(publish, 'ROOT', Path(directory)), \
                patch.object(publish, 'github_release'), patch.object(publish, 'run') as run, \
                patch.object(publish, 'file_versions', return_value=[
                    {'category': 'main', 'version': '1.3.2'}, {'category': 'optional', 'version': '1.3.2'}]):
            publish.publish_files(object(), '1.3.2')
            run.assert_not_called()

    def test_unknown_dispatch_never_dispatches_again(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            checkpoint = root / 'dist/publishing/1.3.2/file-uploads.json'
            checkpoint.parent.mkdir(parents=True)
            checkpoint.write_text('{"main":{"dispatch_pending":true}}')
            with patch.object(publish, 'ROOT', root), patch.object(publish, 'github_release'), \
                    patch.object(publish, 'file_versions', return_value=[]), \
                    patch.object(publish, 'gh', return_value=[]), patch.object(publish, 'run') as run:
                with self.assertRaisesRegex(RuntimeError, 'uncertain'):
                    publish.publish_files(object(), '1.3.2')
                run.assert_not_called()

    def test_all_recorded_media_is_reused_without_opening_picker(self):
        class FakeBrowser:
            def navigate(self, url): pass
            def wait(self, predicate, message, **kwargs):
                result = predicate()
                if not result: raise RuntimeError(message)
                return result
            def snapshot(self): return '@e1 button "Add image(s)"'
            def sync(self): return {'context_id': 'context', 'observation_id': 'observation'}
            def call(self, *args, **kwargs): pass
        plan = {'version': '1.3.2', 'previous_version': '1.3.1', 'images': [
            {'key': 'pc', 'url': 'current-pc', 'visible': True, 'title': 'Current PC'},
            {'key': 'dialog', 'url': 'current-dialog', 'visible': False, 'title': 'Current dialog'}]}
        live = [{'url': 'current-pc', 'hidden': False, 'thumbnail': True, 'title': 'Current PC'},
                {'url': 'current-dialog', 'hidden': True, 'thumbnail': False, 'title': 'Current dialog'}]
        with patch.object(publish, 'media_state', return_value=live), patch.object(publish, 'run') as run, \
                patch.object(publish, 'menu') as menu, patch.object(publish, 'render_sources') as render:
            publish.upload_and_style(FakeBrowser(), plan)
            run.assert_not_called()
            menu.assert_not_called()
            render.assert_called_once_with(plan)

    def test_cleanup_cannot_delete_images_still_used_by_public_body(self):
        class FakeBrowser:
            def navigate(self, url): pass
            def snapshot(self): return 'About this mod'
            def wait(self, predicate, message, **kwargs): return predicate()
            def html(self, name): return '<div class="mod_description_container"><img src="old-dialog"></div>'
            def action(self, *args): raise AssertionError('No mutation is allowed before body verification.')
        with patch.object(publish, 'load_plan', return_value={'images': [{'url': 'old-dialog'}]}):
            with self.assertRaisesRegex(RuntimeError, 'still references old'):
                publish.cleanup_and_order(FakeBrowser(), {'version': '1.3.2', 'previous_version': '1.3.1',
                                                         'images': [{'url': 'new-dialog'}]})

    def test_invalid_copy_is_detected_before_browser_or_upload(self):
        plan = publish.load_plan('1.3.1')
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / 'docs').mkdir()
            (root / 'docs/nexus-pinned-post.bbcode.txt').write_text('[b]V1.3.0[/b] Old notes')
            (root / 'docs/nexus-short-description.txt').write_text('Summary')
            (root / 'docs/nexus-description.bbcode.txt').write_text('Description')
            with patch.object(publish, 'ROOT', root), self.assertRaisesRegex(ValueError, 'sticky version'):
                publish.validate_prepared(plan, 'pages')

    def test_failed_run_stops_instead_of_reuploading(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(publish, 'ROOT', Path(directory)), \
                patch.object(publish, 'github_release'), patch.object(publish, 'file_versions', return_value=[]), \
                patch.object(publish, 'gh', return_value=[{'displayTitle': 'Nexus v1.3.2 / main',
                             'databaseId': 123, 'status': 'completed', 'conclusion': 'failure'}]), \
                patch.object(publish, 'run') as run:
            with self.assertRaisesRegex(RuntimeError, 'ended failure'):
                publish.publish_files(object(), '1.3.2')
            run.assert_not_called()

    def test_privacy_detects_nested_archive_and_redacts_values(self):
        # Construct fixture secrets at runtime; no valid credential is stored in the repository.
        secret = ('gh' + 'p_' + 'x' * 30).encode()
        inner = io.BytesIO()
        with zipfile.ZipFile(inner, 'w') as archive:
            archive.writestr('config.txt', secret)
        outer = io.BytesIO()
        with zipfile.ZipFile(outer, 'w') as archive:
            archive.writestr('runtime.zip', inner.getvalue())
            archive.writestr('preview.png', b'image fixture')
        findings, images = [], []
        privacy.inspect_bytes('release.zip', outer.getvalue(), findings, images)
        self.assertTrue(any(f['object'] == 'release.zip!runtime.zip!config.txt' for f in findings))
        self.assertNotIn(secret.decode(), json.dumps(findings))
        self.assertEqual(images, ['release.zip!preview.png'])

    def test_privacy_permits_public_urls_and_finds_utf16_private_path(self):
        findings, images = [], []
        privacy.inspect_bytes('notes.txt', b'https://github.com/muyuanjin/PBZKeyForge', findings, images)
        self.assertEqual(findings, [])
        privacy.inspect_bytes('app.exe', ('C:' + '\\Users\\Example\\private.sav').encode('utf-16-le'), findings, images)
        self.assertEqual(findings[0]['kind'], 'private_path_or_steam_id')

    def test_unix_directory_labels_are_distinct_from_personal_paths(self):
        findings, images = [], []
        privacy.inspect_bytes('guide.txt', b'Unix roots /home/ and /Users/', findings, images)
        self.assertEqual(findings, [])
        privacy.inspect_bytes('guide.txt', b'Local /home/example/private.txt', findings, images)
        self.assertEqual(findings[0]['kind'], 'private_path_or_steam_id')

    def test_privacy_checks_intermediate_outgoing_commits(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            def git(*args):
                return subprocess.check_output(['git', '-c', 'user.name=Fixture', '-c',
                    'user.email=fixture@example.invalid', *args], cwd=root, stderr=subprocess.DEVNULL).decode().strip()
            git('init')
            file = root / 'config.txt'
            file.write_text('public')
            git('add', 'config.txt'); git('commit', '-m', 'initial')
            base = git('rev-parse', 'HEAD')
            file.write_text('gh' + 'p_' + 'x' * 30)
            git('commit', '-am', 'intermediate')
            file.write_text('public')
            git('commit', '-am', 'final')
            with patch.object(privacy, 'ROOT', root):
                report = privacy.inspect_outgoing(base, [])
            self.assertEqual(report['file_versions_inspected'], 2)
            self.assertTrue(any(f['kind'] == 'credential' for f in report['findings']))


if __name__ == '__main__':
    unittest.main()
