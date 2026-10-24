#!/usr/bin/env python3
"""
gente_pisos.py — La gente de cada piso (21 de octubre de 2026).

Los pisos se veían solos: la multitud solo existía en el 9. Este script escribe en
contenido_piso9.json, para cada piso:
  - "multitudes": estudiantes y visitantes que caminan por el pasillo y entran a los
    espacios del piso (su recorrido son el pasillo, el lobby y los POI del piso), y
    parejas de estudiantes conversando quietos.
  - Para los pisos 4-8 y 10-12, personal fijo en "npcs": vigilante en el lobby, un
    profesor en uno de los laboratorios o salones, secretaría en la oficina del extremo y
    personal de aseo que recorre el piso.
Los visitantes no dan indicaciones (lo resuelve NpcManager): están conociendo el edificio.

Es idempotente: reemplaza lo que haya escrito antes (multitudes, y los npcs con id 'gN_').
Revisa que nadie quede dentro de un muro, un bloque o un mueble sólido.

Uso:   python3 Tools/gente_pisos.py          (después de Tools/pisos_planta.py)
"""
import json, os

ANCHO, LARGO = 39.3, 106.6
U_MID = 21.15
RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUTA = os.path.join(RAIZ, "Assets", "MovU", "Resources", "MovU", "contenido_piso9.json")
PLANTAS = {3: "Piso3/planta_piso3.json", 9: "Piso9/planta_piso9.json", 13: "Piso13/planta_piso13.json"}
for n in (4, 5, 6, 7, 8, 10, 11, 12):
    PLANTAS[n] = f"Pisos/planta_piso{n}.json"
NUEVOS = (4, 5, 6, 7, 8, 10, 11, 12)


def uv(u, v):
    return {"u": round(u / ANCHO, 4), "v": round(v / LARGO, 4)}


def dist_seg(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = dx * dx + dy * dy
    t = 0 if L == 0 else max(0, min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / L))
    return ((p[0] - a[0] - t * dx) ** 2 + (p[1] - a[1] - t * dy) ** 2) ** 0.5


def choca(planta, u, v, r=0.4):
    for m in planta["muros"]:
        if dist_seg((v, u), m["a"], m["b"]) < m.get("g", 0.3) / 2 + r:
            return "muro " + m.get("nombre", "")
    for c in planta.get("bloques", []) + [x for x in planta.get("muebles", []) if x.get("solido", True)]:
        if c["v0"] - r < v < c["v1"] + r and c["u0"] - r < u < c["u1"] + r:
            return c.get("nombre") or c.get("tipo")
    return None


def libre(planta, u, v):
    return choca(planta, u, v) is None


c = json.load(open(RUTA, encoding="utf-8"))
numeros = c["ajustes"]["numerosDePiso"]
pois = c["pois"]
c["npcs"] = [x for x in c["npcs"] if not x["id"].startswith("g")]
multitudes = []
fallos = []

