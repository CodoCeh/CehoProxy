#!/usr/bin/env python3
import re, sys, pathlib, urllib.request, urllib.error, unicodedata

root = pathlib.Path(__file__).resolve().parent.parent
files = [root / "README.md", *sorted((root / "docs").rglob("*.md"))]
link = re.compile(r"(?<!!)\[[^\]]*\]\(([^)\s]+)\)|!\[[^\]]*\]\(([^)\s]+)\)")


def slug(title):
    t = unicodedata.normalize("NFKC", title).strip().lower()
    t = re.sub(r"[^\w\- ]", "", t, flags=re.U).replace(" ", "-")
    return t


def anchors(path):
    out = set()
    for line in path.read_text(encoding="utf-8").splitlines():
        m = re.match(r"#{1,6}\s+(.*)", line)
        if m:
            out.add(slug(m.group(1)))
    return out


bad, external, unchecked = [], {}, []
for f in files:
    for m in link.finditer(f.read_text(encoding="utf-8")):
        target = m.group(1) or m.group(2)
        if target.startswith(("http://", "https://")):
            external.setdefault(target, []).append(f)
        elif target.startswith("mailto:"):
            continue
        elif f == root / "README.md" and target.startswith("../../"):
            external.setdefault("https://github.com/CodoCeh/CehoProxy/" + target[6:], []).append(f)
        else:
            p, _, frag = target.partition("#")
            dest = (f.parent / p).resolve() if p else f
            if not dest.exists():
                bad.append((f, target, "нет файла"))
            elif frag and dest.suffix == ".md" and slug(frag) not in anchors(dest):
                bad.append((f, target, "нет якоря"))

if "--online" in sys.argv:
    for url, where in external.items():
        code = None
        for method in ("HEAD", "GET"):
            try:
                req = urllib.request.Request(url, method=method, headers={"User-Agent": "Mozilla/5.0 link-check"})
                code = urllib.request.urlopen(req, timeout=20).status
            except urllib.error.HTTPError as e:
                code = e.code
            except Exception as e:
                code = str(e)
            if code == 200:
                break
        if isinstance(code, str):
            unchecked.append(url)
        elif code != 200:
            bad.append((where[0], url, f"HTTP {code}"))

print(f"файлов: {len(files)}, внешних ссылок: {len(external)}, ошибок: {len(bad)}")
for url in unchecked:
    print(f"  не проверено (нет сети до узла): {url}")
for f, t, why in bad:
    print(f"  {f.relative_to(root)}: {t} — {why}")
sys.exit(1 if bad else 0)
