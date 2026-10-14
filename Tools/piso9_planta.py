#!/usr/bin/env python3
"""
piso9_planta.py — Planta del piso 9 trazada a partir de su plano y de las fotos.

Reemplaza al modelo de Meshy como geometría jugable del piso 9 (y de su copia,
el piso 10). Genera:
  Assets/MovU/Models/Piso9/planta_piso9.json   muros, bloques, escaleras, techos...
  Assets/MovU/Textures/Piso9/*.png             madera oscura, granito, terrazo

Fuentes (octubre de 2026):
  - Letrero de la planta junto a los ascensores (códigos P9-01 ... P9-6C).
  - Plano de evacuación del noveno piso (nombres de cada espacio).
  - Fotos y video del recorrido: madera en listones, vidrio con antepecho negro,
    techo a la vista con bandejas, lobby de granito con lamas de madera,
    escalera roja.
  - Docs/piso9_espacios.png: el diagrama del modelo de Meshy, a 30 px por metro.
    Es contra él que se sacaron las (u, v) de contenido_piso9.json, así que las
    medidas de aquí salen de ese diagrama y todo el contenido sigue cayendo en
    su sitio.

Coordenadas: metros del diagrama del piso 9, igual que el piso 3:
  u = ancho del edificio, de 0 (lado de las aulas 2 y 3A-3C) a 39.3 m
  v = largo del edificio, de 0 (extremo de la Oficina Gestión Técnica 3) a 106.6 m
'marco' guarda dónde quedaba el piso 9 en el mundo con el modelo de Meshy (plano,
pivote, losa y separación entre pisos): BuildingSetup lo usa para armar el
edificio sin cargar el modelo.

Uso:   python3 Tools/piso9_planta.py          (desde la raíz del repositorio)
"""
import json, os, random

ANCHO, LARGO = 39.3, 106.6
TECHO = 5.6               # losa de arriba (techo a la vista del pasillo)
CIELO_SALON = 3.3         # cielo raso de los salones y oficinas
CIELO_LOBBY = 4.2         # cielo raso blanco del lobby de ascensores

V_IZQ, U_TOP, U_BOT = 1.1, 1.3, 38.2
U_C1, U_C2 = 18.7, 23.6   # caras del pasillo: lado de las aulas 2/3x y lado de las oficinas/1x
U_MID = (U_C1 + U_C2) / 2


def fachada_v(u):
    """v de la cara interior de la fachada inclinada a la altura u (la misma recta del piso 3)."""
    return 89.8 + (u - 1.6) * (104.0 - 89.8) / (37.2 - 1.6)


muros, bloques, muebles, rotulos = [], [], [], []


def muro(a, b, tipo, g=0.3, nombre="", lamas=False):
    m = {"a": [round(a[0], 3), round(a[1], 3)], "b": [round(b[0], 3), round(b[1], 3)],
         "tipo": tipo, "g": g, "nombre": nombre}
    if lamas:
        m["lamas"] = True
    muros.append(m)


def bloque(v0, v1, u0, u1, tipo="concreto", nombre="", alto=TECHO):
    bloques.append({"v0": v0, "v1": v1, "u0": u0, "u1": u1, "tipo": tipo,
                    "nombre": nombre, "alto": alto})


def frente(u, tramos, nombre=""):
    """Muro a lo largo de v. tramos = [(v0, v1, tipo), ...]; lo que no se lista queda abierto (vanos)."""
    for v0, v1, tipo in tramos:
        if v1 - v0 > 0.05:
            muro((v0, u), (v1, u), tipo, 0.3, nombre)


def mueble(tipo, v0, v1, u0, u1, alto, y0=0.0, solido=True):
    muebles.append({"tipo": tipo, "v0": round(v0, 3), "v1": round(v1, 3), "u0": round(u0, 3),
                    "u1": round(u1, 3), "alto": alto, "y0": y0, "solido": solido})


