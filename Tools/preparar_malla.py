#!/usr/bin/env python3
# ============================================================================
# preparar_malla.py — MovU
# ============================================================================
# Hace, sin Blender, el trabajo que uno abriría Blender para hacer:
#
#   1. Clasifica cada triángulo en PISO / MURO / TECHO según su normal.
#   2. Genera coordenadas UV por proyección de caja, medidas en metros reales
#      del mundo de Unity (no en unidades del OBJ), así que una baldosa de
#      1,2 m mide 1,2 m en el juego sin tener que tocar el tiling del material.
#   3. Escribe un OBJ con tres grupos y su .mtl, para que Unity lo importe con
#      tres ranuras de material independientes.
#
# El OBJ de Meshy viene "de pie": el relieve sale en +Z, así que en el espacio
# del archivo el arriba es +Z (en Unity se acuesta con la rotación de -90° en X
# que aplica MeshiWalkableSetup). Todo el script trabaja en esa convención.
#
# Uso:
#   python3 Tools/preparar_malla.py
#   python3 Tools/preparar_malla.py --tile 0.9 --escala 28
# ============================================================================

import argparse
import os
import sys
from collections import Counter

import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.dirname(AQUI)

ENTRADA_POR_DEFECTO = os.path.join(
    RAIZ, "Assets", "MovU", "Models",
    "Meshy_AI_Plano_de_evacuación_0831203851_generate.obj")
SALIDA_POR_DEFECTO = os.path.join(
    RAIZ, "Assets", "MovU", "Models", "PlanoMovU_Estilizado.obj")

GRUPOS = ["Piso", "Muro", "Techo"]


def leer_obj(ruta):
    """Devuelve (vertices Nx3, caras Mx3) leyendo sólo 'v' y 'f'."""
    vertices = []
    caras = []

    with open(ruta, "r", encoding="utf-8", errors="replace") as f:
        for linea in f:
            if linea.startswith("v "):
                partes = linea.split()
                vertices.append((float(partes[1]), float(partes[2]), float(partes[3])))
            elif linea.startswith("f "):
                partes = linea.split()[1:]
                # Un índice de cara puede venir como "12", "12/3" o "12//4".
                idx = [int(p.split("/")[0]) for p in partes]
                # Índices negativos = relativos al final de la lista.
                idx = [i - 1 if i > 0 else len(vertices) + i for i in idx]
                # Triangular en abanico por si viniera algún polígono.
                for k in range(1, len(idx) - 1):
                    caras.append((idx[0], idx[k], idx[k + 1]))

    return np.asarray(vertices, dtype=np.float64), np.asarray(caras, dtype=np.int64)


def normales_de_cara(vertices, caras):
    a = vertices[caras[:, 0]]
    b = vertices[caras[:, 1]]
    c = vertices[caras[:, 2]]
    n = np.cross(b - a, c - a)
    largo = np.linalg.norm(n, axis=1, keepdims=True)
    largo[largo == 0.0] = 1.0
    return n / largo


def clasificar(normales, umbral):
    """0 = Piso, 1 = Muro, 2 = Techo. El 'arriba' del OBJ es +Z."""
    arriba = normales[:, 2]
    grupo = np.full(len(normales), 1, dtype=np.int8)   # muro por defecto
    grupo[arriba >= umbral] = 0
    grupo[arriba <= -umbral] = 2
    return grupo


def uvs_por_proyeccion(vertices, caras, normales, grupo, metros_por_uv):
    """
    Proyección de caja. Para piso y techo se proyecta sobre el plano XY del OBJ
    (el plano horizontal del mundo); para los muros, sobre el eje horizontal en
    el que la superficie realmente avanza, más la altura.

    Devuelve una lista de UV por esquina de cara (3 por triángulo).
    """
    uv = np.zeros((len(caras), 3, 2), dtype=np.float64)

    horizontal = grupo != 1
    for esquina in range(3):
        p = vertices[caras[:, esquina]]

        # Piso y techo: (x, y) del OBJ.
        uv[horizontal, esquina, 0] = p[horizontal, 0] / metros_por_uv
        uv[horizontal, esquina, 1] = p[horizontal, 1] / metros_por_uv

        # Muros: si la normal apunta sobre todo en X, la pared avanza en Y.
        muro = ~horizontal
        avanza_en_y = np.abs(normales[muro, 0]) > np.abs(normales[muro, 1])
        a_lo_largo = np.where(avanza_en_y, p[muro, 1], p[muro, 0])
        uv[muro, esquina, 0] = a_lo_largo / metros_por_uv
        uv[muro, esquina, 1] = p[muro, 2] / metros_por_uv

    return uv


