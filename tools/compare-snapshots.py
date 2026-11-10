import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / 'sandbox/Game'


def read_es3(path):
    text = path.read_text(encoding='utf-8-sig')
    output = []
    index = 0
    while index < len(text):
        char = text[index]
        if char == '"':
            output.append(char)
            index += 1
            while index < len(text):
                char = text[index]
                if char == '\\':
                    following = text[index + 1]
                    if following not in '"\\/bfnrtu':
                        output.append(following)
                    else:
                        output.extend([char, following])
                    index += 2
                    continue
                output.append(char)
                index += 1
                if char == '"':
                    break
            continue
        if char in '{,':
            output.append(char)
            index += 1
            match = re.compile(r'\s*(-?\d+)\s*:').match(text, index)
            if match:
                output.append(json.dumps(match.group(1)) + ':')
                index = match.end()
            continue
        output.append(char)
        index += 1
    return json.loads(''.join(output))


if __name__ == '__main__':
    before_path = Path((ROOT / 'test-snapshot-before.txt').read_text()) / 'save.es3'
    after_path = Path((ROOT / 'test-snapshot-after.txt').read_text()) / 'save.es3'
    before, after = read_es3(before_path), read_es3(after_path)
    differences = []

    def compare(left, right, location):
        if isinstance(left, dict) and isinstance(right, dict):
            for key in sorted(set(left) | set(right)):
                compare(left.get(key), right.get(key), location + '.' + key)
        elif isinstance(left, list) and isinstance(right, list):
            if left != right:
                differences.append((location, f'list[{len(left)}]', f'list[{len(right)}]'))
        elif left != right:
            differences.append((location, str(left)[:140], str(right)[:140]))

    for key in sorted(set(before) | set(after)):
        if key == 'saveInfoData':
            continue
        compare(before.get(key), after.get(key), key)
    for difference in differences[:60]:
        print(difference)
    report = {'before': str(before_path), 'after': str(after_path), 'differences': differences}
    (ROOT / 'test-comparison.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('DIFFERENCE_COUNT', len(differences))