# =====================================================================
# Fachada
# =====================================================================
muro((V_IZQ, U_TOP), (fachada_v(U_TOP), U_TOP), "fachada", 0.35, "Fachada norte")
muro((V_IZQ, U_TOP), (V_IZQ, U_BOT), "fachada", 0.35, "Fachada del extremo")
muro((fachada_v(U_TOP), U_TOP), (fachada_v(U_BOT), U_BOT), "fachada", 0.35, "Fachada inclinada", lamas=True)
muro((V_IZQ, U_BOT), (fachada_v(U_BOT), U_BOT), "fachada", 0.35, "Fachada sur")

# =====================================================================
# Lado de las aulas (u 1.3 .. 18.7): P9-04, P9-05, P9-6A, P9-6B, P9-6C, seguridad
# =====================================================================
# P9-04 Coworking 1 (v 1.1..17.6). Comparte muro con la P9-03 hasta v 8.9.
muro((V_IZQ, 17.7), (8.9, 17.7), "blanco", 0.3, "Muro P9-03 | P9-04")
frente(17.7, [(8.9, 10.4, "madera"), (10.4, 13.6, "vitrina_negra"), (13.6, 14.1, "madera"),
              (16.7, 17.6, "madera")], "Frente P9-04 Coworking 1")
bloque(17.6, 19.3, U_TOP, U_C1, "concreto", "Ducto P9-04 | P9-05")

# P9-05 Aula de Cómputo 2 (v 19.3..36.15) y P9-6A Aula de Cómputo 3A (v 36.15..49.5):
# las dos puertas comparten un nicho del pasillo (cara en u 16.5).
frente(U_C1, [(19.3, 32.2, "vitrina_negra")], "Frente P9-05 Aula de Cómputo 2")
bloque(32.2, 33.0, 16.5, U_C1, "madera", "Portal P9-05")
frente(16.5, [(33.0, 33.1, "madera"), (35.7, 37.01, "madera")], "Nicho P9-05 | P9-6A")
muro((36.15, U_TOP), (36.15, 16.5), "blanco", 0.3, "Muro P9-05 | P9-6A")
bloque(39.41, 41.2, 16.5, U_C1, "madera", "Portal P9-6A")
frente(U_C1, [(41.2, 49.5, "vitrina_negra")], "Frente P9-6A Aula de Cómputo 3A")
bloque(49.5, 50.3, U_TOP, U_C1, "concreto", "Muro P9-6A | P9-6B")

# P9-6B Aula de Cómputo 3B (v 50.3..66.65) y P9-6C Aula de Cómputo 3C (v 66.65..80.0).
frente(U_C1, [(50.3, 61.8, "vitrina_negra")], "Frente P9-6B Aula de Cómputo 3B")
bloque(61.8, 63.4, 16.6, U_C1, "madera", "Portal P9-6B")
frente(16.6, [(63.4, 63.55, "madera"), (66.25, 67.25, "madera"), (69.55, 69.8, "madera")],
       "Nicho P9-6B | P9-6C")
muro((66.65, U_TOP), (66.65, 16.6), "blanco", 0.3, "Muro P9-6B | P9-6C")
bloque(69.8, 71.2, 16.6, U_C1, "madera", "Portal P9-6C")
frente(U_C1, [(71.2, 80.0, "vitrina_negra")], "Frente P9-6C Aula de Cómputo 3C")
bloque(80.0, 80.8, U_TOP, U_C1, "concreto", "Muro P9-6C | Seguridad")

# Cuarto de seguridad (el cuarto chico junto al pasillo) y el ducto largo detrás.
bloque(80.8, 83.0, U_TOP, 14.6, "concreto", "Ducto junto al cuarto de seguridad")
muro((80.8, 14.6), (83.0, 14.6), "concreto", 0.25, "Fondo del cuarto de seguridad")
frente(U_C1, [(80.8, 81.2, "madera"), (82.6, 83.0, "madera")], "Frente Cuarto de seguridad")

