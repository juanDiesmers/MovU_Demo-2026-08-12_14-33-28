#!/usr/bin/env python3
# ============================================================================
# generar_sonidos.py — MovU
# ============================================================================
# Genera por síntesis los efectos de sonido del juego y los deja en
#   Assets/MovU/Resources/MovU/Audio/*.wav
#
# Por qué síntesis y no grabaciones: no hay que citar ni licenciar nada, los
# archivos pesan poco (mono, 16 bits) y el resultado es reproducible. Son
# sonidos "de maqueta": si el equipo consigue grabaciones mejores (por ejemplo
# CC0 de freesound.org o kenney.nl), basta con reemplazar el .wav conservando el
# nombre; el código (AudioManager.cs) no cambia.
#
# Uso:
#   python3 Tools/generar_sonidos.py            # todos
#   python3 Tools/generar_sonidos.py --solo paso_1 ascensor
#
# Requiere numpy.
# ============================================================================

import argparse
import os
import wave

import numpy as np

FS = 44100
AQUI = os.path.dirname(os.path.abspath(__file__))
SALIDA = os.path.join(os.path.dirname(AQUI), "Assets", "MovU", "Resources", "MovU", "Audio")


# ---------------------------------------------------------------------------
# Utilidades
# ---------------------------------------------------------------------------
def tiempo(segundos):
    return np.arange(int(FS * segundos)) / FS


def ruido(n, rng):
    return rng.standard_normal(n)


def filtrar(x, grave=None, agudo=None, suave=0.25):
    """Pasa-banda por FFT con bordes suaves. 'grave' y 'agudo' en Hz."""
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1.0 / FS)
    g = np.ones_like(f)
    if grave:
        g *= 1.0 / (1.0 + (grave / np.maximum(f, 1e-6)) ** (2.0 / suave))
    if agudo:
        g *= 1.0 / (1.0 + (f / agudo) ** (2.0 / suave))
    return np.fft.irfft(X * g, len(x))


def decae(t, tau):
    return np.exp(-t / tau)


def ataque(t, ms):
    return np.clip(t / (ms / 1000.0), 0.0, 1.0)


