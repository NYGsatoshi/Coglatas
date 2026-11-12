#!/usr/bin/env python3
"""Check committed Markdown navigation and catalogue retention, without executing it.

The baseline is an explicit caller input. Catalogue provenance never selects the
protection baseline. Working-tree files, external URL reachability, product
claims, and owning generator/evidence approval procedures are outside this check.
"""

from __future__ import annotations

import argparse
from dataclasses import dataclass
import hashlib
from html.parser import HTMLParser
from importlib.metadata import version
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import unicodedata
from urllib.parse import unquote, urlsplit

from markdown_it import MarkdownIt
import yaml


VERSION = "1.0.0"
REGULAR_MODES = {"100644", "100755"}
MAX_TEXT_BYTES = 8 * 1024 * 1024
MAX_CATALOGUE_BYTES = 4 * 1024 * 1024
KINDS = {
    "entrance", "guide", "reference", "contract", "decision", "evidence",
    "report", "generated-view",
}
LIFECYCLES = {"active", "candidate", "historical", "superseded", "needs-review"}
INTEGRITY_FLAGS = {"generated", "fixed-path-consumer", "immutable-evidence"}
REQUIRED_RECORD_FIELDS = {"path", "kind", "domain", "lifecycle", "classification_basis"}
OPTIONAL_RECORD_STRINGS = {"canonical_for", "source_ref", "superseded_by"}
RECORD_FIELDS = REQUIRED_RECORD_FIELDS | OPTIONAL_RECORD_STRINGS | {"integrity", "related_assets"}
SCOPE_FIELDS = {
    "repository", "base_commit", "base_tree", "file_suffix", "tracked_markdown_at_base",
    "markdown_added_by_this_change", "expected_markdown_count", "excluded_formats",
}
CAMPAIGNS = "performance/baseline-campaigns/evidence/"
SLUGGER_SOURCE = "https://github.com/Flet/github-slugger/tree/3461c4350868329c8530904d170358bca1d31448"