# Caja de la escalera principal: madera hacia el pasillo, vidrio con lamas hacia el lobby.
muro((83.0, U_TOP), (83.0, 19.0), "mostaza", 0.3, "Caja de la escalera principal (norte, muro mostaza)")
# La escalera principal es funcional (StairsManager, "escaleraPrincipal" del JSON de contenido):
# tramo de v 83.4 a 86.2 que sube hacia la fachada (de u 15.5 a 8.0) y rellano junto a las
# ventanas. Aquí va lo que la rodea: la columna del tramo oculto (con sus dos puertas en las
# caras), el macizo bajo el rellano y el cierre del rincón entre la columna y la fachada.
bloque(86.55, 89.0, 8.0, 15.5, "concreto", "Columna del tramo oculto (escalera principal)")
bloque(83.15, 89.0, 1.45, 8.0, "concreto", "Macizo bajo el rellano (escalera principal)", alto=3.0)
muro((89.0, 15.5), (fachada_v(15.5), 15.5), "concreto", 0.25, "Cierre junto a la escalera principal")
frente(19.0, [(83.0, 84.0, "madera"), (86.0, 92.6, "madera")], "Caja de la escalera principal")
muro((92.6, 19.0), (fachada_v(19.0), 19.0), "vidrio", 0.2, "Vidrio de la escalera hacia el lobby", lamas=True)

# =====================================================================
# Lado de las oficinas (u 23.6 .. 38.2)
# =====================================================================
# P9-03 Oficina Gestión Técnica 3 (DTI: soporte técnico), al fondo del pasillo.
muro((8.9, 17.7), (8.9, 19.55), "madera", 0.3, "Frente P9-03")
muro((8.9, 22.0), (8.9, 28.3), "vitrina_negra", 0.3, "Frente P9-03 Oficina Gestión Técnica 3")

# Ducto grande (la caja con la X del plano): panel negro brillante, como en la foto.
bloque(8.9, 19.7, 28.3, U_BOT, "negro_brillante", "Ducto (caja negra)")

# Pasillo de la escalera de emergencia: detrás del muro de lockers.
frente(U_C2, [(17.2, 22.5, "madera")], "Muro de lockers")

# Cuarto eléctrico, Cuarto de cableado y Baño accesible (con ducha), con puerta al pasillo.
frente(U_C2, [(22.5, 24.4, "madera"), (25.9, 28.5, "madera"), (29.9, 31.65, "madera"),
              (33.65, 36.3, "madera")], "Frente de cuartos técnicos")      # el baño accesible lleva puerta de 2 m
for v in (22.5, 27.8, 30.6):
    muro((v, U_C2), (v, 30.2), "concreto", 0.25, "Muro de cuartos técnicos")
muro((21.91, 30.2), (36.3, 30.2), "concreto", 0.3, "Fondo de cuartos | escalera")

# Caja de la escalera de emergencia. La escalera la arma StairsManager desde
# "escalera" del JSON de contenido (tramo u 35.4-38.2, v 23.8-31.5; rellano hasta
# v 35.85). Aquí solo va lo que la rodea: el hueco bajo el tramo oculto queda macizo
# hasta debajo del rellano.
muro((23.9, 30.2), (23.9, 32.3), "concreto", 0.2, "Cierre sobre el tramo oculto")
bloque(23.95, 35.8, 30.35, 35.0, "concreto", "Macizo bajo el tramo oculto", alto=3.3)
bloque(31.6, 35.8, 35.0, 38.05, "concreto", "Macizo bajo el rellano", alto=3.3)
muro((36.3, U_C2), (36.3, U_BOT), "concreto", 0.3, "Muro escalera | baños")

# Baños (mujeres hacia el pasillo, hombres hacia la fachada) con vestíbulo común.
frente(U_C2, [(36.3, 36.8, "madera"), (38.6, 46.3, "madera")], "Frente Baños")
muro((38.8, U_C2), (38.8, 25.5), "blanco", 0.2, "Vestíbulo de baños")
muro((38.8, 27.3), (38.8, 32.0), "blanco", 0.2, "Vestíbulo de baños")
muro((38.8, 33.8), (38.8, U_BOT), "blanco", 0.2, "Vestíbulo de baños")
muro((38.8, 30.8), (46.3, 30.8), "blanco", 0.2, "Baño mujeres | Baño hombres")
muro((46.3, U_C2), (46.3, U_BOT), "concreto", 0.3, "Muro Baños | P9-02")

