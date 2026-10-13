#!/usr/bin/env python3
"""
piso3_planta.py — Planta del piso 3 trazada a partir de su plano de evacuación.

Genera:
  Assets/MovU/Models/Piso3/planta_piso3.json   muros, bloques, escaleras, ascensores...
  Assets/MovU/Textures/Piso3/*.png             concreto, madera y piso (texturas propias)

Coordenadas: metros del diagrama del piso 9 (Docs/piso9_espacios.png), es decir
  u = ancho del edificio, de 0 (lado de los salones A-E) a 39.3 m
  v = largo del edificio, de 0 (extremo de DTI Soporte) a 106.6 m
El piso 3 se rectificó contra el contorno del piso 9 (mismo edificio): la foto del
plano se giró 90° y se ajustó por homografía a las cuatro esquinas de la fachada,
así que los ascensores quedan en la misma vertical que los del piso 9.

Uso:   python3 Tools/piso3_planta.py          (desde la raíz del repositorio)
"""
import json, math, os, random

ANCHO, LARGO = 39.3, 106.6
TECHO = 5.6

U_TOP, U_C1, U_C2, U_BOT, V_IZQ = 1.6, 16.2, 21.2, 37.2, 1.0


def fachada_v(u):
    """v de la cara interior de la fachada inclinada a la altura u."""
    return 89.8 + (u - U_TOP) * (104.0 - 89.8) / (U_BOT - U_TOP)


muros = []
bloques = []


def muro(a, b, tipo, g=0.3, nombre=""):
    muros.append({"a": list(a), "b": list(b), "tipo": tipo, "g": g, "nombre": nombre})


def bloque(v0, v1, u0, u1, tipo="concreto", nombre="", alto=TECHO):
    bloques.append({"v0": v0, "v1": v1, "u0": u0, "u1": u1, "tipo": tipo,
                    "nombre": nombre, "alto": alto})


def frente(u, tramos, tipo, g=0.3, nombre=""):
    """Muro a lo largo de v con huecos: tramos = [(v0, v1), ...] sólidos."""
    for v0, v1 in tramos:
        if v1 - v0 > 0.05:
            muro((v0, u), (v1, u), tipo, g, nombre)


# ---------------------------------------------------------------- fachada
muro((V_IZQ, U_TOP), (fachada_v(U_TOP), U_TOP), "fachada", 0.35, "Fachada norte")
muro((V_IZQ, U_TOP), (V_IZQ, 27.1), "fachada", 0.35, "Fachada del extremo")
muro((fachada_v(U_TOP), U_TOP), (fachada_v(U_BOT), U_BOT), "fachada", 0.35, "Fachada inclinada")
muro((12.5, U_BOT), (84.5, U_BOT), "fachada", 0.35, "Fachada sur")
muro((91.3, U_BOT), (fachada_v(U_BOT), U_BOT), "vidrio", 0.2, "Cierre del lobby")

# ---------------------------------------------------------------- fila de arriba (u 1.6 .. 16.2)
frente(U_C1, [(V_IZQ, 6.9)], "madera", nombre="Frente Higiene")
bloque(9.6, 12.6, U_TOP, U_C1, nombre="Ducto entre Higiene y Taller")
frente(U_C1, [(12.6, 27.3)], "madera", nombre="Frente Taller")
bloque(30.4, 31.2, U_TOP, U_C1, nombre="Muro Taller | Sala A")
frente(U_C1, [(34.4, 45.5)], "vitrina", nombre="Frente Sala A")
muro((45.7, U_TOP), (45.7, U_C1), "concreto", 0.4, "Muro Sala A | Sala B")
frente(U_C1, [(45.9, 60.0)], "vitrina", nombre="Frente Sala B")
bloque(63.0, 63.9, U_TOP, U_C1, nombre="Muro Sala B | Sala C")
frente(U_C1, [(67.0, 77.6)], "vitrina", nombre="Frente Sala C")
muro((77.8, U_TOP), (77.8, U_C1), "concreto", 0.4, "Muro Sala C | Seguridad")
# Cuarto de seguridad y paso a la escalera principal
frente(11.2, [(78.0, 78.4), (80.0, 80.9)], "madera", nombre="Frente Cuarto de seguridad")
muro((80.9, U_TOP), (80.9, 11.2), "concreto", 0.3, "Muro Seguridad | Escalera")
# Caja de la escalera principal: cerrada hacia el pasillo salvo la puerta del lobby
frente(U_C1, [(80.9, 91.0), (93.0, fachada_v(U_C1))], "concreto", nombre="Caja de la escalera principal")

