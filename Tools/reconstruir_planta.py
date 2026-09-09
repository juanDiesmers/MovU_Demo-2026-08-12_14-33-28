#!/usr/bin/env python3
# ============================================================================
# reconstruir_planta.py — MovU
# ============================================================================
# El OBJ de Meshy tiene una planta CORRECTA pero una extrusion ROTA. Medido
# sobre el modelo: el 84,6 % del area de muro esta a menos de 5 grados de un
# plano cardinal (o sea, las paredes si son planas y estan a escuadra), pero la
# altura de la corona va de 1,2 a 3,4 m con una desviacion estandar de 0,52 m.
# Ese borde superior irregular es el defecto que se ve en el juego: la silueta
# dentada contra el cielo y los muros "mordidos".
#
# Este script no intenta arreglar la malla: la USA COMO PLANO y construye la
# geometria de nuevo.
#
#   1. Rasteriza el modelo desde arriba en un mapa de alturas.
#   2. Marca como muro solo las celdas que alcanzaron la altura real de pared
#      (~2,9 m). Todo lo que se quedo a medias — simbolos del plano, el dibujo
#      de la escalera, la zona del extremo que quedo rellena en bloque — pasa a
#      ser piso libre en vez de un mamotreto a media altura.
#   3. Limpia el ruido de sal y pimienta y cierra los huecos pequenos sin tapar
#      los vanos de las puertas.
#   4. Quita los "bloques": lo que sea mas grueso que un muro no es un muro.
#   5. Agrupa las celdas en rectangulos (greedy meshing) y extruye cada uno a
#      una altura UNIFORME.
#   6. Emite un OBJ con normales correctas, UVs por proyeccion de caja en
#      metros de mundo, y tres grupos: Piso / Muro / Corona.
#
# El resultado son muros perfectamente planos, a escuadra, todos a la misma
# altura, con esquinas nitidas, y con muchisimos menos triangulos. Como las
# normales salen bien, en Unity se puede volver a Back face culling.
#
# La orientacion se conserva (arriba = +Z), asi que es un reemplazo directo:
# MeshiWalkableSetup le sigue aplicando -90 grados en X y la escala de 28x.
#
# Uso:
#   python3 Tools/reconstruir_planta.py
#   python3 Tools/reconstruir_planta.py --altura-muro 3.2 --celda 0.04
# ============================================================================

import argparse
import io
import os
import struct
import zlib

import numpy as np

AQUI = os.path.dirname(os.path.abspath(__file__))
RAIZ = os.path.dirname(AQUI)

ENTRADA = os.path.join(RAIZ, "Assets", "MovU", "Models",
                       "Meshy_AI_Plano_de_evacuación_0831203851_generate.obj")
SALIDA = os.path.join(RAIZ, "Assets", "MovU", "Models", "PlantaMovU_Reconstruida.obj")

GRUPOS = ["Piso", "Muro", "Corona"]


# ---------------------------------------------------------------- lectura ---
def leer_obj(ruta):
    V, F = [], []
    for l in io.open(ruta, encoding="utf-8", errors="replace"):
        if l.startswith("v "):
            p = l.split()
            V.append((float(p[1]), float(p[2]), float(p[3])))
        elif l.startswith("f "):
            i = [int(x.split("/")[0]) - 1 for x in l.split()[1:]]
            for k in range(1, len(i) - 1):
                F.append((i[0], i[k], i[k + 1]))
    return np.asarray(V), np.asarray(F)