# P9-02 Oficina Gestión Técnica 2 (DTI: préstamo de equipos) y P9-1B Aula de Cómputo 1B:
# puertas en un nicho común.
frente(U_C2, [(46.3, 55.0, "vitrina_negra")], "Frente P9-02 Oficina Gestión Técnica 2")
bloque(55.0, 56.7, U_C2, 25.0, "madera", "Portal P9-02")
frente(25.0, [(56.7, 56.95, "madera"), (59.35, 60.0, "madera")], "Nicho P9-02")
muro((60.0, 25.0), (60.0, U_BOT), "blanco", 0.3, "Muro P9-02 | P9-1B")
frente(25.45, [(60.0, 60.25, "madera"), (62.55, 62.7, "madera")], "Nicho P9-1B")
bloque(62.7, 64.2, U_C2, 25.45, "madera", "Portal P9-1B")
frente(U_C2, [(64.2, 73.7, "vitrina_negra")], "Frente P9-1B Aula de Cómputo 1B")
muro((73.7, U_C2), (73.7, U_BOT), "blanco", 0.3, "Muro P9-1B | P9-1A")

# P9-1A Aula de Cómputo 1A.
frente(U_C2, [(73.7, 78.5, "vitrina_negra")], "Frente P9-1A Aula de Cómputo 1A")
bloque(78.5, 80.0, U_C2, 24.7, "madera", "Portal P9-1A")
frente(24.7, [(80.0, 80.35, "madera"), (82.85, 83.2, "madera")], "Nicho P9-1A")
muro((83.2, U_C2), (83.2, 24.7), "madera", 0.3, "Nicho P9-1A")
frente(U_C2, [(83.2, 86.2, "madera")], "Frente P9-1A")

# Núcleo de ascensores: concreto hacia el pasillo, granito hacia el lobby.
bloque(86.2, 93.0, U_C2, U_BOT, "concreto", "Núcleo de ascensores")
muro((93.0, U_C2), (93.0, U_BOT), "granito", 0.12, "Granito del lobby")
ascensores = [{"v": 93.07, "u": u, "ancho": 2.4} for u in (26.6, 31.0, 35.4)]
rotulos.append({"texto": "09", "v": 93.08, "u": 33.2, "h": 2.3, "mira": "+v", "tam": 9.0,
                "color": "8C7444"})

# =====================================================================
# Escaleras: la principal y la de emergencia son funcionales (StairsManager)
# =====================================================================
escaleras = [
]

# =====================================================================
# Suelo, pisos de color y techos
# =====================================================================
suelo = [{"v": V_IZQ, "u": U_TOP}, {"v": fachada_v(U_TOP), "u": U_TOP},
         {"v": fachada_v(U_BOT), "u": U_BOT}, {"v": V_IZQ, "u": U_BOT}]

def pts(lista):
    """Puntos (v, u) en el formato que lee JsonUtility: [{"v": .., "u": ..}, ...]."""
    return [{"v": round(a, 3), "u": round(b, 3)} for a, b in lista]


LOBBY = [(93.0, 19.0), (fachada_v(19.0), 19.0), (fachada_v(U_BOT), U_BOT), (93.0, U_BOT)]
pisos = [
    {"tipo": "Terrazo", "h": 0.008, "pts": pts(LOBBY)},
    {"tipo": "BandaGris", "h": 0.014, "pts": pts([(93.12, U_C2), (96.2, U_C2), (96.2, U_BOT), (93.12, U_BOT)])},
]

# Huecos del techo: la caja de la escalera de emergencia (mismos rectángulos que
# StairsManager.HuecoEnElTecho: los dos tramos y el rellano). StairsManager pone
# encima el techo de la caja, más alto.
CONT = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                    "Assets", "MovU", "Resources", "MovU", "contenido_piso9.json")
with open(CONT, encoding="utf-8") as f:
    esc = json.load(f)["escalera"]