# The generated character filter below and the collision rule derive from
# github-slugger at SLUGGER_SOURCE (Unicode 13). Apply the original filter to
# UTF-16 code units, as JavaScript does, rather than treating surrogate ranges
# as Python Unicode scalar ranges. Do not replace it with Unicode categories:
# circled alphabetic symbols, connector punctuation and superscript numbers
# have different behavior.
#
# Copyright (c) 2015, Dan Flettre <fletd01@yahoo.com>
#
# Permission to use, copy, modify, and/or distribute this software for any
# purpose with or without fee is hereby granted, provided that the above
# copyright notice and this permission notice appear in all copies.
#
# THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
# WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
# MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
# ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
# WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN ACTION
# OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF OR IN
# CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
GITHUB_SLUG_FILTER = re.compile(
    "[\\0-\\x1F!-,\\.\\/:-@\\[-\\^`\\{-\\xA9\\xAB-\\xB4\\xB6-\\xB9\\xBB-\\xBF\\xD7\\xF7\\u02C2-\\u02C5\\u02D2-\\u02DF\\u02E5-\\u02EB\\u02ED\\u02EF-\\u"
    "02FF\\u0375\\u0378\\u0379\\u037E\\u0380-\\u0385\\u0387\\u038B\\u038D\\u03A2\\u03F6\\u0482\\u0530\\u0557\\u0558\\u055A-\\u055F\\u0589-\\u059"
    "0\\u05BE\\u05C0\\u05C3\\u05C6\\u05C8-\\u05CF\\u05EB-\\u05EE\\u05F3-\\u060F\\u061B-\\u061F\\u066A-\\u066D\\u06D4\\u06DD\\u06DE\\u06E9\\u06FD"
    "\\u06FE\\u0700-\\u070F\\u074B\\u074C\\u07B2-\\u07BF\\u07F6-\\u07F9\\u07FB\\u07FC\\u07FE\\u07FF\\u082E-\\u083F\\u085C-\\u085F\\u086B-\\u089F"
    "\\u08B5\\u08C8-\\u08D2\\u08E2\\u0964\\u0965\\u0970\\u0984\\u098D\\u098E\\u0991\\u0992\\u09A9\\u09B1\\u09B3-\\u09B5\\u09BA\\u09BB\\u09C5\\u09"
    "C6\\u09C9\\u09CA\\u09CF-\\u09D6\\u09D8-\\u09DB\\u09DE\\u09E4\\u09E5\\u09F2-\\u09FB\\u09FD\\u09FF\\u0A00\\u0A04\\u0A0B-\\u0A0E\\u0A11\\u0A12"
    "\\u0A29\\u0A31\\u0A34\\u0A37\\u0A3A\\u0A3B\\u0A3D\\u0A43-\\u0A46\\u0A49\\u0A4A\\u0A4E-\\u0A50\\u0A52-\\u0A58\\u0A5D\\u0A5F-\\u0A65\\u0A76-\\"
    "u0A80\\u0A84\\u0A8E\\u0A92\\u0AA9\\u0AB1\\u0AB4\\u0ABA\\u0ABB\\u0AC6\\u0ACA\\u0ACE\\u0ACF\\u0AD1-\\u0ADF\\u0AE4\\u0AE5\\u0AF0-\\u0AF8\\u0B0"
    "0\\u0B04\\u0B0D\\u0B0E\\u0B11\\u0B12\\u0B29\\u0B31\\u0B34\\u0B3A\\u0B3B\\u0B45\\u0B46\\u0B49\\u0B4A\\u0B4E-\\u0B54\\u0B58-\\u0B5B\\u0B5E\\u0"
    "B64\\u0B65\\u0B70\\u0B72-\\u0B81\\u0B84\\u0B8B-\\u0B8D\\u0B91\\u0B96-\\u0B98\\u0B9B\\u0B9D\\u0BA0-\\u0BA2\\u0BA5-\\u0BA7\\u0BAB-\\u0BAD\\u0"
    "BBA-\\u0BBD\\u0BC3-\\u0BC5\\u0BC9\\u0BCE\\u0BCF\\u0BD1-\\u0BD6\\u0BD8-\\u0BE5\\u0BF0-\\u0BFF\\u0C0D\\u0C11\\u0C29\\u0C3A-\\u0C3C\\u0C45\\u0"
    "C49\\u0C4E-\\u0C54\\u0C57\\u0C5B-\\u0C5F\\u0C64\\u0C65\\u0C70-\\u0C7F\\u0C84\\u0C8D\\u0C91\\u0CA9\\u0CB4\\u0CBA\\u0CBB\\u0CC5\\u0CC9\\u0CCE"
    "-\\u0CD4\\u0CD7-\\u0CDD\\u0CDF\\u0CE4\\u0CE5\\u0CF0\\u0CF3-\\u0CFF\\u0D0D\\u0D11\\u0D45\\u0D49\\u0D4F-\\u0D53\\u0D58-\\u0D5E\\u0D64\\u0D65\\"
    "u0D70-\\u0D79\\u0D80\\u0D84\\u0D97-\\u0D99\\u0DB2\\u0DBC\\u0DBE\\u0DBF\\u0DC7-\\u0DC9\\u0DCB-\\u0DCE\\u0DD5\\u0DD7\\u0DE0-\\u0DE5\\u0DF0\\u"
    "0DF1\\u0DF4-\\u0E00\\u0E3B-\\u0E3F\\u0E4F\\u0E5A-\\u0E80\\u0E83\\u0E85\\u0E8B\\u0EA4\\u0EA6\\u0EBE\\u0EBF\\u0EC5\\u0EC7\\u0ECE\\u0ECF\\u0ED"
    "A\\u0EDB\\u0EE0-\\u0EFF\\u0F01-\\u0F17\\u0F1A-\\u0F1F\\u0F2A-\\u0F34\\u0F36\\u0F38\\u0F3A-\\u0F3D\\u0F48\\u0F6D-\\u0F70\\u0F85\\u0F98\\u0FB"
    "D-\\u0FC5\\u0FC7-\\u0FFF\\u104A-\\u104F\\u109E\\u109F\\u10C6\\u10C8-\\u10CC\\u10CE\\u10CF\\u10FB\\u1249\\u124E\\u124F\\u1257\\u1259\\u125E\\"
    "u125F\\u1289\\u128E\\u128F\\u12B1\\u12B6\\u12B7\\u12BF\\u12C1\\u12C6\\u12C7\\u12D7\\u1311\\u1316\\u1317\\u135B\\u135C\\u1360-\\u137F\\u1390"
    "-\\u139F\\u13F6\\u13F7\\u13FE-\\u1400\\u166D\\u166E\\u1680\\u169B-\\u169F\\u16EB-\\u16ED\\u16F9-\\u16FF\\u170D\\u1715-\\u171F\\u1735-\\u173"
    "F\\u1754-\\u175F\\u176D\\u1771\\u1774-\\u177F\\u17D4-\\u17D6\\u17D8-\\u17DB\\u17DE\\u17DF\\u17EA-\\u180A\\u180E\\u180F\\u181A-\\u181F\\u187"
    "9-\\u187F\\u18AB-\\u18AF\\u18F6-\\u18FF\\u191F\\u192C-\\u192F\\u193C-\\u1945\\u196E\\u196F\\u1975-\\u197F\\u19AC-\\u19AF\\u19CA-\\u19CF\\u1"
    "9DA-\\u19FF\\u1A1C-\\u1A1F\\u1A5F\\u1A7D\\u1A7E\\u1A8A-\\u1A8F\\u1A9A-\\u1AA6\\u1AA8-\\u1AAF\\u1AC1-\\u1AFF\\u1B4C-\\u1B4F\\u1B5A-\\u1B6A\\"
    "u1B74-\\u1B7F\\u1BF4-\\u1BFF\\u1C38-\\u1C3F\\u1C4A-\\u1C4C\\u1C7E\\u1C7F\\u1C89-\\u1C8F\\u1CBB\\u1CBC\\u1CC0-\\u1CCF\\u1CD3\\u1CFB-\\u1CFF"
    "\\u1DFA\\u1F16\\u1F17\\u1F1E\\u1F1F\\u1F46\\u1F47\\u1F4E\\u1F4F\\u1F58\\u1F5A\\u1F5C\\u1F5E\\u1F7E\\u1F7F\\u1FB5\\u1FBD\\u1FBF-\\u1FC1\\u1FC"
    "5\\u1FCD-\\u1FCF\\u1FD4\\u1FD5\\u1FDC-\\u1FDF\\u1FED-\\u1FF1\\u1FF5\\u1FFD-\\u203E\\u2041-\\u2053\\u2055-\\u2070\\u2072-\\u207E\\u2080-\\u2"
    "08F\\u209D-\\u20CF\\u20F1-\\u2101\\u2103-\\u2106\\u2108\\u2109\\u2114\\u2116-\\u2118\\u211E-\\u2123\\u2125\\u2127\\u2129\\u212E\\u213A\\u21"
    "3B\\u2140-\\u2144\\u214A-\\u214D\\u214F-\\u215F\\u2189-\\u24B5\\u24EA-\\u2BFF\\u2C2F\\u2C5F\\u2CE5-\\u2CEA\\u2CF4-\\u2CFF\\u2D26\\u2D28-\\u"
    "2D2C\\u2D2E\\u2D2F\\u2D68-\\u2D6E\\u2D70-\\u2D7E\\u2D97-\\u2D9F\\u2DA7\\u2DAF\\u2DB7\\u2DBF\\u2DC7\\u2DCF\\u2DD7\\u2DDF\\u2E00-\\u2E2E\\u2E"
    "30-\\u3004\\u3008-\\u3020\\u3030\\u3036\\u3037\\u303D-\\u3040\\u3097\\u3098\\u309B\\u309C\\u30A0\\u30FB\\u3100-\\u3104\\u3130\\u318F-\\u319"
    "F\\u31C0-\\u31EF\\u3200-\\u33FF\\u4DC0-\\u4DFF\\u9FFD-\\u9FFF\\uA48D-\\uA4CF\\uA4FE\\uA4FF\\uA60D-\\uA60F\\uA62C-\\uA63F\\uA673\\uA67E\\uA6"
    "F2-\\uA716\\uA720\\uA721\\uA789\\uA78A\\uA7C0\\uA7C1\\uA7CB-\\uA7F4\\uA828-\\uA82B\\uA82D-\\uA83F\\uA874-\\uA87F\\uA8C6-\\uA8CF\\uA8DA-\\uA"
    "8DF\\uA8F8-\\uA8FA\\uA8FC\\uA92E\\uA92F\\uA954-\\uA95F\\uA97D-\\uA97F\\uA9C1-\\uA9CE\\uA9DA-\\uA9DF\\uA9FF\\uAA37-\\uAA3F\\uAA4E\\uAA4F\\uA"
    "A5A-\\uAA5F\\uAA77-\\uAA79\\uAAC3-\\uAADA\\uAADE\\uAADF\\uAAF0\\uAAF1\\uAAF7-\\uAB00\\uAB07\\uAB08\\uAB0F\\uAB10\\uAB17-\\uAB1F\\uAB27\\uAB"
    "2F\\uAB5B\\uAB6A-\\uAB6F\\uABEB\\uABEE\\uABEF\\uABFA-\\uABFF\\uD7A4-\\uD7AF\\uD7C7-\\uD7CA\\uD7FC-\\uD7FF\\uE000-\\uF8FF\\uFA6E\\uFA6F\\uFA"
    "DA-\\uFAFF\\uFB07-\\uFB12\\uFB18-\\uFB1C\\uFB29\\uFB37\\uFB3D\\uFB3F\\uFB42\\uFB45\\uFBB2-\\uFBD2\\uFD3E-\\uFD4F\\uFD90\\uFD91\\uFDC8-\\uFD"
    "EF\\uFDFC-\\uFDFF\\uFE10-\\uFE1F\\uFE30-\\uFE32\\uFE35-\\uFE4C\\uFE50-\\uFE6F\\uFE75\\uFEFD-\\uFF0F\\uFF1A-\\uFF20\\uFF3B-\\uFF3E\\uFF40\\u"
    "FF5B-\\uFF65\\uFFBF-\\uFFC1\\uFFC8\\uFFC9\\uFFD0\\uFFD1\\uFFD8\\uFFD9\\uFFDD-\\uFFFF]|\\uD800[\\uDC0C\\uDC27\\uDC3B\\uDC3E\\uDC4E\\uDC4F\\u"
    "DC5E-\\uDC7F\\uDCFB-\\uDD3F\\uDD75-\\uDDFC\\uDDFE-\\uDE7F\\uDE9D-\\uDE9F\\uDED1-\\uDEDF\\uDEE1-\\uDEFF\\uDF20-\\uDF2C\\uDF4B-\\uDF4F\\uDF7"
    "B-\\uDF7F\\uDF9E\\uDF9F\\uDFC4-\\uDFC7\\uDFD0\\uDFD6-\\uDFFF]|\\uD801[\\uDC9E\\uDC9F\\uDCAA-\\uDCAF\\uDCD4-\\uDCD7\\uDCFC-\\uDCFF\\uDD28-\\"
    "uDD2F\\uDD64-\\uDDFF\\uDF37-\\uDF3F\\uDF56-\\uDF5F\\uDF68-\\uDFFF]|\\uD802[\\uDC06\\uDC07\\uDC09\\uDC36\\uDC39-\\uDC3B\\uDC3D\\uDC3E\\uDC5"
    "6-\\uDC5F\\uDC77-\\uDC7F\\uDC9F-\\uDCDF\\uDCF3\\uDCF6-\\uDCFF\\uDD16-\\uDD1F\\uDD3A-\\uDD7F\\uDDB8-\\uDDBD\\uDDC0-\\uDDFF\\uDE04\\uDE07-\\u"
    "DE0B\\uDE14\\uDE18\\uDE36\\uDE37\\uDE3B-\\uDE3E\\uDE40-\\uDE5F\\uDE7D-\\uDE7F\\uDE9D-\\uDEBF\\uDEC8\\uDEE7-\\uDEFF\\uDF36-\\uDF3F\\uDF56-\\"
    "uDF5F\\uDF73-\\uDF7F\\uDF92-\\uDFFF]|\\uD803[\\uDC49-\\uDC7F\\uDCB3-\\uDCBF\\uDCF3-\\uDCFF\\uDD28-\\uDD2F\\uDD3A-\\uDE7F\\uDEAA\\uDEAD-\\u"
    "DEAF\\uDEB2-\\uDEFF\\uDF1D-\\uDF26\\uDF28-\\uDF2F\\uDF51-\\uDFAF\\uDFC5-\\uDFDF\\uDFF7-\\uDFFF]|\\uD804[\\uDC47-\\uDC65\\uDC70-\\uDC7E\\uD"
    "CBB-\\uDCCF\\uDCE9-\\uDCEF\\uDCFA-\\uDCFF\\uDD35\\uDD40-\\uDD43\\uDD48-\\uDD4F\\uDD74\\uDD75\\uDD77-\\uDD7F\\uDDC5-\\uDDC8\\uDDCD\\uDDDB\\u"
    "DDDD-\\uDDFF\\uDE12\\uDE38-\\uDE3D\\uDE3F-\\uDE7F\\uDE87\\uDE89\\uDE8E\\uDE9E\\uDEA9-\\uDEAF\\uDEEB-\\uDEEF\\uDEFA-\\uDEFF\\uDF04\\uDF0D\\u"
    "DF0E\\uDF11\\uDF12\\uDF29\\uDF31\\uDF34\\uDF3A\\uDF45\\uDF46\\uDF49\\uDF4A\\uDF4E\\uDF4F\\uDF51-\\uDF56\\uDF58-\\uDF5C\\uDF64\\uDF65\\uDF6D"
    "-\\uDF6F\\uDF75-\\uDFFF]|\\uD805[\\uDC4B-\\uDC4F\\uDC5A-\\uDC5D\\uDC62-\\uDC7F\\uDCC6\\uDCC8-\\uDCCF\\uDCDA-\\uDD7F\\uDDB6\\uDDB7\\uDDC1-\\"
    "uDDD7\\uDDDE-\\uDDFF\\uDE41-\\uDE43\\uDE45-\\uDE4F\\uDE5A-\\uDE7F\\uDEB9-\\uDEBF\\uDECA-\\uDEFF\\uDF1B\\uDF1C\\uDF2C-\\uDF2F\\uDF3A-\\uDFF"
    "F]|\\uD806[\\uDC3B-\\uDC9F\\uDCEA-\\uDCFE\\uDD07\\uDD08\\uDD0A\\uDD0B\\uDD14\\uDD17\\uDD36\\uDD39\\uDD3A\\uDD44-\\uDD4F\\uDD5A-\\uDD9F\\uDD"
    "A8\\uDDA9\\uDDD8\\uDDD9\\uDDE2\\uDDE5-\\uDDFF\\uDE3F-\\uDE46\\uDE48-\\uDE4F\\uDE9A-\\uDE9C\\uDE9E-\\uDEBF\\uDEF9-\\uDFFF]|\\uD807[\\uDC09\\"
    "uDC37\\uDC41-\\uDC4F\\uDC5A-\\uDC71\\uDC90\\uDC91\\uDCA8\\uDCB7-\\uDCFF\\uDD07\\uDD0A\\uDD37-\\uDD39\\uDD3B\\uDD3E\\uDD48-\\uDD4F\\uDD5A-\\"
    "uDD5F\\uDD66\\uDD69\\uDD8F\\uDD92\\uDD99-\\uDD9F\\uDDAA-\\uDEDF\\uDEF7-\\uDFAF\\uDFB1-\\uDFFF]|\\uD808[\\uDF9A-\\uDFFF]|\\uD809[\\uDC6F-\\"
    "uDC7F\\uDD44-\\uDFFF]|[\\uD80A\\uD80B\\uD80E-\\uD810\\uD812-\\uD819\\uD824-\\uD82B\\uD82D\\uD82E\\uD830-\\uD833\\uD837\\uD839\\uD83D\\uD83"
    "F\\uD87B-\\uD87D\\uD87F\\uD885-\\uDB3F\\uDB41-\\uDBFF][\\uDC00-\\uDFFF]|\\uD80D[\\uDC2F-\\uDFFF]|\\uD811[\\uDE47-\\uDFFF]|\\uD81A[\\uDE39"
    "-\\uDE3F\\uDE5F\\uDE6A-\\uDECF\\uDEEE\\uDEEF\\uDEF5-\\uDEFF\\uDF37-\\uDF3F\\uDF44-\\uDF4F\\uDF5A-\\uDF62\\uDF78-\\uDF7C\\uDF90-\\uDFFF]|\\u"
    "D81B[\\uDC00-\\uDE3F\\uDE80-\\uDEFF\\uDF4B-\\uDF4E\\uDF88-\\uDF8E\\uDFA0-\\uDFDF\\uDFE2\\uDFE5-\\uDFEF\\uDFF2-\\uDFFF]|\\uD821[\\uDFF8-\\u"
    "DFFF]|\\uD823[\\uDCD6-\\uDCFF\\uDD09-\\uDFFF]|\\uD82C[\\uDD1F-\\uDD4F\\uDD53-\\uDD63\\uDD68-\\uDD6F\\uDEFC-\\uDFFF]|\\uD82F[\\uDC6B-\\uDC"
    "6F\\uDC7D-\\uDC7F\\uDC89-\\uDC8F\\uDC9A-\\uDC9C\\uDC9F-\\uDFFF]|\\uD834[\\uDC00-\\uDD64\\uDD6A-\\uDD6C\\uDD73-\\uDD7A\\uDD83\\uDD84\\uDD8C"
    "-\\uDDA9\\uDDAE-\\uDE41\\uDE45-\\uDFFF]|\\uD835[\\uDC55\\uDC9D\\uDCA0\\uDCA1\\uDCA3\\uDCA4\\uDCA7\\uDCA8\\uDCAD\\uDCBA\\uDCBC\\uDCC4\\uDD06"
    "\\uDD0B\\uDD0C\\uDD15\\uDD1D\\uDD3A\\uDD3F\\uDD45\\uDD47-\\uDD49\\uDD51\\uDEA6\\uDEA7\\uDEC1\\uDEDB\\uDEFB\\uDF15\\uDF35\\uDF4F\\uDF6F\\uDF8"
    "9\\uDFA9\\uDFC3\\uDFCC\\uDFCD]|\\uD836[\\uDC00-\\uDDFF\\uDE37-\\uDE3A\\uDE6D-\\uDE74\\uDE76-\\uDE83\\uDE85-\\uDE9A\\uDEA0\\uDEB0-\\uDFFF]|"
    "\\uD838[\\uDC07\\uDC19\\uDC1A\\uDC22\\uDC25\\uDC2B-\\uDCFF\\uDD2D-\\uDD2F\\uDD3E\\uDD3F\\uDD4A-\\uDD4D\\uDD4F-\\uDEBF\\uDEFA-\\uDFFF]|\\uD8"
    "3A[\\uDCC5-\\uDCCF\\uDCD7-\\uDCFF\\uDD4C-\\uDD4F\\uDD5A-\\uDFFF]|\\uD83B[\\uDC00-\\uDDFF\\uDE04\\uDE20\\uDE23\\uDE25\\uDE26\\uDE28\\uDE33\\"
    "uDE38\\uDE3A\\uDE3C-\\uDE41\\uDE43-\\uDE46\\uDE48\\uDE4A\\uDE4C\\uDE50\\uDE53\\uDE55\\uDE56\\uDE58\\uDE5A\\uDE5C\\uDE5E\\uDE60\\uDE63\\uDE6"
    "5\\uDE66\\uDE6B\\uDE73\\uDE78\\uDE7D\\uDE7F\\uDE8A\\uDE9C-\\uDEA0\\uDEA4\\uDEAA\\uDEBC-\\uDFFF]|\\uD83C[\\uDC00-\\uDD2F\\uDD4A-\\uDD4F\\uDD"
    "6A-\\uDD6F\\uDD8A-\\uDFFF]|\\uD83E[\\uDC00-\\uDFEF\\uDFFA-\\uDFFF]|\\uD869[\\uDEDE-\\uDEFF]|\\uD86D[\\uDF35-\\uDF3F]|\\uD86E[\\uDC1E\\uDC"
    "1F]|\\uD873[\\uDEA2-\\uDEAF]|\\uD87A[\\uDFE1-\\uDFFF]|\\uD87E[\\uDE1E-\\uDFFF]|\\uD884[\\uDF4B-\\uDFFF]|\\uDB40[\\uDC00-\\uDCFF\\uDDF0-\\"
    "uDFFF]"
)