def nivel_de_piso(V, F):
    """Moda de las superficies horizontales, ponderada por area."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    n = np.cross(b - a, c - a)
    ln = np.linalg.norm(n, axis=1)
    area = ln * 0.5
    n = n / np.maximum(ln[:, None], 1e-12)
    hor = n[:, 2] > 0.85
    z = (a[hor][:, 2] + b[hor][:, 2] + c[hor][:, 2]) / 3.0
    h, e = np.histogram(z, bins=200, weights=area[hor])
    i = int(h.argmax())
    return 0.5 * (e[i] + e[i + 1])


def rasterizar(V, F, paso, muestras=40, semilla=7):
    """Mapa de alturas: para cada celda, la z maxima de la malla."""
    a, b, c = V[F[:, 0]], V[F[:, 1]], V[F[:, 2]]
    xmin, ymin = V[:, 0].min(), V[:, 1].min()
    nx = int((V[:, 0].max() - xmin) / paso) + 1
    ny = int((V[:, 1].max() - ymin) / paso) + 1

    alt = np.full((nx, ny), -1e9, np.float32)
    rng = np.random.default_rng(semilla)
    # Muestreo baricentrico uniforme sobre cada triangulo.
    r1 = np.sqrt(rng.random((len(F), muestras, 1)))
    r2 = rng.random((len(F), muestras, 1))
    P = ((1 - r1) * a[:, None, :] + r1 * (1 - r2) * b[:, None, :]
         + r1 * r2 * c[:, None, :]).reshape(-1, 3)

    ix = np.clip(((P[:, 0] - xmin) / paso).astype(np.int32), 0, nx - 1)
    iy = np.clip(((P[:, 1] - ymin) / paso).astype(np.int32), 0, ny - 1)
    np.maximum.at(alt, (ix, iy), P[:, 2])
    return alt, xmin, ymin, nx, ny


# ------------------------------------------------------------- morfologia ---
def _morf_1d(m, k, eje, erosionar):
    r = k // 2
    if r == 0:
        return m
    pad = [(0, 0), (0, 0)]
    pad[eje] = (r, r)
    # Al erosionar se rellena con True para no comerse el borde;
    # al dilatar, con False para no inventar geometria fuera del mapa.
    p = np.pad(m, pad, constant_values=bool(erosionar))
    n = m.shape[eje]
    out = None
    for d in range(k):
        sl = [slice(None), slice(None)]
        sl[eje] = slice(d, d + n)
        w = p[tuple(sl)]
        out = w if out is None else (out & w if erosionar else out | w)
    return out


def _morf(m, k, erosionar):
    return _morf_1d(_morf_1d(m, k, 0, erosionar), k, 1, erosionar)


def apertura_direccional(m, k):
    """Conserva solo las celdas que forman parte de un tramo LARGO, en x o en y.

    Es la herramienta clave: un muro es largo en una direccion y delgado en la
    otra, asi que esto conserva los muros enteros y borra de un golpe todo el
    fleco de una celda que deja la rasterizacion en los bordes. Sin esto, el
    68 % de los rectangulos salia con un lado de 5 cm — muros de papel y
    rendijas de un centimetro por todas partes.
    """
    horiz = _morf_1d(_morf_1d(m, k, 0, True), k, 0, False)
    vert = _morf_1d(_morf_1d(m, k, 1, True), k, 1, False)
    return horiz | vert


def normalizar_grosor(m, eje, t):
    """Reemplaza cada tramo transversal por uno de grosor FIJO, centrado.

    Es lo que convierte un plano rasterizado en arquitectura. Un muro cuyo
    grosor oscila entre 2 y 3 celdas obliga al greedy meshing a partirlo en un
    nucleo grueso mas un fleco de una celda, y ese fleco es lo que en el juego
    se ve como muros de papel y rendijas. Con el grosor constante, cada muro
    sale como un solo rectangulo largo.
    """
    out = np.zeros_like(m)
    mm = m if eje == 0 else m.T
    oo = out if eje == 0 else out.T
    n = mm.shape[1]
    for i in range(mm.shape[0]):
        fila = mm[i]
        if not fila.any():
            continue
        d = np.diff(np.concatenate(([0], fila.view(np.int8), [0])))
        for ini, fin in zip(np.flatnonzero(d == 1), np.flatnonzero(d == -1)):
            centro = (ini + fin) // 2
            hi = min(n, max(t, centro - t // 2 + t))
            lo = max(0, hi - t)
            oo[i, lo:hi] = True
    return out


def submuestrear(m, f):
    """Baja la resolucion por voto de mayoria: cuantiza el borde de los muros."""
    if f <= 1:
        return m
    nx, ny = m.shape
    nx2, ny2 = nx // f, ny // f
    a = m[:nx2 * f, :ny2 * f].reshape(nx2, f, ny2, f)
    return a.sum(axis=(1, 3)) * 2 >= f * f


def apertura(m, k):
    """Borra lo mas fino que k: quita el ruido de sal."""
    return _morf(_morf(m, k, True), k, False)


def cierre(m, k):
    """Rellena huecos mas chicos que k: quita el ruido de pimienta."""
    return _morf(_morf(m, k, False), k, True)


def quitar_islas(mask, min_celdas):
    """Borra las componentes conexas mas pequenas que min_celdas.

    Son las motas que quedan sueltas dentro de un salon: sin esto, cada mota
    se convierte en una columna de 3 m plantada en mitad del piso.
    """
    nx, ny = mask.shape
    visto = np.zeros_like(mask)
    fuera = np.zeros_like(mask)
    xs, ys = np.nonzero(mask)
    for x0, y0 in zip(xs.tolist(), ys.tolist()):
        if visto[x0, y0]:
            continue
        pila = [(x0, y0)]
        visto[x0, y0] = True
        comp = []
        while pila:
            x, y = pila.pop()
            comp.append((x, y))
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                u, v = x + dx, y + dy
                if 0 <= u < nx and 0 <= v < ny and mask[u, v] and not visto[u, v]:
                    visto[u, v] = True
                    pila.append((u, v))
        if len(comp) < min_celdas:
            for x, y in comp:
                fuera[x, y] = True
    return mask & ~fuera


# --------------------------------------------------------- greedy meshing ---
def rectangulos(mask):
    """Agrupa las celdas encendidas en el minimo razonable de rectangulos."""
    m = mask.copy()
    nx, ny = m.shape
    out = []
    for x in range(nx):
        y = 0
        fila = m[x]
        while y < ny:
            if not fila[y]:
                y += 1
                continue
            y2 = y
            while y2 < ny and fila[y2]:
                y2 += 1
            x2 = x + 1
            while x2 < nx and m[x2, y:y2].all():
                x2 += 1
            out.append((x, y, x2, y2))
            m[x:x2, y:y2] = False
            y = y2
    return out


# ------------------------------------------------------------- escritura ----
class Malla:
    """Acumula caras con vertices propios: sombreado plano y normales exactas."""

    def __init__(self):
        self.v, self.vt, self.vn, self.caras = [], [], [], {g: [] for g in GRUPOS}

    def quad(self, grupo, p, uv, normal):
        base = len(self.v) + 1
        self.v.extend(p)
        self.vt.extend(uv)
        self.vn.append(normal)
        ni = len(self.vn)
        self.caras[grupo].append((base, base + 1, base + 2, base + 3, ni))

    def escribir(self, ruta, cabecera):
        mtl = os.path.splitext(os.path.basename(ruta))[0] + ".mtl"
        colores = {"Piso": (0.64, 0.66, 0.69),
                   "Muro": (0.87, 0.87, 0.84),
                   "Corona": (0.55, 0.57, 0.61)}
        with open(os.path.join(os.path.dirname(ruta), mtl), "w", encoding="utf-8") as f:
            for g in GRUPOS:
                r, gg, b = colores[g]
                f.write(f"newmtl {g}\nKd {r:.4f} {gg:.4f} {b:.4f}\n"
                        f"Ks 0 0 0\nNs 10.0\nd 1.0\nillum 2\n\n")

        with open(ruta, "w", encoding="utf-8") as f:
            for l in cabecera:
                f.write(f"# {l}\n")
            f.write(f"mtllib {mtl}\no PlantaMovU\n")
            for x, y, z in self.v:
                f.write(f"v {x:.6f} {y:.6f} {z:.6f}\n")
            for u, w in self.vt:
                f.write(f"vt {u:.6f} {w:.6f}\n")
            for x, y, z in self.vn:
                f.write(f"vn {x:.4f} {y:.4f} {z:.4f}\n")
            for g in GRUPOS:
                if not self.caras[g]:
                    continue
                f.write(f"g {g}\nusemtl {g}\n")
                for a, b, c, d, n in self.caras[g]:
                    f.write(f"f {a}/{a}/{n} {b}/{b}/{n} {c}/{c}/{n}\n")
                    f.write(f"f {a}/{a}/{n} {c}/{c}/{n} {d}/{d}/{n}\n")

    @property
    def triangulos(self):
        return 2 * sum(len(v) for v in self.caras.values())


def preview_png(ruta, ocupado, muro, nx, ny, bruto=None):
    img = np.zeros((ny, nx, 3), np.uint8) + 18
    img[ocupado.T] = (238, 238, 240)
    if bruto is not None:
        # en rojo, lo que la malla original tenia levantado y aqui se descarto
        img[(bruto & ~muro).T] = (215, 95, 70)
    img[muro.T] = (28, 32, 40)
    raw = b"".join(b"\x00" + img[r].tobytes() for r in range(ny))

    def ch(t, d):
        return (struct.pack(">I", len(d)) + t + d
                + struct.pack(">I", zlib.crc32(t + d) & 0xffffffff))

    open(ruta, "wb").write(
        b"\x89PNG\r\n\x1a\n"
        + ch(b"IHDR", struct.pack(">IIBBBBB", nx, ny, 8, 2, 0, 0, 0))
        + ch(b"IDAT", zlib.compress(raw, 6)) + ch(b"IEND", b""))


# ------------------------------------------------------------------ main ----
def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--entrada", default=ENTRADA)
    ap.add_argument("--salida", default=SALIDA)
    ap.add_argument("--escala", type=float, default=28.0)
    ap.add_argument("--celda", type=float, default=0.05,
                    help="Metros de mundo por celda de la rejilla.")
    ap.add_argument("--umbral-muro", type=float, default=1.60,
                    help="Altura minima (m) que debio alcanzar la extrusion para "
                         "contar como muro real.")
    ap.add_argument("--altura-muro", type=float, default=3.00,
                    help="Altura uniforme a la que se levantan todos los muros.")
    ap.add_argument("--grosor-bloque", type=float, default=1.00,
                    help="Lo mas grueso que esto no es un muro: se descarta.")
    ap.add_argument("--cierre", type=float, default=0.35,
                    help="Huecos menores a esto se rellenan (sin tapar puertas).")
    ap.add_argument("--salida-celda", type=float, default=0.10,
                    help="Tamano de celda de la GEOMETRIA final (la rasterizacion "
                         "sigue siendo mas fina). Cuantiza el borde de los muros.")
    ap.add_argument("--grosor-muro", type=float, default=0.30,
                    help="Grosor uniforme al que se normalizan todos los muros.")
    ap.add_argument("--largo-min", type=float, default=0.60,
                    help="Un tramo de muro mas corto que esto no es un muro.")
    ap.add_argument("--min-isla", type=float, default=0.15,
                    help="Area minima (m2) de una isla de muro para conservarla.")
    ap.add_argument("--grosor-contorno", type=float, default=0.25,
                    help="Grosor del muro con que se reemplaza un bloque relleno.")
    ap.add_argument("--cerrar-huella", type=float, default=0.60,
                    help="Cierra los agujeros de muestreo de la huella del piso.")
    ap.add_argument("--tile", type=float, default=1.20,
                    help="Metros de mundo por repeticion de textura.")
    args = ap.parse_args()

    E, C = args.escala, args.celda
    paso = C / E

    print(f"Leyendo {os.path.basename(args.entrada)} ...")
    V, F = leer_obj(args.entrada)
    print(f"  {len(V):,} vertices / {len(F):,} triangulos")

    zf = nivel_de_piso(V, F)
    alt, xmin, ymin, nx, ny = rasterizar(V, F, paso)
    ocupado = alt > -1e8
    altura_m = np.where(ocupado, (alt - zf) * E, -99.0)

    print(f"Rejilla {nx} x {ny} a {C*100:.0f} cm  ->  {nx*C:.1f} x {ny*C:.1f} m")

    bruto = ocupado & (altura_m > args.umbral_muro)
    print(f"  celdas por encima de {args.umbral_muro:.2f} m: {bruto.sum():,}")

    k_cie = max(3, int(round(args.cierre / C)) | 1)
    k_blo = max(3, int(round(args.grosor_bloque / C)) | 1)
    k_hue = max(3, int(round(args.cerrar_huella / C)) | 1)
    k_con = max(3, int(round(2.0 * args.grosor_contorno / C)) | 1)
    k_lar = max(3, int(round(args.largo_min / C)) | 1)

    muro = apertura(bruto, 3)          # fuera el ruido de sal
    muro = cierre(muro, k_cie)         # soldar los cortes de la extrusion

    # Lo que sea mas grueso que un muro es un bloque: una zona donde la
    # extrusion relleno un salon entero en vez de dibujar sus paredes.
    # No se borra — se reemplaza por SU CONTORNO. Asi el salon queda entrable
    # y al mismo tiempo sigue cerrado, que es lo que un plano quiere decir.
    bloques = apertura(muro, k_blo)
    if bloques.any():
        contorno = bloques & ~_morf(bloques, k_con, True)
        print(f"  {bloques.sum():,} celdas de bloque relleno "
              f"(> {args.grosor_bloque:.2f} m de grosor) reducidas a "
              f"{contorno.sum():,} celdas de contorno")
        muro = (muro & ~bloques) | contorno
        muro = cierre(muro, 3)         # soldar el empalme contorno/muro

    # Solo sobrevive lo que forma un tramo de muro de verdad.
    antes = muro.sum()
    muro = apertura_direccional(muro, k_lar)
    print(f"  {antes - muro.sum():,} celdas descartadas por no formar un tramo "
          f"de al menos {args.largo_min:.2f} m")
    muro = cierre(muro, 3)

    # Cuantizar el borde: a 5 cm el contorno de un muro queda dentado por el
    # muestreo, y cada diente se convierte en un rectangulo aparte.
    f = max(1, int(round(args.salida_celda / C)))
    muro = submuestrear(muro, f)
    huella = submuestrear(apertura(cierre(ocupado, k_hue), 3), f)
    Cs = C * f
    paso_s = paso * f
    nxs, nys = muro.shape
    print(f"  rejilla de salida {nxs} x {nys} a {Cs*100:.0f} cm")

    muro = cierre(muro, 3)
    muro = apertura(muro, 3)

    # Grosor constante. Se separan los muros largos en x de los largos en y,
    # se normaliza cada familia en su direccion transversal, y se unen: las
    # esquinas salen bien porque quedan cubiertas por las dos.
    t = max(1, int(round(args.grosor_muro / Cs)))

    # Ojo: este nucleo solo sirve para SABER hacia donde corre cada muro, no
    # para filtrar. El filtro de largo ya se aplico sobre la rejilla fina; si se
    # vuelve a aplicar aqui se comen los tramos cortos entre dos puertas y los
    # muros salen punteados.
    k_ori = max(3, int(round(0.40 / Cs)) | 1)
    largo_x = _morf_1d(_morf_1d(muro, k_ori, 0, True), k_ori, 0, False)
    largo_y = _morf_1d(_morf_1d(muro, k_ori, 1, True), k_ori, 1, False)
    resto = muro & ~(largo_x | largo_y)     # esquinas y jambas sueltas

    normalizado = (normalizar_grosor(largo_x, 0, t)
                   | normalizar_grosor(largo_y, 1, t)
                   | resto)
    cobertura = 100.0 * (normalizado & _morf(muro, 5, False)).sum() / max(normalizado.sum(), 1)
    perdido = 100.0 * (muro & ~_morf(normalizado, 5, False)).sum() / max(muro.sum(), 1)
    muro = cierre(normalizado, 3)
    print(f"  grosor de muro normalizado a {t * Cs:.2f} m "
          f"(cobertura {cobertura:.1f}%, muro perdido {perdido:.1f}%)")

    muro = quitar_islas(muro, max(4, int(round(args.min_isla / (Cs * Cs)))))
    piso = huella & ~muro
    print(f"  muro final {muro.sum():,} celdas "
          f"({100*muro.sum()/max(huella.sum(),1):.1f}% de la planta)")

    r_muro = rectangulos(muro)
    r_piso = rectangulos(piso)
    print(f"  rectangulos: {len(r_muro):,} de muro, {len(r_piso):,} de piso")

    mpu = args.tile / E
    z0 = zf
    z1 = zf + args.altura_muro / E
    m = Malla()

    def X(i): return xmin + i * paso_s
    def Y(j): return ymin + j * paso_s

    for (x0, y0, x1, y1) in r_piso:
        A, B, Cc, D = X(x0), X(x1), Y(y0), Y(y1)
        m.quad("Piso",
               [(A, Cc, z0), (B, Cc, z0), (B, D, z0), (A, D, z0)],
               [(A/mpu, Cc/mpu), (B/mpu, Cc/mpu), (B/mpu, D/mpu), (A/mpu, D/mpu)],
               (0, 0, 1))

    for (x0, y0, x1, y1) in r_muro:
        A, B, Cc, D = X(x0), X(x1), Y(y0), Y(y1)

        # Corona (tapa superior)
        m.quad("Corona",
               [(A, Cc, z1), (B, Cc, z1), (B, D, z1), (A, D, z1)],
               [(A/mpu, Cc/mpu), (B/mpu, Cc/mpu), (B/mpu, D/mpu), (A/mpu, D/mpu)],
               (0, 0, 1))

        # Cuatro caras laterales, con el giro correcto para que la normal
        # apunte hacia afuera de la caja.
        m.quad("Muro", [(A, Cc, z0), (B, Cc, z0), (B, Cc, z1), (A, Cc, z1)],
               [(A/mpu, z0/mpu), (B/mpu, z0/mpu), (B/mpu, z1/mpu), (A/mpu, z1/mpu)],
               (0, -1, 0))
        m.quad("Muro", [(B, D, z0), (A, D, z0), (A, D, z1), (B, D, z1)],
               [(B/mpu, z0/mpu), (A/mpu, z0/mpu), (A/mpu, z1/mpu), (B/mpu, z1/mpu)],
               (0, 1, 0))
        m.quad("Muro", [(B, Cc, z0), (B, D, z0), (B, D, z1), (B, Cc, z1)],
               [(Cc/mpu, z0/mpu), (D/mpu, z0/mpu), (D/mpu, z1/mpu), (Cc/mpu, z1/mpu)],
               (1, 0, 0))
        m.quad("Muro", [(A, D, z0), (A, Cc, z0), (A, Cc, z1), (A, D, z1)],
               [(D/mpu, z0/mpu), (Cc/mpu, z0/mpu), (Cc/mpu, z1/mpu), (D/mpu, z1/mpu)],
               (-1, 0, 0))

    cabecera = [
        "MovU - planta reconstruida a partir del plano de Meshy.",
        f"Generado por Tools/reconstruir_planta.py (rasterizado a {C*100:.0f} cm, "
        f"geometria a {Cs*100:.0f} cm).",
        f"Muros a altura uniforme de {args.altura_muro:.2f} m; solo se levantaron",
        f"las celdas cuya extrusion original supero {args.umbral_muro:.2f} m.",
        f"Escala asumida en Unity: {E:g}x -> planta de {nxs*Cs:.1f} x {nys*Cs:.1f} m.",
        f"Una repeticion de textura = {args.tile:g} m de mundo.",
        "Arriba = +Z, igual que el original: reemplazo directo.",
    ]
    m.escribir(args.salida, cabecera)
    preview_png(os.path.join(RAIZ, "planta_reconstruida.png"), huella, muro, nxs, nys)

    mb = os.path.getsize(args.salida) / (1024 * 1024)
    print(f"\nListo: {m.triangulos:,} triangulos  ({mb:.1f} MB)")
    print(f"  Piso   {2*len(m.caras['Piso']):>8,} tri")
    print(f"  Muro   {2*len(m.caras['Muro']):>8,} tri")
    print(f"  Corona {2*len(m.caras['Corona']):>8,} tri")
    print(f"  -> {args.salida}")


if __name__ == "__main__":
    raise SystemExit(main())
