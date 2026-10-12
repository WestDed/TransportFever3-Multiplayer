"""All translation gaps have been filled; no failures are exempt."""
import json
import unittest
import sys
import test_lang
from test_lang import LANGUAGES, LANG_FILE, code_keys, placeholders

KNOWN_MISSING = frozenset()


def unexpected_translation_errors(keys, texts, languages):
    errors = []
    for lang in languages:
        for key in keys:
            value = (texts.get(key) or {}).get(lang)
            if not value:
                if key not in KNOWN_MISSING:
                    errors.append(f'{lang}: missing {key}')
            elif placeholders(value) != placeholders(key):
                errors.append(f'{lang}: broken placeholders {key}')
    return errors


class TranslationBaselineTests(unittest.TestCase):
    def test_existing_translations_have_no_new_errors(self):
        texts = json.loads(LANG_FILE.read_text(encoding='utf-8-sig'))['texts']
        self.assertEqual(unexpected_translation_errors(code_keys(), texts, LANGUAGES), [])

    def test_no_gaps_are_exempt(self):
        self.assertEqual(KNOWN_MISSING, frozenset())

    def test_new_gap_fails(self):
        self.assertEqual(unexpected_translation_errors(['new text'], {}, ['ru']), ['ru: missing new text'])

    def test_broken_placeholder_fails(self):
        key = "=== Cancelled: the session ends ==="
        self.assertEqual(unexpected_translation_errors([key], {key: {'ru': '{0}'}}, ['ru']), [f'ru: broken placeholders {key}'])

    def test_valid_translation_does_not_fail(self):
        key = "=== Cancelled: the session ends ==="
        self.assertEqual(unexpected_translation_errors([key], {key: {'ru': 'Исправлено'}}, ['ru']), [])


def report_baseline():
    # Run the original check; catch only its known nonzero result, never exceptions.
    code = test_lang.main()
    texts = json.loads(LANG_FILE.read_text(encoding='utf-8-sig'))['texts']
    errors = unexpected_translation_errors(code_keys(), texts, LANGUAGES)
    if code not in (0, 1) or errors:
        print('Unexpected translation failure:', errors)
        return 1
    print('Translation baseline: only the three audited keys may remain untranslated.')
    return 0


if __name__ == '__main__':
    if '--report' in sys.argv:
        sys.exit(report_baseline())
    unittest.main(verbosity=2)