def github_slug(text: str) -> str:
    """Apply the pinned github-slugger filter without duplicate allocation."""
    encoded = text.lower().encode("utf-16-le")
    units = "".join(chr(encoded[i] | encoded[i + 1] << 8) for i in range(0, len(encoded), 2))
    retained = GITHUB_SLUG_FILTER.sub("", units)
    decoded = retained.encode("utf-16-le", "surrogatepass").decode("utf-16-le")
    return decoded.replace(" ", "-")


class InputFailure(Exception):
    def __init__(self, rule: str, message: str, path: str = "", target: str = ""):
        super().__init__(message)
        self.rule, self.path, self.target = rule, path, target


@dataclass(frozen=True)
class Entry:
    mode: str
    kind: str
    oid: str


class GitRepository:
    """Read immutable Git objects, never checkout files or invoke target scripts."""

    def __init__(self, path: str):
        self.path = Path(path).resolve()
        self.environment = os.environ.copy()
        for key in (
            "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_OBJECT_DIRECTORY",
            "GIT_ALTERNATE_OBJECT_DIRECTORIES", "GIT_REPLACE_REF_BASE",
        ):
            self.environment.pop(key, None)
        self.environment.update({
            "GIT_NO_REPLACE_OBJECTS": "1", "GIT_NO_LAZY_FETCH": "1",
            "GIT_TERMINAL_PROMPT": "0", "GIT_OPTIONAL_LOCKS": "0",
        })
        self.blobs: dict[str, bytes] = {}
        self.trees: dict[str, GitTree] = {}

    def run(self, *arguments: str, allow_failure: bool = False) -> subprocess.CompletedProcess:
        try:
            result = subprocess.run(
                ["git", "-c", "core.fsmonitor=false", "-C", str(self.path), *arguments],
                stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=self.environment,
                timeout=30, check=False,
            )
        except (OSError, subprocess.TimeoutExpired) as error:
            raise InputFailure("input.git", f"Cannot read Git objects: {error}") from error
        if result.returncode and not allow_failure:
            # Do not print object contents or inherited credential configuration.
            raise InputFailure("input.git", f"Git {arguments[0]} could not read the requested object/history.")
        return result

    def resolve(self, ref: str) -> str:
        if not ref or any(ord(char) < 32 for char in ref):
            raise InputFailure("input.git", "A nonempty, valid commit reference is required.")
        return self.run("rev-parse", "--verify", "--end-of-options", f"{ref}^{{commit}}").stdout.decode("ascii").strip()

    def tree(self, ref: str) -> GitTree:
        commit = self.resolve(ref)
        if commit not in self.trees:
            self.trees[commit] = GitTree(self, commit)
        return self.trees[commit]

    def is_ancestor(self, base: str, candidate: str) -> bool:
        result = self.run("merge-base", "--is-ancestor", base, candidate, allow_failure=True)
        if result.returncode not in (0, 1):
            raise InputFailure("input.git", "Cannot establish the requested ancestry; fetch complete history first.")
        return result.returncode == 0

    def blob(self, entry: Entry, path: str, limit: int) -> bytes:
        if entry.kind != "blob" or entry.mode not in REGULAR_MODES:
            raise InputFailure("input.git", "A regular tracked Git blob is required.", path)
        if entry.oid not in self.blobs:
            size = int(self.run("cat-file", "-s", entry.oid).stdout)
            if size > limit:
                raise InputFailure("input.size", f"Text exceeds the explicit {limit}-byte parsing limit.", path)
            self.blobs[entry.oid] = self.run("cat-file", "blob", entry.oid).stdout
        data = self.blobs[entry.oid]
        if len(data) > limit:
            raise InputFailure("input.size", f"Text exceeds the explicit {limit}-byte parsing limit.", path)
        return data


