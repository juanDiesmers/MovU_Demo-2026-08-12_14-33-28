#!/usr/bin/env python3
# ============================================================================
# tapar_corona.py — MovU
# ============================================================================
# El modelo de Meshy se conserva tal cual (es el que se decidio usar), pero su
# extrusion deja las coronas de los muros a alturas que van de 1,4 a 3,4 m.
# Eso no importaba con un solo piso al aire libre; con tres pisos apilados si:
# por cada muro que se quedo corto se ve el piso de arriba.
#
# Este script NO modifica la malla original. Genera una malla APARTE que sube
# cada muro desde donde se quedo hasta una altura uniforme. Se instancia como
# hermana del modelo, con el mismo transform, y el jugador nunca la distingue:
# a la altura de los ojos sigue viendo la geometria organica de Meshy, y el
# relleno solo existe por encima.
#
# De paso arregla la silueta dentada contra el cielo del ultimo piso.
#
# Como funciona: rasteriza el modelo desde arriba, y para cada banda de altura
# emite las celdas de muro que todavia no llegan a esa banda. Las mascaras son
# monotonas (crecen con la altura), asi que las cajas se apilan sin huecos.
#
# Uso:
#   python3 Tools/tapar_corona.py
#   python3 Tools/tapar_corona.py --altura-muro 3.49 --banda 0.25
# ============================================================================

import argparse
import os
import sys

import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.dirname(AQUI)
sys.path.insert(0, AQUI)

import reconstruir_planta as R   # noqa: E402  (comparte la rasterizacion)

ENTRADA = os.path.join(RAIZ, "Assets", "MovU", "Models",
                       "Meshy_AI_Plano_de_evacuación_0831203851_generate.obj")
