"""Package resource catalog and actual Lua bootstrap; engine calls are fixtures."""
import json
from pathlib import Path
import tempfile
import unittest

from lupa.lua52 import LuaError
from test_lockstep import CONTENT, Game


class ResourceCatalogTests(unittest.TestCase):
    def setUp(self):
        self.content = Path(CONTENT)
        self.files = json.loads((self.content.parent / '_content.json').read_text(encoding='utf-8'))['files']

    def test_catalog_contains_every_shipped_content_file(self):
        actual = {p.relative_to(self.content).as_posix() for p in self.content.rglob('*') if p.is_file()}
        self.assertEqual(len(self.files), len(set(self.files)), 'Duplicate resource registration')
        self.assertEqual(set(self.files), actual, 'Missing or unregistered content in game catalog')

    def test_registered_package_bootstraps_bridge_and_emits_hello(self):
        # Game loads real modules through the resource catalog; its api/world are
        # still injected fixtures. This is not an in-game readiness assertion.
        with tempfile.TemporaryDirectory() as tmp:
            game = Game('CatalogHost', 'host', tmp, 50000)
            game.L.execute('FRAME(); UI.POLL(); SIMSTEP()')
            self.assertEqual(len([line for line in game.read('out.log') if line.startswith('hello\tCatalogHost\t')]), 1)
            self.assertTrue(any('bridge v0.17 started' in line for line in game.read('mod.log')))

    def test_unregistered_permission_module_stops_bridge_before_hello(self):
        # Reproduce the shipped incident: file exists on disk, but the game
        # catalog omits it. Both relative and namespaced ug_require must fail.
        missing = [name for name in self.files if name != 'mpfever_permissions.lua']
        self.assertTrue((self.content / 'mpfever_permissions.lua').is_file())
        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaisesRegex(LuaError, 'mpfever_permissions.lua'):
                Game('MissingResource', 'host', tmp, 50000, manifest_files=missing)
            out = Path(tmp, 'MissingResource', 'out.log')
            self.assertTrue(not out.exists() or out.read_bytes() == b'')
            self.assertFalse(Path(tmp, 'MissingResource', 'mod.log').exists())


if __name__ == '__main__':
    unittest.main(verbosity=2)