class GitTree:
    def __init__(self, repo: GitRepository, commit: str):
        self.repo, self.commit = repo, commit
        self.oid = repo.run("rev-parse", f"{commit}^{{tree}}").stdout.decode("ascii").strip()
        self.entries: dict[str, Entry] = {}
        self.directories = {""}
        for record in repo.run("ls-tree", "-rz", "--full-tree", commit).stdout.split(b"\0"):
            if not record:
                continue
            header, raw_path = record.split(b"\t", 1)
            try:
                path = raw_path.decode("utf-8")
            except UnicodeDecodeError as error:
                raise InputFailure("input.encoding", "Git contains a path that is not valid UTF-8.") from error
            mode, kind, oid = header.decode("ascii").split()
            self.entries[path] = Entry(mode, kind, oid)
            parts = path.split("/")
            self.directories.update("/".join(parts[:i]) for i in range(1, len(parts)))
        self.markdown = sorted(path for path in self.entries if path.endswith(".md"))

    def identity(self) -> dict:
        return {"commit": self.commit, "tree": self.oid}

    def text(self, path: str, limit: int = MAX_TEXT_BYTES) -> str:
        entry = self.entries.get(path)
        if entry is None:
            raise InputFailure("input.git", "Required path is absent from the selected Git tree.", path)
        data = self.repo.blob(entry, path, limit)
        try:
            text = data.decode("utf-8")
        except UnicodeDecodeError as error:
            raise InputFailure("input.encoding", "Text is not valid UTF-8.", path) from error
        if "\0" in text:
            raise InputFailure("input.encoding", "Text contains a NUL byte.", path)
        return text