def bordes(x, ms=4.0):
    """Entrada y salida suaves: evita los chasquidos al empezar y al cortar."""
    n = max(1, int(FS * ms / 1000.0))
    n = min(n, len(x) // 2)
    rampa = np.linspace(0.0, 1.0, n)
    x = x.copy()
    x[:n] *= rampa
    x[-n:] *= rampa[::-1]
    return x


def normalizar(x, pico_db=-3.0):
    m = np.max(np.abs(x))
    if m < 1e-9:
        return x
    return x * (10 ** (pico_db / 20.0) / m)


def poner(destino, x, en_segundos):
    i = int(FS * en_segundos)
    fin = min(len(destino), i + len(x))
    destino[i:fin] += x[: fin - i]


def nota(freq, dur, tau, brillo=0.35):
    """Una campanita: fundamental + dos armónicos que se apagan antes."""
    t = tiempo(dur)
    x = np.sin(2 * np.pi * freq * t) * decae(t, tau)
    x += brillo * np.sin(2 * np.pi * 2 * freq * t) * decae(t, tau * 0.5)
    x += brillo * 0.4 * np.sin(2 * np.pi * 3.01 * freq * t) * decae(t, tau * 0.3)
    return x * ataque(t, 4)


def guardar(nombre, x, pico_db=-3.0):
    os.makedirs(SALIDA, exist_ok=True)
    x = normalizar(bordes(np.asarray(x, dtype=np.float64)), pico_db)
    datos = (np.clip(x, -1.0, 1.0) * 32767.0).astype("<i2")
    ruta = os.path.join(SALIDA, nombre + ".wav")
    with wave.open(ruta, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(FS)
        w.writeframes(datos.tobytes())
    print(f"  {nombre}.wav  {len(x) / FS:5.2f} s  {os.path.getsize(ruta) / 1024:6.1f} KB")


# ---------------------------------------------------------------------------
# Sonidos
# ---------------------------------------------------------------------------
def paso(semilla):
    """Un paso sobre baldosa: golpe sordo del talón + chasquido de la suela."""
    rng = np.random.default_rng(semilla)
    t = tiempo(0.16)
    f0 = rng.uniform(95, 125)
    barrido = f0 * (1.0 - 0.45 * np.clip(t / 0.06, 0, 1))
    golpe = np.sin(2 * np.pi * np.cumsum(barrido) / FS) * decae(t, rng.uniform(0.022, 0.032))
    suela = filtrar(ruido(len(t), rng), 900, rng.uniform(2600, 3800)) * decae(t, rng.uniform(0.010, 0.016))
    roce = filtrar(ruido(len(t), rng), 300, 1100) * decae(t, 0.045) * ataque(t, 6)
    return golpe * 0.9 + suela * rng.uniform(0.5, 0.8) + roce * rng.uniform(0.12, 0.25)


def voz_blip():
    """Una sílaba sin palabras: una vocal corta con dos formantes."""
    t = tiempo(0.075)
    f0 = 210.0 * (1.0 + 0.06 * np.sin(2 * np.pi * 9 * t))
    fase = 2 * np.pi * np.cumsum(f0) / FS
    x = np.zeros_like(t)
    for k in range(1, 16):
        f = 210.0 * k
        # Formantes de una "a" abierta, anchos.
        peso = np.exp(-((f - 750) / 380) ** 2) + 0.6 * np.exp(-((f - 1250) / 420) ** 2) + 0.08
        x += peso * np.sin(k * fase) / k ** 0.3
    return x * ataque(t, 6) * decae(t, 0.035)


def puerta_abrir():
    rng = np.random.default_rng(31)
    x = np.zeros(int(FS * 0.55))
    # Pestillo.
    t = tiempo(0.05)
    poner(x, filtrar(ruido(len(t), rng), 1500, 6000) * decae(t, 0.006) * 0.9, 0.0)
    poner(x, np.sin(2 * np.pi * 620 * t) * decae(t, 0.012) * 0.5, 0.004)
    # Bisagra: un quejido corto que sube.
    t = tiempo(0.36)
    f = 210 + 140 * (t / 0.36) ** 1.4 + 9 * np.sin(2 * np.pi * 23 * t)
    quejido = np.sign(np.sin(2 * np.pi * np.cumsum(f) / FS)) * 0.5 + np.sin(2 * np.pi * np.cumsum(f) / FS)
    quejido = filtrar(quejido, 250, 1900) * np.sin(np.pi * t / 0.36) ** 1.5
    poner(x, quejido * 0.16, 0.05)
    # Aire que mueve la hoja.
    t = tiempo(0.45)
    poner(x, filtrar(ruido(len(t), rng), 120, 700) * np.sin(np.pi * t / 0.45) ** 2 * 0.5, 0.05)
    return x


def puerta_cerrar():
    rng = np.random.default_rng(32)
    x = np.zeros(int(FS * 0.42))
    t = tiempo(0.30)
    poner(x, np.sin(2 * np.pi * (88 - 30 * t / 0.3) * t) * decae(t, 0.05) * 1.0, 0.0)
    poner(x, filtrar(ruido(len(t), rng), 200, 1400) * decae(t, 0.03) * 0.7, 0.0)
    t = tiempo(0.05)
    poner(x, filtrar(ruido(len(t), rng), 1800, 7000) * decae(t, 0.005) * 0.7, 0.045)
    return x


def melodia(notas, paso_s, dur_nota, tau, cola=0.5):
    x = np.zeros(int(FS * (paso_s * len(notas) + dur_nota + cola)))
    for i, f in enumerate(notas):
        acorde = f if isinstance(f, (list, tuple)) else [f]
        for g in acorde:
            poner(x, nota(g, dur_nota + cola, tau) / len(acorde) ** 0.5, i * paso_s)
    return x


def inventario(sube):
    t = tiempo(0.11)
    f = np.linspace(300, 560, len(t)) if sube else np.linspace(560, 300, len(t))
    x = np.sin(2 * np.pi * np.cumsum(f) / FS) * np.sin(np.pi * t / 0.11) ** 0.8
    return x + 0.25 * np.sin(4 * np.pi * np.cumsum(f) / FS) * decae(t, 0.03)


def ascensor():
    """El 'ding' de llegada: una campana con parciales no armónicos."""
    t = tiempo(1.5)
    x = np.zeros_like(t)
    for f, a, tau in [(1318.5, 1.0, 0.42), (2637.0, 0.35, 0.22), (3520.0, 0.18, 0.14), (1975.5, 0.22, 0.30)]:
        x += a * np.sin(2 * np.pi * f * t) * decae(t, tau)
    return x * ataque(t, 2)


def negado():
    x = np.zeros(int(FS * 0.30))
    t = tiempo(0.09)
    zumbido = filtrar(np.sign(np.sin(2 * np.pi * 142 * t)), 80, 1500) * np.sin(np.pi * t / 0.09) ** 0.5
    poner(x, zumbido, 0.0)
    poner(x, zumbido * 0.85, 0.14)
    return x


def ambiente():
    """Fondo de un edificio: ventilación, un zumbido eléctrico y murmullo lejano.
    Diez segundos que empalman consigo mismos."""
    rng = np.random.default_rng(7)
    dur, cruce = 10.0, 1.5
    n = int(FS * (dur + cruce))
    t = np.arange(n) / FS

    aire = filtrar(ruido(n, rng), 40, 420, suave=0.5)
    aire *= 1.0 + 0.18 * np.sin(2 * np.pi * 0.11 * t) + 0.08 * np.sin(2 * np.pi * 0.37 * t + 1.0)

    zumbido = 0.05 * np.sin(2 * np.pi * 120 * t) + 0.02 * np.sin(2 * np.pi * 240 * t)

    # Murmullo: ruido en la banda de la voz, modulado despacio por varias "conversaciones".
    murmullo = np.zeros(n)
    for k in range(5):
        banda = filtrar(ruido(n, rng), 280 + 60 * k, 1500 + 150 * k, suave=0.4)
        mod = 0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(0.4, 1.3) * t + rng.uniform(0, 6.28))
        mod *= 0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(2.0, 4.5) * t + rng.uniform(0, 6.28))
        murmullo += banda * mod
    murmullo *= 0.22 / max(1e-9, np.std(murmullo))

    x = aire / np.std(aire) + zumbido + murmullo * 0.5

    # Empalme: la cola se funde con el principio, así el bucle no tiene costura.
    m = int(FS * cruce)
    k = np.linspace(0.0, 1.0, m)
    cuerpo = x[: n - m].copy()
    cuerpo[:m] = cuerpo[:m] * np.sqrt(k) + x[n - m:] * np.sqrt(1.0 - k)
    return cuerpo