def escribir_mtl(ruta_mtl):
    colores = {
        "Piso":  (0.64, 0.66, 0.69),
        "Muro":  (0.87, 0.87, 0.84),
        "Techo": (0.93, 0.93, 0.95),
    }
    with open(ruta_mtl, "w", encoding="utf-8") as f:
        f.write("# MovU - materiales base del entorno.\n")
        f.write("# Colores planos a proposito: en Unity cada ranura recibe su\n")
        f.write("# propio material (procedural o con texturas PBR).\n\n")
        for nombre in GRUPOS:
            r, g, b = colores[nombre]
            f.write(f"newmtl {nombre}\n")
            f.write(f"Kd {r:.4f} {g:.4f} {b:.4f}\n")
            f.write("Ks 0.0000 0.0000 0.0000\n")
            f.write("Ns 10.0\n")
            f.write("d 1.0\n")
            f.write("illum 2\n\n")


def escribir_obj(ruta, vertices, caras, uv, grupo, cabecera):
    nombre_mtl = os.path.splitext(os.path.basename(ruta))[0] + ".mtl"
    escribir_mtl(os.path.join(os.path.dirname(ruta), nombre_mtl))

    with open(ruta, "w", encoding="utf-8") as f:
        for linea in cabecera:
            f.write(f"# {linea}\n")
        f.write(f"mtllib {nombre_mtl}\n")
        f.write("o PlanoMovU\n")

        for v in vertices:
            f.write(f"v {v[0]:.6f} {v[1]:.6f} {v[2]:.6f}\n")

        # Las UV se escriben agrupadas por material para que los índices de
        # cara queden contiguos y el archivo sea legible.
        contador_uv = 0
        bloques = []
        for g in range(3):
            sel = np.flatnonzero(grupo == g)
            bloques.append((g, sel, contador_uv))
            contador_uv += len(sel) * 3

        for _, sel, _ in bloques:
            for cara_i in sel:
                for esquina in range(3):
                    u, v = uv[cara_i, esquina]
                    f.write(f"vt {u:.6f} {v:.6f}\n")

        for g, sel, base in bloques:
            if len(sel) == 0:
                continue
            f.write(f"g {GRUPOS[g]}\n")
            f.write(f"usemtl {GRUPOS[g]}\n")
            for j, cara_i in enumerate(sel):
                a, b, c = caras[cara_i] + 1
                ua, ub, uc = base + j * 3 + 1, base + j * 3 + 2, base + j * 3 + 3
                f.write(f"f {a}/{ua} {b}/{ub} {c}/{uc}\n")


def main():
    ap = argparse.ArgumentParser(description="Separa el plano en piso/muro/techo y le genera UVs.")
    ap.add_argument("--entrada", default=ENTRADA_POR_DEFECTO)
    ap.add_argument("--salida",  default=SALIDA_POR_DEFECTO)
    ap.add_argument("--escala",  type=float, default=28.0,
                    help="Escala que aplica Unity al modelo (unidades OBJ -> metros).")
    ap.add_argument("--tile",    type=float, default=1.2,
                    help="Metros de mundo que abarca una repeticion de textura.")
    ap.add_argument("--umbral",  type=float, default=0.80,
                    help="Coseno minimo con el eje vertical para contar como piso/techo.")
    args = ap.parse_args()

    if not os.path.isfile(args.entrada):
        print(f"ERROR: no existe {args.entrada}", file=sys.stderr)
        return 1

    print(f"Leyendo {os.path.basename(args.entrada)} ...")
    vertices, caras = leer_obj(args.entrada)
    print(f"  {len(vertices):,} vertices / {len(caras):,} triangulos")

    normales = normales_de_cara(vertices, caras)
    grupo = clasificar(normales, args.umbral)

    cuenta = Counter(grupo.tolist())
    total = len(caras)
    for i, nombre in enumerate(GRUPOS):
        n = cuenta.get(i, 0)
        print(f"  {nombre:<6} {n:>8,} tri  ({100.0 * n / total:5.1f} %)")

    # 1 unidad OBJ = 'escala' metros, y queremos 'tile' metros por repeticion.
    metros_por_uv = args.tile / args.escala
    uv = uvs_por_proyeccion(vertices, caras, normales, grupo, metros_por_uv)

    dims = (vertices.max(axis=0) - vertices.min(axis=0)) * args.escala
    cabecera = [
        "MovU - plano separado en piso/muro/techo con UVs por proyeccion de caja.",
        f"Generado por Tools/preparar_malla.py desde {os.path.basename(args.entrada)}",
        f"Escala asumida en Unity: {args.escala:g}x  ->  planta de "
        f"{dims[0]:.1f} x {dims[1]:.1f} m, alto {dims[2]:.1f} m",
        f"Una repeticion de textura = {args.tile:g} m de mundo.",
        "El arriba del archivo es +Z; en Unity se acuesta con la rotacion de -90 en X.",
    ]

    print(f"Escribiendo {os.path.basename(args.salida)} ...")
    escribir_obj(args.salida, vertices, caras, uv, grupo, cabecera)

    mb = os.path.getsize(args.salida) / (1024 * 1024)
    print(f"Listo. {mb:.1f} MB con 3 ranuras de material (Piso / Muro / Techo).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