class Report:
    def __init__(self, mode: str):
        self.data = {
            "schema_version": 1, "mode": mode, "status": "error", "source": None,
            "baseline": None,
            "checker": {
                "version": VERSION,
                "source_sha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                "parser_versions": {
                    name: version(name) for name in ("markdown-it-py", "mdurl", "PyYAML")
                },
                "slugger_source": SLUGGER_SOURCE,
                "slugger_filter_sha256": hashlib.sha256(GITHUB_SLUG_FILTER.pattern.encode("ascii")).hexdigest(),
                "unicode_case_version": unicodedata.unidata_version,
            },
            "scope": {
                "source": "committed-git-tree", "working_tree_bytes": "NOT_INSPECTED",
                "markdown_dialect": "CommonMark + tables + strikethrough",
                "bare_url_autolinks": "NOT_PARSED",
                "unassigned_heading_codepoints": "REJECTED",
                "catalogue_and_protection": "REQUIRED" if mode == "check" else "NOT_APPLICABLE",
                "external_url_reachability": "NOT_EVALUATED", "historical_exceptions": "NONE",
                "owning_validators": "NOT_EXECUTED", "excluded_formats": [".mdx"],
            },
            "counts": {key: 0 for key in (
                "markdown_files", "parsed_links", "external_links", "local_links",
                "validated_fragments", "headings", "catalogue_entries", "asset_references",
                "unique_assets", "protected_documents", "immutable_files",
            )},
            "findings": [],
        }

    def finding(self, rule: str, path: str, target: str, message: str, line: int | None = None):
        self.data["findings"].append({
            "rule": rule, "path": path, "line": line, "target": target, "message": message,
        })

    def input_error(self, error: InputFailure):
        self.finding(error.rule, error.path, error.target, str(error))

    def finish(self) -> int:
        findings = self.data["findings"]
        code = 2 if any(item["rule"].startswith("input.") for item in findings) else int(bool(findings))
        self.data["status"] = ("pass", "fail", "error")[code]
        return code


@dataclass(frozen=True)
class Reference:
    path: str
    line: int
    target: str


class HtmlReferences(HTMLParser):
    def __init__(self, path: str, line: int, anchors: set[str], references: list[Reference]):
        super().__init__(convert_charrefs=True)
        self.path, self.line = path, line
        self.anchors, self.references = anchors, references

    def handle_starttag(self, tag: str, attributes):
        # HTML uses the first occurrence of a duplicated attribute.
        attrs = {}
        for key, value in attributes:
            attrs.setdefault(key, value)
        for name in (("id", "name") if tag == "a" else ("id",)):
            if attrs.get(name):
                self.anchors.add(attrs[name])
        navigation = {"a": "href", "img": "src", "audio": "src", "video": "src", "source": "src"}
        attribute = navigation.get(tag)
        if attribute in attrs and attrs[attribute] is not None:
            self.references.append(Reference(self.path, self.line + self.getpos()[0] - 1, attrs[attribute]))
        if tag == "video" and attrs.get("poster") is not None:
            self.references.append(Reference(self.path, self.line + self.getpos()[0] - 1, attrs["poster"]))

    handle_startendtag = handle_starttag