def construir():
    C5, E5, G5, A5, B5, C6, E6, G6 = 523.25, 659.25, 783.99, 880.0, 987.77, 1046.5, 1318.5, 1568.0
    s = {}
    for i in range(1, 7):
        s[f"paso_{i}"] = (lambda i=i: paso(100 + i), -6.0)
    s["voz_blip"] = (voz_blip, -6.0)
    s["puerta_abrir"] = (puerta_abrir, -5.0)
    s["puerta_cerrar"] = (puerta_cerrar, -4.0)
    s["mision_inicio"] = (lambda: melodia([E5, A5], 0.11, 0.35, 0.16), -6.0)
    s["mision_completa"] = (lambda: melodia([C5, E5, G5, C6], 0.10, 0.5, 0.22), -4.0)
    s["todas_completas"] = (lambda: melodia([C5, E5, G5, C6, G5, [C6, E6, G6]], 0.13, 0.9, 0.42, 0.9), -3.0)
    s["objeto_obtenido"] = (lambda: melodia([B5, E6], 0.07, 0.3, 0.13), -6.0)
    s["inventario_abrir"] = (lambda: inventario(True), -9.0)
    s["inventario_cerrar"] = (lambda: inventario(False), -9.0)
    s["ascensor"] = (ascensor, -5.0)
    s["negado"] = (negado, -8.0)
    s["ambiente"] = (ambiente, -14.0)
    return s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--solo", nargs="*", help="Nombres (sin .wav) de los que se quieren regenerar.")
    args = ap.parse_args()

    sonidos = construir()
    elegidos = args.solo if args.solo else list(sonidos)
    print(f"Escribiendo en {SALIDA}")
    for nombre in elegidos:
        if nombre not in sonidos:
            print(f"  (no existe '{nombre}')")
            continue
        hacer, pico = sonidos[nombre]
        if nombre == "ambiente":
            # Sin 'bordes': tiene que empalmar consigo mismo.
            os.makedirs(SALIDA, exist_ok=True)
            x = normalizar(hacer(), pico)
            datos = (np.clip(x, -1.0, 1.0) * 32767.0).astype("<i2")
            ruta = os.path.join(SALIDA, nombre + ".wav")
            with wave.open(ruta, "wb") as w:
                w.setnchannels(1)
                w.setsampwidth(2)
                w.setframerate(FS)
                w.writeframes(datos.tobytes())
            print(f"  {nombre}.wav  {len(x) / FS:5.2f} s  {os.path.getsize(ruta) / 1024:6.1f} KB  (bucle)")
        else:
            guardar(nombre, hacer(), pico)


if __name__ == "__main__":
    main()