huecos = [
    (esc["tramo"]["v0"] * LARGO, esc["rellano"]["v0"] * LARGO, esc["ocultoU0"] * ANCHO, esc["tramo"]["u1"] * ANCHO),
    (esc["rellano"]["v0"] * LARGO, esc["rellano"]["v1"] * LARGO, esc["rellano"]["u0"] * ANCHO, esc["rellano"]["u1"] * ANCHO),
]


def recortar(poligono, v0, v1, u0, u1):
    """Sutherland-Hodgman: polígono convexo (lista de (v, u)) contra un rectángulo."""
    def corte(pts, dentro, cruce):
        salida = []
        for i in range(len(pts)):
            a, b = pts[i], pts[(i + 1) % len(pts)]
            if dentro(b):
                if not dentro(a):
                    salida.append(cruce(a, b))
                salida.append(b)
            elif dentro(a):
                salida.append(cruce(a, b))
        return salida

    def en_v(c):
        return lambda a, b: (c, a[1] + (b[1] - a[1]) * (c - a[0]) / (b[0] - a[0]))

    def en_u(c):
        return lambda a, b: (a[0] + (b[0] - a[0]) * (c - a[1]) / (b[1] - a[1]), c)

    p = list(poligono)
    for dentro, cruce in [(lambda q: q[0] >= v0, en_v(v0)), (lambda q: q[0] <= v1, en_v(v1)),
                          (lambda q: q[1] >= u0, en_u(u0)), (lambda q: q[1] <= u1, en_u(u1))]:
        if not p:
            break
        p = corte(p, dentro, cruce)
    return p


def area(p):
    return abs(sum(p[i][0] * p[(i + 1) % len(p)][1] - p[(i + 1) % len(p)][0] * p[i][1]
                   for i in range(len(p)))) / 2


huella = [(q["v"], q["u"]) for q in suelo]
cortes_v = sorted({-1.0, 200.0} | {h[0] for h in huecos} | {h[1] for h in huecos})
cortes_u = sorted({-1.0, 100.0} | {h[2] for h in huecos} | {h[3] for h in huecos})
techos = []
for i in range(len(cortes_v) - 1):
    for j in range(len(cortes_u) - 1):
        v0, v1, u0, u1 = cortes_v[i], cortes_v[i + 1], cortes_u[j], cortes_u[j + 1]
        cv, cu = (v0 + v1) / 2, (u0 + u1) / 2
        if any(h[0] <= cv <= h[1] and h[2] <= cu <= h[3] for h in huecos):
            continue
        pieza = recortar(huella, v0, v1, u0, u1)
        if len(pieza) >= 3 and area(pieza) > 0.5:
            techos.append({"tipo": "TechoOscuro", "h": TECHO,
                           "pts": pts(pieza)})

