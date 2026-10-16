"""Execute the production portable UI filter; no engine addresses/hooks invoked."""
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class NativePrivacyTests(unittest.TestCase):
    @unittest.skipUnless(shutil.which('g++'), 'Portable C++ filter runs on Linux; DLL also compiles on Windows CI')
    def test_local_projection_keeps_own_amounts_and_all_simulation_data(self):
        with tempfile.TemporaryDirectory() as folder:
            exe = str(Path(folder) / 'privacy-test')
            subprocess.run(['g++', '-std=c++11', '-Wall', '-Wextra', '-Werror', str(ROOT/'tests/native_privacy_tests.cpp'), '-o', exe], check=True, capture_output=True)
            subprocess.run([exe], check=True, capture_output=True)

    def test_filter_is_after_replication_and_only_targets_gui_destination(self):
        code = (ROOT/'native/mpfever_native.cpp').read_text(encoding='utf-8')
        code = code.split('extern "C" u64 ReplDetour(', 1)[1].split('return r;', 1)[0]
        self.assertLess(code.index('g_replOrig(src, dst, flag)'), code.index('ClearForeignMoneyPositions('))
        self.assertIn('ClearForeignMoneyPositions(*(void**)((u8*)src+0x18),*(void**)((u8*)dst+0x18)', code)
        self.assertNotIn('ClearOthersIn((u8*)src', code)