def without_frontmatter(text: str) -> str:
    lines = text.removeprefix("\ufeff").splitlines(keepends=True)
    if lines and lines[0].rstrip("\r\n") == "---":
        for index in range(1, len(lines)):
            if lines[index].rstrip("\r\n") in {"---", "..."}:
                return "\n" * (index + 1) + "".join(lines[index + 1:])
    return "".join(lines)


def heading_text(children) -> str:
    parts = []
    for child in children or []:
        if child.type in {"text", "code_inline"}:
            parts.append(child.content)
        elif child.type == "image":
            parts.append(heading_text(child.children))
        elif child.type in {"softbreak", "hardbreak"}:
            parts.append("\n")
    return "".join(parts)


def parse_documents(tree: GitTree, report: Report) -> tuple[dict[str, set[str]], list[Reference]]:
    parser = MarkdownIt("commonmark", {"html": True}).enable(["table", "strikethrough"])
    # Retain malformed escapes and Unicode for our strict, single-decode path
    # rule instead of normalizing a bad percent escape into a different URL.
    parser.normalizeLink = lambda value: value
    anchors: dict[str, set[str]] = {}
    references: list[Reference] = []
    report.data["counts"]["markdown_files"] = len(tree.markdown)
    if not tree.markdown:
        raise InputFailure("input.scope", "The selected tree contains no scoped Markdown documents.")
    for path in tree.markdown:
        entry = tree.entries[path]
        if entry.mode not in REGULAR_MODES or entry.kind != "blob":
            report.finding("link.unsupported", path, path, "Markdown symlinks and gitlinks are not supported.")
            continue
        try:
            tokens = parser.parse(without_frontmatter(tree.text(path)))
        except InputFailure as error:
            report.input_error(error)
            continue
        current: set[str] = set()
        automatic: dict[str, int] = {}
        automatic_supported = True
        anchors[path] = current
        for index, token in enumerate(tokens):
            line = (token.map[0] + 1) if token.map else 1
            if token.type == "heading_open":
                visible = heading_text(tokens[index + 1].children)
                report.data["counts"]["headings"] += 1
                if any(unicodedata.category(char) == "Cn" for char in visible):
                    automatic_supported = False
                    report.finding(
                        "link.unsupported", path, visible,
                        "Heading contains a code point unassigned in the reported Unicode casing version; "
                        "this and subsequent automatic heading IDs cannot be verified.", line,
                    )
                if not automatic_supported:
                    # Later suffix allocation can depend on the uncertain ID.
                    # Explicit HTML anchors remain independently verifiable.
                    continue
                original = github_slug(visible)
                slug = original
                while slug in automatic:
                    automatic[original] += 1
                    slug = f"{original}-{automatic[original]}"
                automatic[slug] = 0
                current.add(slug)
            if token.type == "html_block":
                HtmlReferences(path, line, current, references).feed(token.content)
            if token.type != "inline":
                continue
            inline_line = line
            for child in token.children or []:
                if child.type == "link_open":
                    references.append(Reference(path, inline_line, child.attrGet("href") or ""))
                elif child.type == "image":
                    references.append(Reference(path, inline_line, child.attrGet("src") or ""))
                elif child.type == "html_inline":
                    HtmlReferences(path, inline_line, current, references).feed(child.content)
                if child.type in {"softbreak", "hardbreak"}:
                    inline_line += 1
    return anchors, references


def has_controls(value: str) -> bool:
    return any(unicodedata.category(char) in {"Cc", "Cs"} for char in value)


def decode_url_component(value: str) -> str:
    if re.search(r"%(?![0-9a-fA-F]{2})", value):
        raise ValueError("Malformed percent escape.")
    decoded = unquote(value, encoding="utf-8", errors="strict")
    if has_controls(decoded) or "\\" in decoded:
        raise ValueError("Control characters and backslashes are unsupported in local references.")
    return decoded


def local_path(source: str, path: str) -> str:
    if not path:
        return source
    parts = [] if path.startswith("/") else source.split("/")[:-1]
    for part in path.split("/"):
        if part in {"", "."}:
            continue
        if part == "..":
            if not parts:
                raise ValueError("Reference escapes the selected repository tree; cross-tree addressing is unsupported.")
            parts.pop()
        else:
            parts.append(part)
    return "/".join(parts)


def unsupported_target(tree: GitTree, path: str) -> bool:
    parts = path.split("/")
    for index in range(1, len(parts) + 1):
        entry = tree.entries.get("/".join(parts[:index]))
        if entry and (entry.mode not in REGULAR_MODES or entry.kind != "blob"):
            return True
    return False


def validate_links(tree: GitTree, report: Report):
    anchors, references = parse_documents(tree, report)
    counts = report.data["counts"]
    counts["parsed_links"] = len(references)
    for reference in references:
        raw = reference.target
        finding = lambda rule, message: report.finding(rule, reference.path, raw, message, reference.line)
        try:
            if has_controls(raw) or "\\" in raw:
                raise ValueError("Control characters and backslashes are unsupported in references.")
            address = urlsplit(raw)
            if address.scheme or address.netloc:
                if address.scheme.lower() == "file" or re.match(r"^[A-Za-z]:", raw):
                    finding("link.unsupported", "Host filesystem addresses are outside the selected Git tree.")
                else:
                    counts["external_links"] += 1
                continue
            counts["local_links"] += 1
            path = decode_url_component(address.path)
            fragment = decode_url_component(address.fragment)
            target = local_path(reference.path, path)
        except (ValueError, UnicodeError) as error:
            finding("link.path", str(error))
            continue
        if unsupported_target(tree, target):
            finding("link.unsupported", f"Symlink or gitlink target is unsupported: {target}")
        elif path.endswith("/") and target not in tree.directories:
            finding("link.missing", f"Directory target does not exist in the selected tree: {target}")
        elif target not in tree.entries and target not in tree.directories:
            finding("link.missing", f"Exact-case target does not exist in the selected tree: {target}")
        elif fragment:
            if target not in anchors:
                finding("link.unsupported", f"Fragments on non-Markdown or unparsed targets are not validated: {target}")
            elif fragment not in anchors[target]:
                finding("link.anchor", f"Anchor {fragment!r} does not exist in {target}.")
            else:
                counts["validated_fragments"] += 1