# ---------------------------------------------------------------- fila de abajo (u 21.2 .. 37.2)
bloque(V_IZQ, 11.6, 27.1, U_BOT, nombre="Vacío de doble altura")
bloque(11.6, 12.5, U_C2, U_BOT, nombre="Muro de la escalera de emergencia")
frente(U_C2, [(15.6, 16.6), (18.2, 20.3)], "concreto", nombre="Frente Cuarto eléctrico")
frente(U_C2, [(20.3, 21.4), (22.9, 25.0)], "concreto", nombre="Frente Cuarto de cableado")
frente(U_C2, [(25.0, 26.6), (29.2, 29.8)], "madera", nombre="Frente Baño accesible")
bloque(29.8, 30.9, U_C2, 30.4, nombre="Muro Baño accesible | Baños")
for v in (15.6, 20.3, 25.0):
    muro((v, U_C2), (v, 30.4), "concreto", 0.3, "Muro de cuartos técnicos")
muro((15.6, 30.4), (29.8, 30.4), "concreto", 0.3, "Fondo de cuartos técnicos")
muro((29.8, 30.4), (29.8, U_BOT), "concreto", 0.3, "Muro escalera | vestíbulo")
# Baños
frente(U_C2, [(32.9, 42.2)], "madera", nombre="Frente Baño mujeres")
muro((32.9, U_C2), (32.9, 28.0), "concreto", 0.25, "Vestíbulo de baños")
muro((32.9, 30.2), (32.9, U_BOT), "concreto", 0.25, "Vestíbulo de baños")
muro((35.3, 28.0), (42.2, 28.0), "concreto", 0.25, "Baño mujeres")
muro((35.3, 30.2), (42.2, 30.2), "concreto", 0.25, "Baño hombres")
muro((42.2, U_C2), (42.2, U_BOT), "concreto", 0.3, "Muro Baños | Oficinas")
# Oficinas y Simulación
frente(U_C2, [(42.2, 56.8)], "madera", nombre="Frente Oficinas")
muro((60.2, U_C2), (60.2, U_BOT), "madera", 0.2, "Muro Oficinas | Simulación")
frente(U_C2, [(63.5, 84.5)], "vitrina", nombre="Frente Simulación")
muro((84.5, U_C2), (84.5, U_BOT), "concreto", 0.3, "Muro Simulación | Ascensores")
bloque(84.5, 91.3, U_C2, U_BOT, nombre="Núcleo de ascensores")

ascensores = [{"v": 91.3, "u": u, "ancho": 2.6} for u in (24.25, 29.25, 34.25)]
# Torniquetes de la entrada: a un lado, para dejar libre el sitio del ascensor del
# juego (u 36.0, v 98.9, el mismo de todos los pisos).
torniquetes = [{"v": 93.0 + 1.6 * i, "u": 36.4} for i in range(3)]

