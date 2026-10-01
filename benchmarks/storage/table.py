import sys, collections
rows=[l.strip().split('|') for l in open(sys.argv[1]) if l.startswith('MAPLEBENCH|') and len(l.split('|'))>=7]
data=collections.OrderedDict()
variants=[]
for _,v,n,op,first,med,extra in rows:
    if v not in variants: variants.append(v)
    data.setdefault((int(n),op),{})[v]=(float(first),float(med),extra)
for n in sorted({k[0] for k in data}):
    print(f"\n### {n:,} notes")
    print("| operation | " + " | ".join(variants) + " |")
    print("|---|" + "---|"*len(variants))
    for (nn,op),vals in data.items():
        if nn!=n: continue
        cells=[]
        for v in variants:
            if v not in vals: cells.append(""); continue
            f,m,e=vals[v]
            if op=="database size": cells.append(e)
            elif op.startswith("bulk") or "cold" in op or "first query" in op: cells.append(f"{f:,.0f} ms" + (f" ({e})" if e and 'index' in e else ""))
            else: cells.append(f"{m:.2f} ms")
        print(f"| {op} | " + " | ".join(cells) + " |")
