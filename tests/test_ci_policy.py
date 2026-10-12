"""Keep the audit's single known failure from becoming a blanket exemption."""
from pathlib import Path
import unittest

KNOWN_EXPECTED_FAILURE = 'test_companies_offline.CompaniesOffline.test_signature_should_detect_account_binding_change'


def expected_failure_ids(suite):
    found = set()
    for item in suite:
        if isinstance(item, unittest.TestSuite):
            found.update(expected_failure_ids(item))
        else:
            method = getattr(item, item._testMethodName)
            if getattr(method, '__unittest_expecting_failure__', False):
                found.add(item.id())
    return found


class CiPolicyTests(unittest.TestCase):
    def test_only_the_audited_identity_failure_is_exempt(self):
        suite = unittest.defaultTestLoader.discover(str(Path(__file__).resolve().parent), pattern='test_*.py')
        self.assertEqual(expected_failure_ids(suite), {KNOWN_EXPECTED_FAILURE})

    def test_unknown_expected_failure_is_identified(self):
        class Example(unittest.TestCase):
            @unittest.expectedFailure
            def test_unknown(self):
                self.fail('new error')
        suite = unittest.defaultTestLoader.loadTestsFromTestCase(Example)
        self.assertNotEqual(expected_failure_ids(suite), {KNOWN_EXPECTED_FAILURE})
        self.assertEqual(len(expected_failure_ids(suite)), 1)


if __name__ == '__main__':
    unittest.main(verbosity=2)