# Cielos rasos blancos (los salones y oficinas tienen cielo raso perforado; el pasillo no).
ESPACIOS = {
    "P9-03 Oficina Gestión Técnica 3": [(V_IZQ, 8.9, 17.7, U_BOT)],
    "P9-04 Coworking 1": [(V_IZQ, 17.6, U_TOP, 17.7)],
    "P9-05 Aula de Cómputo 2": [(19.3, 33.0, U_TOP, U_C1), (33.0, 36.15, U_TOP, 16.5)],
    "P9-6A Aula de Cómputo 3A": [(36.15, 41.2, U_TOP, 16.5), (41.2, 49.5, U_TOP, U_C1)],
    "P9-6B Aula de Cómputo 3B": [(50.3, 63.4, U_TOP, U_C1), (63.4, 66.65, U_TOP, 16.6)],
    "P9-6C Aula de Cómputo 3C": [(66.65, 69.8, U_TOP, 16.6), (69.8, 80.0, U_TOP, U_C1)],
    "Cuarto de seguridad": [(80.8, 83.0, 14.6, U_C1)],
    "Cuarto eléctrico": [(22.5, 27.8, U_C2, 30.2)],
    "Cuarto de cableado": [(27.8, 30.6, U_C2, 30.2)],
    "Baño accesible": [(30.6, 36.3, U_C2, 30.2)],
    "Baños": [(36.3, 46.3, U_C2, U_BOT)],
    "P9-02 Oficina Gestión Técnica 2": [(46.3, 55.0, U_C2, U_BOT), (55.0, 60.0, 25.0, U_BOT)],
    "P9-1B Aula de Cómputo 1B": [(60.0, 62.7, 25.45, U_BOT), (62.7, 73.7, U_C2, U_BOT)],
    "P9-1A Aula de Cómputo 1A": [(73.7, 80.0, U_C2, U_BOT), (80.0, 83.2, 24.7, U_BOT),
                                 (83.2, 86.2, U_C2, U_BOT)],
}
for nombre, rects in ESPACIOS.items():
    for v0, v1, u0, u1 in rects:
        techos.append({"tipo": "TechoClaro", "h": CIELO_SALON, "nombre": nombre,
                       "pts": pts([(v0, u0), (v1, u0), (v1, u1), (v0, u1)])})
techos.append({"tipo": "TechoClaro", "h": CIELO_LOBBY, "nombre": "Lobby de ascensores",
               "pts": pts(LOBBY)})

# =====================================================================
# Mobiliario: lo que se ve a través del vidrio (mesas con computador, sillas)
# =====================================================================
def fila_de_puestos(v0, v1, u, sillas_hacia=+1, hueco=1.6):
    """Mesa corrida a lo largo de v en u, partida en dos con un paso al medio."""
    medio = (v0 + v1) / 2
    for a, b in ((v0, medio - hueco / 2), (medio + hueco / 2, v1)):
        if b - a < 1.5:
            continue
        mueble("Mesa", a, b, u - 0.4, u + 0.4, 0.75)
        n = max(1, int((b - a) / 1.5))
        for k in range(n):
            c = a + (b - a) * (k + 0.5) / n
            mueble("Monitor", c - 0.28, c + 0.28, u - 0.1, u - 0.04, 0.38, y0=0.78, solido=False)
            su = u + sillas_hacia * 0.75
            mueble("Silla", c - 0.24, c + 0.24, su - 0.24, su + 0.24, 0.08, y0=0.42, solido=False)
            sb = su + sillas_hacia * 0.22
            mueble("Silla", c - 0.24, c + 0.24, sb - 0.03, sb + 0.03, 0.5, y0=0.5, solido=False)
            mueble("Silla", c - 0.03, c + 0.03, su - 0.03, su + 0.03, 0.42, solido=False)


# Aulas del lado norte (u 1.3..18.7): tres filas; los pasillos entre ellas quedan en u ~6.2, 11 y 15.5.
for v0, v1 in [(19.3, 36.15), (36.15, 49.5), (50.3, 66.65), (66.65, 80.0)]:
    for u in (3.8, 8.6, 13.4):
        fila_de_puestos(v0 + 1.4, v1 - 1.4, u, +1)
# Aulas 1A y 1B (u 23.6..38.2): tres filas; pasillos en u ~29.6 y 34.
for v0, v1 in [(60.0, 73.7), (73.7, 86.2)]:
    for u in (27.6, 31.8, 36.1):
        fila_de_puestos(v0 + 1.6, v1 - 1.4, u, +1)
# Coworking: mesas cuadradas.
for v in (4.5, 10.0, 14.6):
    for u in (4.0, 8.5, 13.6):
        mueble("Mesa", v - 0.8, v + 0.8, u - 0.8, u + 0.8, 0.75)
# Oficinas de DTI: puestos contra la ventana (los mostradores los pone el JSON de contenido).
for v0, v1 in [(2.0, 7.6), (47.6, 53.4), (54.2, 58.6)]:
    mueble("Mesa", v0, v1, 36.5, 37.6, 0.75)
    n = max(1, int((v1 - v0) / 1.6))
    for k in range(n):
        c = v0 + (v1 - v0) * (k + 0.5) / n
        mueble("Monitor", c - 0.28, c + 0.28, 37.3, 37.36, 0.38, y0=0.78, solido=False)