for n in numeros:
    idx = numeros.index(n) + 1
    planta = json.load(open(os.path.join(RAIZ, "Assets", "MovU", "Models", PLANTAS[n]), encoding="utf-8"))
    del_piso = [p for p in pois if p["piso"] == idx]
    espacios = [p for p in del_piso if p["rotuloU"] > 0 and p["categoria"] not in ("Escalera", "Ascensor")]

    # --- Recorrido: pasillo, vestíbulo y lobby (el núcleo del 9) + los espacios del piso
    recorrido = []
    if n != 3:
        for v in (12.5, 20, 28, 36, 44, 52, 60, 68, 76, 84):
            recorrido.append((U_MID, v))
        recorrido += [(24.0, 97.0), (30.5, 98.5), (34.0, 97.8)]
    for p in espacios:
        recorrido.append((p["u"] * ANCHO, p["v"] * LARGO))
    recorrido = [(u, v) for u, v in recorrido if libre(planta, u, v)]

    # --- Parejas conversando
    if n == 3:
        e = next((p for p in del_piso if p["id"] == "p3_entrada"), None)
        parejas = [(e["u"] * ANCHO, e["v"] * LARGO)] if e else []
    else:
        parejas = [(23.6, 12.8), (30.5, 99.0)]
        if n == 11:
            parejas.append((12.0, 30.0))                 # en la Free Plaza
        if n == 9:
            parejas = [(23.6, 12.8)]
    parejas = [(u, v) for u, v in parejas
               if libre(planta, u - 0.6, v) and libre(planta, u + 0.6, v) and libre(planta, u, v - 0.6) and libre(planta, u, v + 0.6)]

    m = {"semilla": 100 + n, "piso": idx, "estudiantes": 7, "visitantes": 2,
         "recorrido": [uv(u, v) for u, v in recorrido], "parejas": [uv(u, v) for u, v in parejas]}
    if n in (3, 13):
        m["estudiantes"], m["visitantes"] = 5, 2
    if n == 9:
        # El 9 ya tiene su multitud (la de 'multitud'): solo se le suman las parejas.
        c["multitud"]["parejas"] = m["parejas"]
    else:
        multitudes.append(m)

    # --- Personal fijo de los pisos nuevos
    if n in NUEVOS:
        extremo = next((p for p in del_piso if abs(p["v"] * LARGO - 6.75) < 0.1), None)
        aula = next((p for p in espacios if p["categoria"] in ("Salon", "Laboratorio")
                     and round(p["rotuloU"] * ANCHO, 1) in (18.7, 23.6)), None)
        otro = next((p for p in espacios if aula and p["id"] != aula["id"] and p["categoria"] in ("Salon", "Laboratorio")), None)
        fijos = [{"id": f"g{n}_vigilante", "rol": "Vigilante", "nombre": "Vigilante", "u": 24.4, "v": 97.5, "yaw": 200,
                  "conducta": "quieto",
                  "frases": [f"Bienvenido al piso {n}.",
                             "Los ascensores están aquí mismo. La escalera de emergencia queda al fondo del pasillo, detrás de los lockers."]},
                 {"id": f"g{n}_aseo", "rol": "Aseo", "nombre": "Personal de aseo", "u": U_MID, "v": 50.0, "yaw": 0,
                  "conducta": "deambula", "frases": []}]
        if extremo:
            fijos.append({"id": f"g{n}_secretaria", "rol": "Administrativo", "nombre": "Secretaría",
                          "u": 20.0, "v": 5.5, "yaw": 0, "conducta": "quieto",
                          "frases": [f"Esta es {extremo['nombre']}.", "Si buscas un laboratorio, los rótulos están sobre cada puerta."]})
        if aula:
            ru, rv = aula["rotuloU"] * ANCHO, aula["rotuloV"] * LARGO
            derecha = ru < U_MID
            pu = ru - 2.8 if derecha else ru + 2.8
            frases = [f"Bienvenido a {aula['nombre']}."]
            if otro:
                frases.append(f"En este piso también está {otro['nombre']}.")
            fijos.append({"id": f"g{n}_profesor", "rol": "Profesor", "nombre": "Profesor", "u": pu, "v": rv,
                          "yaw": 90 if derecha else 270, "conducta": "quieto", "frases": frases})
        for f in fijos:
            if not libre(planta, f["u"], f["v"]):
                fallos.append(f"piso {n}: {f['id']} cae en {choca(planta, f['u'], f['v'])}")
            f.update(uv(f["u"], f["v"]))
            f["piso"] = idx
            c["npcs"].append({k: f[k] for k in ("id", "rol", "nombre", "piso", "u", "v", "yaw", "conducta", "frases")})

    total = m["estudiantes"] + m["visitantes"] + 2 * len(parejas) + sum(1 for x in c["npcs"] if x["piso"] == idx)
    print(f"piso {n:>2}: {len(recorrido)} puntos de recorrido, {len(parejas)} parejas, {total} personas")

if fallos:
    raise SystemExit("\n".join(fallos))
c["multitudes"] = multitudes
json.dump(c, open(RUTA, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print(f"contenido_piso9.json: {len(c['npcs'])} NPC fijos, {len(multitudes)} multitudes además de la del 9.")
