#!/usr/bin/env python3
"""
piso_generico_planta.py — Planta de los pisos genéricos (4 a 8 y 10 a 12).

Todavía no hay planos de esos pisos, pero hacen falta para que la escalera de emergencia
conecte el edificio de verdad (del 4 al 13). Se arman con el núcleo del piso 9 (mismo
edificio): fachada, pasillo, lobby, ascensores, escaleras, baños y cuartos técnicos, con
los mismos acabados. Lo que cambia:

  - Todas las puertas quedan CERRADAS (hoja de madera, letrero y colisión): los salones
    y oficinas de esos pisos todavía no tienen nada. La única puerta abierta es la de la
    escalera de emergencia, que es la que une los pisos.
  - Sin mobiliario ni luces dentro de los salones (se ven vacíos a través del vidrio).
  - El número del granito del lobby es el del piso ("{n}", lo pone el constructor).

Lee Assets/MovU/Models/Piso9/planta_piso9.json y las puertas de contenido_piso9.json, así
que hay que correrlo después de Tools/piso9_planta.py.
Genera Assets/MovU/Models/PisoGenerico/planta_generico.json.

Uso:   python3 Tools/piso_generico_planta.py          (desde la raíz del repositorio)
"""
import json, os

ANCHO, LARGO = 39.3, 106.6
U_MID = (18.7 + 23.6) / 2
CIELO_SALON = 3.3
PUERTAS_ABIERTAS = {"p_escalera", "p_escalera_principal"}   # las dos escaleras: las que conectan los pisos

raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
with open(os.path.join(raiz, "Assets", "MovU", "Models", "Piso9", "planta_piso9.json"), encoding="utf-8") as f:
    p9 = json.load(f)
with open(os.path.join(raiz, "Assets", "MovU", "Resources", "MovU", "contenido_piso9.json"), encoding="utf-8") as f:
    contenido = json.load(f)

planta = {k: v for k, v in p9.items() if k not in ("marco", "muebles", "rotulos", "lamparas", "luces")}
planta["fuente"] = "Núcleo del piso 9 (planta_piso9.json) con los salones cerrados"
planta["muebles"] = []
planta["rotulos"] = [dict(r, texto="{n}") for r in p9.get("rotulos", []) if r.get("texto") == "09"]
# Luces: las del pasillo y el lobby; las de adentro de los salones (más bajas que su cielo raso) no.
planta["luces"] = [l for l in p9.get("luces", []) if l.get("y", 5.5) > CIELO_SALON + 0.2]
planta["lamparas"] = [l for l in p9.get("lamparas", []) if l.get("y", 4.8) > CIELO_SALON]

cerradas = []
for d in contenido["puertas"]:
    if d["piso"] != 0 or d["id"] in PUERTAS_ABIERTAS:
        continue
    u, v = d["u"] * ANCHO, d["v"] * LARGO
    if round(d["yaw"]) % 180 == 0:            # muro a lo largo de u (la P9-03, al fondo): el pasillo queda en +v
        eje, mira = "u", "+v"
    else:                                     # muro a lo largo de v: el pasillo queda hacia el centro
        eje, mira = "v", ("+u" if u < U_MID else "-u")
    cerradas.append({"v": round(v, 3), "u": round(u, 3), "ancho": d["ancho"], "eje": eje, "mira": mira,
                     "texto": "CERRADO"})
planta["puertasCerradas"] = cerradas

destino = os.path.join(raiz, "Assets", "MovU", "Models", "PisoGenerico")
os.makedirs(destino, exist_ok=True)
with open(os.path.join(destino, "planta_generico.json"), "w", encoding="utf-8") as f:
    json.dump(planta, f, ensure_ascii=False, indent=1)
print(f"planta_generico.json: {len(planta['muros'])} muros, {len(cerradas)} puertas cerradas, "
      f"{len(planta['lamparas'])} lámparas.")