# Baños: mesón de lavamanos y cubículos.
for u0 in (24.0, 31.2):          # mesón contra el vestíbulo de cada baño
    mueble("Meson", 39.6, 43.6, u0, u0 + 0.6, 0.86)
for v in (40.4, 42.2, 44.0):
    mueble("Divisor", v - 0.03, v + 0.03, 27.8, 30.65, 2.0, y0=0.15, solido=False)
    mueble("Divisor", v - 0.03, v + 0.03, 35.0, 38.05, 2.0, y0=0.15, solido=False)
for v in (39.6, 41.3, 43.1, 44.9):
    mueble("Sanitario", v - 0.25, v + 0.25, 29.8, 30.6, 0.42)
    mueble("Sanitario", v - 0.25, v + 0.25, 37.2, 38.0, 0.42)

# =====================================================================
# Techo a la vista del pasillo, luminarias y lámparas
# =====================================================================
bandejas = [{"a": [9.4, U_MID - 0.9], "b": [92.5, U_MID - 0.9], "y": 4.9, "ancho": 0.6},
            {"a": [9.4, U_MID + 1.0], "b": [92.5, U_MID + 1.0], "y": 5.05, "ancho": 0.35}]
luces = [{"a": [9.4, U_C1 + 0.55], "b": [92.0, U_C1 + 0.55], "y": 5.5},
         {"a": [9.4, U_C2 - 0.55], "b": [86.0, U_C2 - 0.55], "y": 5.5},
         {"a": [9.4, 25.5], "b": [17.0, 25.5], "y": 5.5}]          # vestíbulo de la P9-03
lamparas = []
for v in range(11, 92, 8):
    lamparas.append({"v": v, "u": U_MID, "y": 4.8, "rango": 13.0, "intensidad": 1.6})
lamparas += [{"v": 13.0, "u": 25.5, "y": 4.8, "rango": 11.0, "intensidad": 1.3},   # vestíbulo
             {"v": 21.0, "u": 27.0, "y": 4.8, "rango": 9.0, "intensidad": 1.2},    # paso a la escalera
             {"v": 21.8, "u": 34.0, "y": 4.8, "rango": 9.0, "intensidad": 1.2}]    # hall de la escalera
for v, u in [(96.0, 27.0), (99.5, 33.5), (95.5, 21.0)]:                           # lobby
    lamparas.append({"v": v, "u": u, "y": 3.9, "rango": 13.0, "intensidad": 1.6})
for v in (95.0, 97.5, 100.0):                                                     # downlights del lobby
    for u in (25.0, 29.0, 33.0, 37.0):
        if v < fachada_v(u) - 0.8:
            luces.append({"a": [v - 0.15, u], "b": [v + 0.15, u], "y": CIELO_LOBBY - 0.01})
for nombre, rects in ESPACIOS.items():
    for v0, v1, u0, u1 in rects:
        if (v1 - v0) * (u1 - u0) < 25:
            continue
        n = max(1, round((v1 - v0) / 8))
        for k in range(n):
            lamparas.append({"v": round(v0 + (v1 - v0) * (k + 0.5) / n, 2), "u": round((u0 + u1) / 2, 2),
                             "y": CIELO_SALON - 0.35, "rango": 11.0, "intensidad": 1.25})
        for uu in ((u0 + u1) / 2 - 3.0, (u0 + u1) / 2 + 3.0):          # luminarias lineales del cielo raso
            if u0 + 0.8 < uu < u1 - 0.8 and v1 - v0 > 4:
                luces.append({"a": [v0 + 1.2, round(uu, 2)], "b": [v1 - 1.2, round(uu, 2)], "y": CIELO_SALON - 0.01})

