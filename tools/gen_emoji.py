# Regenerates src/MonopolyBank/Data/emoji.txt (token picker) from Unicode emoji-test.txt:
#   curl -o emoji-test.txt https://unicode.org/Public/emoji/16.0/emoji-test.txt
#   python tools/gen_emoji.py emoji-test.txt src/MonopolyBank/Data/emoji.txt
import sys, re
src, out = sys.argv[1], sys.argv[2]
keys = {"Smileys & Emotion": "smileys", "People & Body": "people", "Animals & Nature": "animals", "Food & Drink": "food",
        "Travel & Places": "travel", "Activities": "activities", "Objects": "objects", "Symbols": "symbols", "Flags": "flags"}
groups, cur = {}, None
for line in open(src, encoding="utf-8"):
    m = re.match(r"# group: (.+)", line)
    if m:
        cur = keys.get(m.group(1).strip()); continue
    if not cur or line.startswith("#") or "; fully-qualified" not in line: continue
    cps, rest = line.split(";", 1)
    cps = [int(c, 16) for c in cps.split()]
    if any(0x1F3FB <= c <= 0x1F3FF for c in cps): continue  # skin tones
    ver = float(re.search(r"# \S+ E(\d+\.\d+)", rest).group(1))
    if ver > 15.0: continue  # newer ones show as boxes on many phones
    groups.setdefault(cur, []).append("".join(map(chr, cps)))
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write("# Generated from Unicode emoji-test.txt 16.0: fully-qualified, Emoji <= 15.0, no skin tones. One group per line: key, then emoji.\n")
    for k in keys.values():
        f.write(k + " " + " ".join(groups[k]) + "\n")
        print(k, len(groups[k]))
