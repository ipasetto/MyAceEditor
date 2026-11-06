import sys, os, math
from collections import defaultdict
sys.stdout.reconfigure(encoding="utf-8")
# juiz: cada vértice do jogo precisa de um par no nosso a menos de TOL metros, e o total precisa bater
real, ours = sys.argv[1], sys.argv[2]
TOL = float(sys.argv[3]) if len(sys.argv) > 3 else 0.03
show = int(sys.argv[4]) if len(sys.argv) > 4 else 14
def rd(p):
    o = {"B": [], "F": [], "L": []}
    for l in open(p, encoding="utf-8"):
        c = l.split()
        if c and c[0] == "v": o[c[1]].append((float(c[2]), float(c[3]), float(c[4])))
    return o
def matched(R, O):
    C = 1.0
    G = defaultdict(list)
    for p in O: G[(int(p[0] // C), int(p[1] // C), int(p[2] // C))].append(p)
    n = 0
    for p in R:
        x, y, z = int(p[0] // C), int(p[1] // C), int(p[2] // C); ok = False
        for a in (-1, 0, 1):
            for b in (-1, 0, 1):
                for c in (-1, 0, 1):
                    for q in G.get((x + a, y + b, z + c), ()):
                        if (p[0] - q[0]) ** 2 + (p[1] - q[1]) ** 2 + (p[2] - q[2]) ** 2 < TOL * TOL: ok = True; break
                    if ok: break
                if ok: break
            if ok: break
        n += ok
    return n
rows = []
for f in sorted(os.listdir(real)):
    if not os.path.exists(os.path.join(ours, f)): continue
    R, O = rd(os.path.join(real, f)), rd(os.path.join(ours, f))
    b, fr = matched(R["B"], O["B"]), matched(R["F"], O["F"]); lf = matched(R["L"], O["L"])
    rows.append((f[:-8], len(R["B"]), len(O["B"]), b, len(R["F"]), len(O["F"]), fr, len(R["L"]), len(O["L"]), lf))
exB = sum(r[1] == r[2] == r[3] for r in rows)
exF = sum(r[4] == r[5] == r[6] for r in rows)
ex = sum(r[1] == r[2] == r[3] and r[4] == r[5] == r[6] for r in rows)
tb = sum(r[1] for r in rows); mb = sum(r[3] for r in rows); tf = sum(r[4] for r in rows); mf = sum(r[6] for r in rows)
exL = sum(r[7] == r[8] == r[9] for r in rows); tl = sum(r[7] for r in rows); ml = sum(r[9] for r in rows)
print(f"folhas exatas {exL} de {len(rows)} | vertices folha {ml}/{tl} | nosso total {sum(r[8] for r in rows)}")
print(f"arvores {len(rows)} | galhos exatos {exB} | frondes exatas {exF} | inteiras exatas {ex} | vertices galho {mb}/{tb} fronde {mf}/{tf}")
bad = sorted((r for r in rows if not (r[1] == r[2] == r[3] and r[4] == r[5] == r[6])), key=lambda r: (r[3] - r[1]) + (r[6] - r[4]))
for r in bad[:show]:
    print(f"  {r[0][:22]:22} galho jogo {r[1]} nosso {r[2]} par {r[3]} | fronde jogo {r[4]} nosso {r[5]} par {r[6]} | folha jogo {r[7]} nosso {r[8]} par {r[9]}")