SALIDA = os.path.join(RAIZ, "Assets", "MovU", "Models", "RellenoCorona.obj")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--entrada", default=ENTRADA)
    ap.add_argument("--salida", default=SALIDA)
    ap.add_argument("--escala", type=float, default=28.0,
                    help="Escala VERTICAL que aplica Unity al modelo.")
    ap.add_argument("--celda", type=float, default=0.05,
                    help="Metros por celda al rasterizar.")
    ap.add_argument("--salida-celda", type=float, default=0.10,
                    help="Metros por celda de la geometria del relleno.")
    ap.add_argument("--umbral-muro", type=float, default=0.60,
                    help="Altura minima (m) para considerar que una celda es muro.")
    ap.add_argument("--altura-muro", type=float, default=3.49,
                    help="Altura uniforme (m sobre el piso) hasta la que se sube "
                         "cada muro. Es la cara inferior de la losa del piso de "
                         "arriba, asi que define la separacion entre pisos.")
    ap.add_argument("--banda", type=float, default=0.50,
                    help="Alto de cada banda de relleno. Mas fino = mas fiel al "
                         "perfil real del muro, y mas triangulos.")
    ap.add_argument("--solape", type=float, default=0.06,
                    help="Cuanto baja el relleno por debajo de la corona real, "
                         "para que no quede una rendija en la union.")
    ap.add_argument("--sin-techo", action="store_true",
                    help="No generar la losa de techo (deja el piso a cielo abierto).")
    ap.add_argument("--tile", type=float, default=1.20)
    args = ap.parse_args()

    E, C = args.escala, args.celda
    paso = C / E

    print(f"Leyendo {os.path.basename(args.entrada)} ...")
    V, F = R.leer_obj(args.entrada)
    print(f"  {len(V):,} vertices / {len(F):,} triangulos")

    zf = R.nivel_de_piso(V, F)
    alt, xmin, ymin, nx, ny = R.rasterizar(V, F, paso)
    ocupado = alt > -1e8
    altura_m = np.where(ocupado, (alt - zf) * E, -99.0)

    muro = ocupado & (altura_m > args.umbral_muro)
    muro = R.apertura(muro, 3)
    muro = R.cierre(muro, max(3, int(round(0.20 / C)) | 1))

    # A la rejilla de salida. El relleno vive por encima de la linea de vision,
    # asi que no necesita el detalle fino de la rasterizacion.
    f = max(1, int(round(args.salida_celda / C)))
    muro_s = R.submuestrear(muro, f)
    # Altura de la corona por celda gruesa: la MAXIMA de las finas que la componen,
    # para no cortar por debajo de un muro que si llego arriba.
    nx2, ny2 = muro_s.shape
    h = np.where(muro, altura_m, -99.0)[:nx2 * f, :ny2 * f]
    corona = h.reshape(nx2, f, ny2, f).max(axis=(1, 3))

    Cs = C * f
    paso_s = paso * f
    mpu = args.tile / E
    techo = args.altura_muro

    faltan = muro_s & (corona < techo - 0.01)
    print(f"  rejilla {nx2} x {ny2} a {Cs*100:.0f} cm")
    print(f"  celdas de muro: {muro_s.sum():,}")
    print(f"  celdas que no llegan a {techo:.2f} m: {faltan.sum():,} "
          f"({100*faltan.sum()/max(muro_s.sum(),1):.1f} %)")
    if faltan.any():
        c = corona[faltan]
        print(f"  su corona actual: p5={np.percentile(c,5):.2f}  "
              f"mediana={np.percentile(c,50):.2f}  p95={np.percentile(c,95):.2f} m")

    # Una sola caja por celda: desde su corona (cuantizada hacia abajo) hasta
    # el techo. Cuantizar agrupa celdas vecinas en la misma altura de arranque,
    # que es lo que permite al greedy meshing juntarlas en pocas cajas grandes.
    # Apilar bandas daba el mismo resultado visual con 5 veces mas triangulos.
    base = max(args.umbral_muro, 0.0)
    niveles = np.arange(base, techo, args.banda)
    if len(niveles) == 0:
        niveles = np.array([base])
    nivel_de = np.clip(np.digitize(corona, niveles) - 1, 0, len(niveles) - 1)

    m = R.Malla()
    def X(i): return xmin + i * paso_s
    def Y(j): return ymin + j * paso_s
    def Z(metros): return zf + metros / E

    total_rects = 0
    b = Z(techo)
    for i, nivel in enumerate(niveles):
        capa = faltan & (nivel_de == i)
        if not capa.any():
            continue
        a = Z(float(nivel) - args.solape)
        rects = R.rectangulos(capa)
        total_rects += len(rects)
        for (x0, y0, x1, y1) in rects:
            A, B, Cc, D = X(x0), X(x1), Y(y0), Y(y1)
            m.quad("Corona",
                   [(A, Cc, b), (B, Cc, b), (B, D, b), (A, D, b)],
                   [(A/mpu, Cc/mpu), (B/mpu, Cc/mpu), (B/mpu, D/mpu), (A/mpu, D/mpu)],
                   (0, 0, 1))
            m.quad("Muro", [(A, Cc, a), (B, Cc, a), (B, Cc, b), (A, Cc, b)],
                   [(A/mpu, a/mpu), (B/mpu, a/mpu), (B/mpu, b/mpu), (A/mpu, b/mpu)],
                   (0, -1, 0))
            m.quad("Muro", [(B, D, a), (A, D, a), (A, D, b), (B, D, b)],
                   [(B/mpu, a/mpu), (A/mpu, a/mpu), (A/mpu, b/mpu), (B/mpu, b/mpu)],
                   (0, 1, 0))
            m.quad("Muro", [(B, Cc, a), (B, D, a), (B, D, b), (B, Cc, b)],
                   [(Cc/mpu, a/mpu), (D/mpu, a/mpu), (D/mpu, b/mpu), (Cc/mpu, b/mpu)],
                   (1, 0, 0))
            m.quad("Muro", [(A, D, a), (A, Cc, a), (A, Cc, b), (A, D, b)],
                   [(D/mpu, a/mpu), (Cc/mpu, a/mpu), (Cc/mpu, b/mpu), (D/mpu, b/mpu)],
                   (-1, 0, 0))

    # ---- Losa de techo ------------------------------------------------
    # Cada piso tiene que ser AUTONOMO. El techo de un piso no puede ser la losa
    # del piso de arriba, porque FloorManager apaga los pisos que no se estan
    # usando: sin esto, todos los pisos quedan a cielo abierto.
    if not args.sin_techo:
        huella = R.submuestrear(
            R.apertura(R.cierre(ocupado, max(3, int(round(0.60 / C)) | 1)), 3), f)
        rects_techo = R.rectangulos(huella)
        for (x0, y0, x1, y1) in rects_techo:
            A, B, Cc, D = X(x0), X(x1), Y(y0), Y(y1)
            # Una sola capa mirando hacia ABAJO: por encima nunca se ve, porque
            # ahi esta la losa del piso siguiente.
            m.quad("Corona",
                   [(A, D, b), (B, D, b), (B, Cc, b), (A, Cc, b)],
                   [(A/mpu, D/mpu), (B/mpu, D/mpu), (B/mpu, Cc/mpu), (A/mpu, Cc/mpu)],
                   (0, 0, -1))
        print(f"  losa de techo: {len(rects_techo):,} rectangulos a {techo:.2f} m")

    cabecera = [
        "MovU - relleno de corona para el modelo de Meshy.",
        "NO reemplaza al modelo: se instancia como hermana, con el mismo transform.",
        f"Sube cada muro hasta {techo:.2f} m sobre el piso, en bandas de "
        f"{args.banda*100:.0f} cm.",
        f"Generado por Tools/tapar_corona.py. Escala vertical asumida: {E:g}x.",
        "Arriba = +Z, igual que el modelo original.",
    ]
    m.escribir(args.salida, cabecera)

    mb = os.path.getsize(args.salida) / (1024 * 1024)
    print(f"\nListo: {m.triangulos:,} triangulos en {total_rects:,} cajas ({mb:.1f} MB)")
    print(f"  -> {args.salida}")
    print(f"\n  Separacion entre pisos que implica: {techo:.2f} m + espesor de losa.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