# Dónde quedaba el piso 9 con el modelo de Meshy (sondeo de la escena del 14 oct 2026).
marco = {"xMin": -19.6487, "xMax": 19.6487, "zMin": -53.3072, "zMax": 53.3072,
         "pivote": [-0.0252, 2.8031, -0.0348], "alturaLosa": -1.9846, "separacion": 6.4938}

planta = {
    "version": 2,
    "fuente": "Letrero de planta y plano de evacuación del piso 9, fotos y video del recorrido (oct 2026), "
              "sobre las medidas del diagrama Docs/piso9_espacios.png",
    "ancho": ANCHO, "largo": LARGO, "alturaTecho": TECHO, "marco": marco,
    "suelo": suelo, "pisos": pisos, "techos": techos,
    "muros": muros, "bloques": bloques, "escaleras": escaleras,
    "ascensores": ascensores, "torniquetes": [], "muebles": muebles, "rotulos": rotulos,
    "bandejas": bandejas, "luces": luces, "lamparas": lamparas,
}


# =====================================================================
# Texturas propias del piso 9
# =====================================================================
def texturas(carpeta):
    from PIL import Image, ImageDraw, ImageFilter
    os.makedirs(carpeta, exist_ok=True)
    rnd = random.Random(9)
    N = 512

    # Madera oscura en listones verticales, con vetas claras de albura (como en el pasillo).
    im = Image.new("RGB", (N, N))
    d = ImageDraw.Draw(im)
    ancho = N // 10
    for i in range(10):
        x0 = i * ancho
        base = (100 + rnd.randint(-12, 10), 64 + rnd.randint(-7, 7), 44 + rnd.randint(-5, 5))
        d.rectangle([x0, 0, x0 + ancho, N], fill=base)
        for _ in range(45):
            xv = x0 + rnd.randint(2, ancho - 2)
            c = tuple(max(0, v - rnd.randint(4, 12)) for v in base)
            d.line([xv, 0, xv + rnd.randint(-3, 3), N], fill=c, width=1)
        if rnd.random() < 0.25:                       # una veta clara larga, a veces
            xv = x0 + rnd.randint(6, ancho - 6)
            d.line([xv, 0, xv + rnd.randint(-8, 8), N], fill=(176, 132, 86), width=rnd.randint(1, 3))
        d.line([x0, 0, x0, N], fill=(30, 18, 12), width=3)
    im.filter(ImageFilter.GaussianBlur(0.9)).save(os.path.join(carpeta, "P9_Madera.png"))

    # Granito gris moteado (muro de los ascensores).
    im = Image.new("RGB", (N, N), (196, 197, 198))
    for _ in range(55000):
        x, y = rnd.randrange(N), rnd.randrange(N)
        g = rnd.choice([(62, 62, 66), (120, 121, 124), (214, 215, 216), (238, 238, 238), (172, 172, 176)])
        im.putpixel((x, y), g)
    im = im.filter(ImageFilter.GaussianBlur(0.7))
    im.save(os.path.join(carpeta, "P9_Granito.png"))

    # Terrazo claro del lobby.
    im = Image.new("RGB", (N, N), (212, 213, 212))
    for _ in range(9000):
        x, y = rnd.randrange(N), rnd.randrange(N)
        g = rnd.randint(150, 235)
        im.putpixel((x, y), (g, g, g - 2))
    im.filter(ImageFilter.GaussianBlur(0.6)).save(os.path.join(carpeta, "P9_Terrazo.png"))


if __name__ == "__main__":
    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    destino = os.path.join(raiz, "Assets", "MovU", "Models", "Piso9")
    os.makedirs(destino, exist_ok=True)
    with open(os.path.join(destino, "planta_piso9.json"), "w", encoding="utf-8") as f:
        json.dump(planta, f, ensure_ascii=False, indent=1)
    texturas(os.path.join(raiz, "Assets", "MovU", "Textures", "Piso9"))
    print(f"planta_piso9.json: {len(muros)} muros, {len(bloques)} bloques, {len(muebles)} muebles, "
          f"{len(techos)} techos, {len(lamparas)} lámparas. Texturas listas.")
