import sys
sys.stdout.reconfigure(encoding="utf-8")
d = sys.argv[2] if len(sys.argv) > 2 else "."
R = {}
for l in open(d + "/real_all.txt", encoding="utf-8"):
    c = l.rstrip("\n").split("\t")
    if len(c) == 4: R[c[0]] = (int(c[1]), int(c[2]), float(c[3]))
O = {}
for l in open(d + "/ours_all.txt", encoding="utf-8"):
    c = l.rstrip("\n").split("\t")
    if len(c) >= 4 and c[1].isdigit(): O[c[0]] = (int(c[1]), int(c[2]), float(c[3]))
both = [k for k in R if k in O]
gv = sum(R[k][0] == O[k][0] for k in both)
fv = sum(R[k][1] == O[k][1] for k in both)
hv = sum(abs(R[k][2] - O[k][2]) <= 0.15 for k in both)
al = sum(R[k][0] == O[k][0] and R[k][1] == O[k][1] and abs(R[k][2] - O[k][2]) <= 0.15 for k in both)
print(f"arvores {len(both)} | galhos iguais {gv} | frondes iguais {fv} | altura igual {hv} | tudo igual {al}")
show = int(sys.argv[1]) if len(sys.argv) > 1 else 10
bad = sorted((k for k in both if R[k][:2] != O[k][:2]), key=lambda k: -abs(R[k][1] - O[k][1]) - abs(R[k][0] - O[k][0]))
for k in bad[:show]: print(k[:24], "real", R[k], "nosso", O[k])