# ---------------------------------------------------------------- escaleras (solo se ven)
# Cada tramo sube en la dirección 'sube' (+v, -v, +u, -u) de h0 a h1.
escaleras = [
    {"nombre": "Escalera principal", "estilo": "roja", "tramos": [
        {"v0": 85.0, "v1": 89.6, "u0": 11.9, "u1": 15.0, "sube": "+v", "h0": 0.0, "h1": 1.75, "n": 9},
        {"v0": 89.6, "v1": 91.0, "u0": 4.6, "u1": 15.0, "sube": "", "h0": 1.75, "h1": 1.75, "n": 0},
        {"v0": 81.9, "v1": 84.6, "u0": 4.6, "u1": 11.9, "sube": "-u", "h0": 0.0, "h1": 1.75, "n": 9},
        {"v0": 81.9, "v1": 91.0, "u0": 2.0, "u1": 4.6, "sube": "", "h0": 1.75, "h1": 1.75, "n": 0}],
     "nucleo": {"v0": 84.6, "v1": 89.6, "u0": 4.6, "u1": 11.9},
     "barreras": [{"a": [84.85, 11.9], "b": [84.85, 15.0]}, {"a": [81.9, 12.15], "b": [84.6, 12.15]}]},
    {"nombre": "Escalera de emergencia", "estilo": "gris", "tramos": [
        {"v0": 15.9, "v1": 27.4, "u0": 33.9, "u1": 37.0, "sube": "+v", "h0": 0.0, "h1": 1.75, "n": 11},
        {"v0": 27.4, "v1": 29.6, "u0": 30.6, "u1": 37.0, "sube": "", "h0": 1.75, "h1": 1.75, "n": 0},
        {"v0": 15.9, "v1": 27.4, "u0": 30.6, "u1": 33.7, "sube": "-v", "h0": 1.75, "h1": 3.5, "n": 11}],
     "nucleo": {"v0": 15.9, "v1": 27.4, "u0": 33.7, "u1": 33.9},
     "barreras": [{"a": [15.65, 33.9], "b": [15.65, 37.0]}]},
]

suelo = [{"v": V_IZQ, "u": U_TOP}, {"v": fachada_v(U_TOP), "u": U_TOP},
         {"v": fachada_v(U_BOT), "u": U_BOT}, {"v": V_IZQ, "u": U_BOT}]

# Bandejas de cables y luminarias lineales del pasillo (techo a la vista).
u_mid = (U_C1 + U_C2) / 2
bandejas = [{"a": [2.0, u_mid - 0.9], "b": [91.0, u_mid - 0.9], "y": 4.9, "ancho": 0.6},
            {"a": [2.0, u_mid + 1.0], "b": [91.0, u_mid + 1.0], "y": 5.05, "ancho": 0.35}]
luces = [{"a": [2.0, U_C1 + 0.45], "b": [80.0, U_C1 + 0.45], "y": 5.5},
         {"a": [2.0, U_C2 - 0.45], "b": [84.0, U_C2 - 0.45], "y": 5.5}]
# Una fila de luces en cada salón.
for v0, v1, u in [(1.5, 9.4, 9), (12.8, 30.2, 9), (31.4, 45.3, 9), (46.1, 62.8, 9), (64.1, 77.4, 9),
                  (42.5, 59.9, 29), (60.5, 84.2, 29)]:
    luces.append({"a": [v0 + 0.8, u], "b": [v1 - 0.8, u], "y": 5.5})

# Lámparas reales (sin sombras): la ruta Forward+ del proyecto no limita cuántas.
lamparas = []
for v in range(5, 90, 8):
    lamparas.append({"v": v, "u": u_mid, "y": 4.8, "rango": 13.0, "intensidad": 1.6})
lamparas += [{"v": 96.0, "u": 26.0, "y": 4.8, "rango": 14.0, "intensidad": 1.8},
             {"v": 99.0, "u": 33.0, "y": 4.8, "rango": 12.0, "intensidad": 1.6},
             {"v": 92.0, "u": 18.5, "y": 4.8, "rango": 12.0, "intensidad": 1.5}]
for v0, v1, u0, u1 in [(1.0, 9.6, 1.6, 16.2), (12.6, 30.4, 1.6, 16.2), (31.2, 45.5, 1.6, 16.2),
                       (45.9, 63.0, 1.6, 16.2), (63.9, 77.6, 1.6, 16.2), (42.2, 60.2, 21.2, 37.2),
                       (60.2, 84.5, 21.2, 37.2), (80.9, 91.0, 1.6, 16.2), (12.5, 30.9, 21.2, 37.2)]:
    n = max(1, round((v1 - v0) / 10))
    for k in range(n):
        lamparas.append({"v": v0 + (v1 - v0) * (k + 0.5) / n, "u": (u0 + u1) / 2, "y": 4.6,
                         "rango": 12.0, "intensidad": 1.3})