class StrictLoader(yaml.SafeLoader):
    """Reject duplicate keys, aliases and excessive nesting before construction."""

    def __init__(self, stream):
        super().__init__(stream)
        self.depth = 0

    def compose_node(self, parent, index):
        if self.check_event(yaml.AliasEvent) or getattr(self.peek_event(), "anchor", None):
            raise yaml.composer.ComposerError(
                None, None, "YAML anchors and aliases are unsupported.", self.peek_event().start_mark,
            )
        self.depth += 1
        try:
            if self.depth > 64:
                raise yaml.composer.ComposerError(
                    None, None, "Catalogue nesting exceeds 64.", self.peek_event().start_mark,
                )
            return super().compose_node(parent, index)
        finally:
            self.depth -= 1

    def construct_mapping(self, node, deep=False):
        if not isinstance(node, yaml.MappingNode):
            raise yaml.YAMLError("A YAML mapping was expected.")
        mapping = {}
        for key_node, value_node in node.value:
            key = self.construct_object(key_node, deep=deep)
            if not isinstance(key, str):
                raise yaml.constructor.ConstructorError(
                    None, None, "Catalogue mapping keys must be strings.", key_node.start_mark,
                )
            if key in mapping:
                raise yaml.constructor.ConstructorError(
                    None, None, "Duplicate catalogue mapping key.", key_node.start_mark,
                )
            mapping[key] = self.construct_object(value_node, deep=deep)
        return mapping


def nonblank(value) -> bool:
    return isinstance(value, str) and bool(value.strip())


def string_array(value) -> bool:
    return isinstance(value, list) and all(nonblank(item) for item in value) and len(value) == len(set(value))


def repository_path(value) -> bool:
    if not nonblank(value) or has_controls(value) or "\\" in value or value.startswith("/"):
        return False
    parts = value.split("/")
    return all(part not in {"", ".", ".."} for part in parts) and not re.match(r"^[A-Za-z][A-Za-z0-9+.-]*:", value)


def load_catalogue(tree: GitTree, path: str) -> dict:
    if not repository_path(path) or path not in tree.entries:
        raise InputFailure("input.catalogue", "The required catalogue is absent from the selected Git tree.", path)
    try:
        data = yaml.load(tree.text(path, MAX_CATALOGUE_BYTES), Loader=StrictLoader)
    except (yaml.YAMLError, RecursionError) as error:
        mark = getattr(error, "problem_mark", None)
        location = f" at line {mark.line + 1}, column {mark.column + 1}" if mark else ""
        # PyYAML's formatted exception includes source lines; keep diagnostics
        # useful without copying candidate file contents into CI logs.
        raise InputFailure(
            "input.catalogue", f"Cannot parse catalogue ({type(error).__name__}){location}.", path,
        ) from error
    if not isinstance(data, dict):
        raise InputFailure("input.catalogue", "Catalogue root must be a mapping.", path)
    return data


def validate_catalogue(tree: GitTree, data: dict, path: str, report: Report, count: bool) -> dict[str, dict]:
    schema = lambda message, target="": report.finding("catalogue.schema", path, target, message)
    if set(data) != {"version", "scope", "documents"}:
        schema("Version 1 requires exactly version, scope and documents at the root.")
    if type(data.get("version")) is not int or data["version"] != 1:
        schema("Only integer catalogue version 1 is supported.", "version")
    documents = data.get("documents")
    if not isinstance(documents, list):
        schema("documents must be an array.", "documents")
        documents = []
    if count:
        report.data["counts"]["catalogue_entries"] = len(documents)
    records, listed_paths, assets = {}, [], []
    for index, record in enumerate(documents):
        location = f"documents[{index}]"
        if not isinstance(record, dict):
            schema("Every document must be a mapping.", location)
            continue
        if not REQUIRED_RECORD_FIELDS.issubset(record) or set(record) - RECORD_FIELDS:
            schema("Record has missing required fields or unsupported fields.", location)
        for field in REQUIRED_RECORD_FIELDS | (OPTIONAL_RECORD_STRINGS & set(record)):
            if not nonblank(record.get(field)):
                schema(f"{field} must be a nonempty string.", location)
        for field, allowed in (("kind", KINDS), ("lifecycle", LIFECYCLES)):
            if not isinstance(record.get(field), str) or record[field] not in allowed:
                schema(f"Unsupported {field} value.", location)
        for field in ("integrity", "related_assets"):
            if field in record and not string_array(record[field]):
                schema(f"{field} must be an array of unique nonempty strings.", location)
        flags = record.get("integrity", [])
        if string_array(flags) and set(flags) - INTEGRITY_FLAGS:
            schema("Unknown integrity flag.", location)
        owner = record.get("path")
        if not repository_path(owner) or not owner.endswith(".md"):
            schema("Document path must be a normalized repository-relative .md path.", location)
            continue
        listed_paths.append(owner)
        if owner in records:
            schema("Duplicate document path.", owner)
        else:
            records[owner] = record
        companions = record.get("related_assets", [])
        if not string_array(companions):
            continue
        for asset in companions:
            assets.append(asset)
            if not repository_path(asset) or unsupported_target(tree, asset):
                report.finding("asset.path", owner, asset, "Companion must be a regular, repository-relative Git blob.")
            elif asset not in tree.entries:
                report.finding("asset.missing", owner, asset, "Exact-case companion is absent from the selected Git tree.")
    if listed_paths != sorted(listed_paths):
        schema("Document records must be sorted by exact path.", "documents")
    for missing in sorted(set(tree.markdown) - set(records)):
        report.finding("catalogue.coverage", path, missing, "Tracked Markdown document has no catalogue entry.")
    for extra in sorted(set(records) - set(tree.markdown)):
        report.finding("catalogue.coverage", path, extra, "Catalogue entry has no exact tracked Markdown path.")
    if count:
        report.data["counts"].update(asset_references=len(assets), unique_assets=len(set(assets)))
    validate_scope(tree, data.get("scope"), path, report)
    return records


