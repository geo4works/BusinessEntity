"""Contract and ownership guards for deployment; no Docker or live IdP mutations."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
import uuid
import zipfile
from unittest.mock import patch

import host_install as target
from build_payload import allowed


class DeploymentGuards(unittest.TestCase):
    """Exercise failure boundaries before privileged deployment operations."""
    def setUp(self):
        """Load the non-secret selected server contract."""
        self.request = json.loads(Path(__file__).with_name('install-request.example.json').read_text())

    def test_selected_target(self):
        """Only the chosen HTTPS origin and explicit human login are accepted."""
        target.validate_request(self.request)
        for key, value in [('host', 'other'), ('installationName', '../user-hub'),
                           ('applicationOrigin', 'http://192.168.55.15:7070'),
                           ('applicationOrigin', 'https://192.168.55.15:9440'),
                           ('adminLogin', '__SELECT_EXISTING_USER__')]:
            changed = {**self.request, key: value}
            with self.subTest(key=key, value=value), self.assertRaises(target.InstallError):
                target.validate_request(changed)

    def test_source_excludes_credentials_and_build_artifacts(self):
        """Only source inputs are shipped, including current uncommitted implementation."""
        for name in ['.env', 'Authentic/.env', 'BusinessEntity/.env.local',
                     'BusinessEntity/identity.initial.json', 'BusinessEntity/bin/a.dll',
                     'BusinessEntity/private.key', 'Deployment/identity-secrets.env']:
            self.assertFalse(allowed(name), name)
        self.assertTrue(allowed('BusinessEntity/Infrastructure/Identity/NewCode.cs'))
        self.assertTrue(allowed('BusinessEntity.Resources/DataFill_01.txt'))
        self.assertTrue(allowed('BusinessEntity/MiniApps/BackupRestoreMiniApp/Registration/BackupRestoreRegistration.cs'))
        self.assertTrue(allowed('BusinessEntity/MiniApps/DataProviderMiniApp/Contracts/Properties/Property.cs'))

    def test_archive_traversal_and_symlink_rejected(self):
        """A source bundle cannot overwrite deployment state or files outside its root."""
        with tempfile.TemporaryDirectory() as folder:
            archive = Path(folder) / 'bad.zip'
            for name in ['../installation.json', '/root/file', 'dir\\..\\file']:
                with zipfile.ZipFile(archive, 'w') as output:
                    output.writestr(name, 'bad')
                with self.assertRaises(target.InstallError):
                    target.extract_source(archive, Path(folder) / 'source')
            item = zipfile.ZipInfo('link')
            item.external_attr = 0o120777 << 16
            with zipfile.ZipFile(archive, 'w') as output:
                output.writestr(item, '/etc/passwd')
            with self.assertRaises(target.InstallError):
                target.extract_source(archive, Path(folder) / 'source')

    def test_source_duplicates_rejected(self):
        """Duplicate paths cannot replace a previously validated source entry."""
        import warnings
        with tempfile.TemporaryDirectory() as folder, warnings.catch_warnings():
            warnings.simplefilter('ignore')
            archive = Path(folder) / 'duplicate.zip'
            with zipfile.ZipFile(archive, 'w') as output:
                output.writestr('a', 'first')
                output.writestr('a', 'second')
            with self.assertRaises(target.InstallError):
                target.extract_source(archive, Path(folder) / 'source')

    def test_isolated_compose(self):
        """The plan exposes one HTTPS port and never joins or defines Hub services."""
        state = {'installationId': str(uuid.uuid4()), 'request': self.request,
                 'dbPassword': 'test-db', 'clientSecret': 'test-client'}
        config = target.compose_config(Path('/srv/businessentity'), state, 'app:hash', 'logger:hash')
        self.assertEqual(set(config['services']), {'business-entity', 'web_logger', 'postgres-production-db'})
        self.assertEqual(config['services']['business-entity']['ports'], ['7070:8443'])
        for name, service in config['services'].items():
            self.assertNotIn('container_name', service)
            if name != 'business-entity':
                self.assertNotIn('ports', service)
        self.assertEqual(config['networks'], {'default': {}})
        self.assertFalse(any('TOKEN' in key for key in config['services']['business-entity']['environment']))

    def test_plan_change_prevents_mutation(self):
        """A stale approval fails before directory creation or external registration."""
        report = {'approval': 'current', 'plan': {'root': '/srv/must-not-create'}}
        with tempfile.TemporaryDirectory() as folder:
            lock = Path(folder) / 'lock'
            import builtins
            real_open = builtins.open
            def open_lock(path, *args, **kwargs):
                """Redirect only the process lock to a test-owned location."""
                return real_open(lock if path == '/run/lock/businessentity-install.lock' else path, *args, **kwargs)
            with patch('builtins.open', side_effect=open_lock), patch.object(target, 'digest', return_value='hash'), \
                    patch.object(target, 'inspect', return_value=(None, None, report)), \
                    patch.object(target.Path, 'mkdir') as mkdir:
                with self.assertRaisesRegex(target.InstallError, 'APPROVAL_PLAN_CHANGED'):
                    target.install(self.request, Path('source.zip'), 'stale')
                mkdir.assert_not_called()

    def test_management_errors_do_not_disclose_body(self):
        """Upstream errors containing credentials are replaced by a fixed public code."""
        http = object.__new__(target.Http)
        with patch.object(http, 'request', return_value=(403, None, b'secret-token-in-upstream-body')):
            with self.assertRaisesRegex(target.InstallError, '^HTTPS_JSON_FAILED$'):
                http.json('https://example.invalid')


if __name__ == '__main__':
    unittest.main()
