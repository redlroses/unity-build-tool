import sys
import re

def main():
    if len(sys.argv) < 3:
        sys.exit(1)

    version = sys.argv[1].lstrip('v')
    output_file = sys.argv[2]
    changelog_path = sys.argv[3] if len(sys.argv) > 3 else 'CHANGELOG.md'

    try:
        with open(changelog_path, 'r', encoding='utf-8') as f:
            content = f.read()
        pattern = rf'## \[?{re.escape(version)}\]?[^\n]*\n(.*?)(?=\n## |\Z)'
        match = re.search(pattern, content, re.DOTALL)
        notes = match.group(1).strip() if match else ''
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write(notes)
    except Exception as e:
        print(f"Warning: Failed to extract release notes: {e}")
        with open(output_file, 'w', encoding='utf-8') as f:
            f.write('')

if __name__ == '__main__':
    main()