def validate_scope(tree: GitTree, scope, path: str, report: Report):
    finding = lambda message, target="scope": report.finding("catalogue.scope", path, target, message)
    if not isinstance(scope, dict) or set(scope) != SCOPE_FIELDS:
        finding("Version 1 scope requires its complete, recognized provenance fields.")
        return
    if not nonblank(scope["repository"]):
        finding("scope.repository must be a nonempty string.")
    if scope["file_suffix"] != ".md" or scope["excluded_formats"] != [".mdx"]:
        finding("Version 1 inventories .md and explicitly excludes .mdx.")
    for field in ("tracked_markdown_at_base", "expected_markdown_count"):
        if type(scope[field]) is not int or scope[field] < 0:
            finding(f"scope.{field} must be a nonnegative integer.", field)
    additions = scope["markdown_added_by_this_change"]
    if not string_array(additions) or any(not repository_path(item) or not item.endswith(".md") for item in additions):
        finding("Declared additions must be unique normalized Markdown paths.")
        return
    if additions != sorted(additions):
        finding("Declared additions must be sorted by exact path.")
    for field in ("base_commit", "base_tree"):
        if not isinstance(scope[field], str) or not re.fullmatch(r"[0-9a-f]{40}", scope[field]):
            finding(f"scope.{field} must be a full lowercase Git object identity.", field)
            return
    try:
        base = tree.repo.tree(scope["base_commit"])
        if not tree.repo.is_ancestor(base.commit, tree.commit):
            finding("Provenance base must be an ancestor of the selected source.")
        if base.oid != scope["base_tree"]:
            finding("Provenance tree does not match its named commit.", "base_tree")
        if len(base.markdown) != scope["tracked_markdown_at_base"]:
            finding("Recorded base Markdown count does not match the Git tree.", "tracked_markdown_at_base")
        expected = set(base.markdown) | set(additions)
        if set(additions) & set(base.markdown):
            finding("A declared addition already exists in the provenance base.")
        if expected != set(tree.markdown):
            finding("Version 1 requires the exact base-plus-additions path set; removals require a refreshed provenance base.")
        if scope["expected_markdown_count"] != len(tree.markdown) or scope["expected_markdown_count"] != len(expected):
            finding("Expected Markdown count does not match the resulting inventory.", "expected_markdown_count")
    except InputFailure as error:
        report.input_error(error)


def record_values(record: dict, field: str) -> set[str]:
    value = record.get(field, [])
    return set(value) if string_array(value) else set()


def validate_protection(base: GitTree, candidate: GitTree, before: dict, after: dict, report: Report):
    protected = {
        path for path in before.keys() | after.keys()
        if record_values(before.get(path, {}), "integrity") or record_values(after.get(path, {}), "integrity")
    }
    immutable: set[str] = set()
    for path in sorted(protected):
        old, new = before.get(path, {}), after.get(path, {})
        old_flags = record_values(old, "integrity")
        flags = old_flags | record_values(new, "integrity")
        for removed in sorted(old_flags - record_values(new, "integrity")):
            report.finding("protection.removed", path, removed, "Candidate metadata cannot remove a trusted baseline obligation.")
        old_assets = record_values(old, "related_assets")
        new_assets = record_values(new, "related_assets")
        for removed in sorted(old_assets - new_assets):
            report.finding("protection.binding", path, removed, "A trusted protected companion binding was removed.")
        if "source_ref" in old and old["source_ref"] != new.get("source_ref"):
            report.finding("protection.binding", path, "source_ref", "A trusted protected source binding was changed or removed.")
        entry = candidate.entries.get(path)
        if "fixed-path-consumer" in flags and (entry is None or entry.mode not in REGULAR_MODES or entry.kind != "blob"):
            report.finding("protection.fixed_path", path, path, "The trusted fixed path must remain a regular tracked file.")
        dependencies = {path} | old_assets | new_assets
        if "generated" in flags:
            changed = sorted(item for item in dependencies if base.entries.get(item) != candidate.entries.get(item) or item not in base.entries)
            if changed:
                report.finding("protection.generated", path, path, "Generated view or owning source changed; separately qualified owning-validator execution is required: " + ", ".join(changed))
        if "immutable-evidence" not in flags:
            continue
        immutable.update(dependencies)
        if path.startswith(CAMPAIGNS) and "/" in path[len(CAMPAIGNS):]:
            bundle = CAMPAIGNS + path[len(CAMPAIGNS):].split("/", 1)[0] + "/"
            immutable.update(item for item in base.entries.keys() | candidate.entries.keys() if item.startswith(bundle))
    for path in sorted(immutable):
        if path not in base.entries or base.entries.get(path) != candidate.entries.get(path):
            report.finding("protection.immutable", path, path, "Immutable path, Git mode and blob identity must match the externally selected baseline.")
    report.data["counts"].update(protected_documents=len(protected), immutable_files=len(immutable))


def check(args, report: Report):
    repo = GitRepository(args.repo)
    candidate = repo.tree(args.ref)
    report.data["source"] = candidate.identity()
    if args.mode == "check":
        base = repo.tree(args.baseline)
        report.data["baseline"] = base.identity()
        if not repo.is_ancestor(base.commit, candidate.commit):
            raise InputFailure("input.git", "Trusted baseline must be an ancestor of, or equal to, the selected source.")
        baseline_catalogue = load_catalogue(base, args.catalog)
        candidate_catalogue = load_catalogue(candidate, args.catalog)
        # Baseline catalogue flaws remain visible findings, and cannot disappear
        # merely because the candidate has stopped declaring a dependency.
        before = validate_catalogue(base, baseline_catalogue, args.catalog, report, count=False)
        after = validate_catalogue(candidate, candidate_catalogue, args.catalog, report, count=True)
        validate_protection(base, candidate, before, after, report)
    validate_links(candidate, report)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="mode", required=True)
    for mode in ("check", "links"):
        command = commands.add_parser(mode)
        command.add_argument("--repo", required=True, help="Repository containing all required Git objects.")
        command.add_argument("--ref", required=True, help="Committed candidate source; working-tree edits are not inspected.")
        command.add_argument("--json", required=True, help="Explicit JSON report output path.")
        if mode == "check":
            command.add_argument("--baseline", required=True, help="Externally selected trusted protection baseline.")
            command.add_argument("--catalog", default="docs/catalog.yml")
    args = parser.parse_args()
    report = Report(args.mode)
    try:
        check(args, report)
    except InputFailure as error:
        report.input_error(error)
    except (OSError, ValueError, RecursionError) as error:
        report.finding("input.runtime", "", "", f"Incomplete validation: {type(error).__name__}: {error}")
    code = report.finish()
    output = Path(args.json)
    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps(report.data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    except OSError as error:
        print(f"Cannot write requested validation report: {error}", file=sys.stderr)
        return 2
    counts = report.data["counts"]
    print(f"Documentation integrity {report.data['status']}: {counts['markdown_files']} Markdown files, "
          f"{counts['parsed_links']} parsed references, {len(report.data['findings'])} findings; report: {output}")
    for finding in report.data["findings"]:
        print(f"{finding['rule']}: {finding['path']}: {finding['message']}")
    return code


if __name__ == "__main__":
    sys.exit(main())
