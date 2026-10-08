"""Strict diagnostic JSON input; accepts only UTF-8 with one optional leading BOM."""
import json
from pathlib import Path
from common import PerformanceContractError


def _object(pairs):
    value = {}
    for key, item in pairs:
        if key in value:
            raise ValueError('duplicate JSON field')
        value[key] = item
    return value


def _constant(_):
    raise ValueError('nonfinite JSON number')


def decode_json(data, expected_type=dict):
    try:
        value = json.loads(data.decode('utf-8-sig'), object_pairs_hook=_object, parse_constant=_constant)
        if not isinstance(value, expected_type):
            raise ValueError('unexpected JSON root type')
        return value
    except (UnicodeError, ValueError) as error:
        raise PerformanceContractError('invalid diagnostic JSON input') from error


def load_json(path, expected_type=dict):
    try:
        data = Path(path).read_bytes()
    except OSError as error:
        raise PerformanceContractError('missing diagnostic JSON input') from error
    return decode_json(data, expected_type)