planta = {
    "version": 1,
    "fuente": "Foto del plano de evacuación del piso 3, rectificada contra el contorno del piso 9",
    "ancho": ANCHO, "largo": LARGO, "alturaTecho": TECHO,
    "suelo": suelo, "muros": muros, "bloques": bloques, "escaleras": escaleras,
    "ascensores": ascensores, "torniquetes": torniquetes,
    "bandejas": bandejas, "luces": luces, "lamparas": lamparas,
}


# ---------------------------------------------------------------- texturas
def texturas(carpeta):
    from PIL import Image, ImageDraw, ImageFilter
    os.makedirs(carpeta, exist_ok=True)
    rnd = random.Random(3)
    N = 512

    # Concreto a la vista: tablas anchas y apenas marcadas, con poros. 2 m por textura.
    im = Image.new("RGB", (N, N), (184, 180, 173))
    d = ImageDraw.Draw(im)
    for i in range(8):                        # tablas verticales de 25 cm, tono casi igual
        x0 = i * N // 8
        tono = rnd.randint(-5, 5)
        d.rectangle([x0, 0, x0 + N // 8, N], fill=(184 + tono, 180 + tono, 172 + tono))
        d.line([x0, 0, x0, N], fill=(172, 168, 161), width=1)
    for _ in range(60):                       # manchas suaves
        x, y, r = rnd.randrange(N), rnd.randrange(N), rnd.randint(10, 40)
        g = rnd.randint(-8, 8)
        d.ellipse([x - r, y - r, x + r, y + r], fill=(184 + g, 180 + g, 172 + g))
    im = im.filter(ImageFilter.GaussianBlur(6))
    for _ in range(7000):                     # poros
        x, y = rnd.randrange(N), rnd.randrange(N)
        g = rnd.randint(150, 200)
        im.putpixel((x, y), (g, g - 3, g - 7))
    d = ImageDraw.Draw(im)
    for x, y in [(N // 4, N // 3), (3 * N // 4, N // 3), (N // 4, 5 * N // 6), (3 * N // 4, 5 * N // 6)]:
        d.ellipse([x - 4, y - 4, x + 4, y + 4], fill=(140, 137, 132))   # huecos de las formaletas
    im.filter(ImageFilter.GaussianBlur(0.6)).save(os.path.join(carpeta, "P3_Concreto.png"))

    # Madera en listones verticales (como los muros del pasillo y los salones).
    im = Image.new("RGB", (N, N))
    d = ImageDraw.Draw(im)
    ancho = N // 12
    for i in range(12):
        x0 = i * ancho
        base = (108 + rnd.randint(-14, 10), 62 + rnd.randint(-8, 8), 38 + rnd.randint(-6, 6))
        d.rectangle([x0, 0, x0 + ancho, N], fill=base)
        for _ in range(40):                    # vetas
            xv = x0 + rnd.randint(2, ancho - 2)
            c = tuple(max(0, v - rnd.randint(8, 22)) for v in base)
            d.line([xv, 0, xv + rnd.randint(-3, 3), N], fill=c, width=1)
        d.line([x0, 0, x0, N], fill=(40, 24, 16), width=3)            # junta oscura
    im.filter(ImageFilter.GaussianBlur(0.5)).save(os.path.join(carpeta, "P3_Madera.png"))

    # Piso gris claro epóxico con un poco de grano.
    im = Image.new("RGB", (N, N), (196, 196, 192))
    for _ in range(14000):
        x, y = rnd.randrange(N), rnd.randrange(N)
        g = rnd.randint(176, 214)
        im.putpixel((x, y), (g, g, g - 3))
    im.filter(ImageFilter.GaussianBlur(0.8)).save(os.path.join(carpeta, "P3_Piso.png"))


if __name__ == "__main__":
    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    destino = os.path.join(raiz, "Assets", "MovU", "Models", "Piso3")
    os.makedirs(destino, exist_ok=True)
    with open(os.path.join(destino, "planta_piso3.json"), "w", encoding="utf-8") as f:
        json.dump(planta, f, ensure_ascii=False, indent=1)
    texturas(os.path.join(raiz, "Assets", "MovU", "Textures", "Piso3"))
    print(f"planta_piso3.json: {len(muros)} muros, {len(bloques)} bloques, "
          f"{len(escaleras)} escaleras, {len(ascensores)} ascensores. Texturas listas.")
