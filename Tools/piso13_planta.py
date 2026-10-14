#!/usr/bin/env python3
"""
piso13_planta.py — Planta del piso 13 trazada a partir de su plano de evacuación y de las fotos.

Genera Assets/MovU/Models/Piso13/planta_piso13.json (las texturas son las del piso 9).

Fuentes (14 de octubre de 2026): el plano de evacuación del "treceavo piso" (viene girado
180° respecto al del piso 9: ascensores arriba a la derecha), 9 fotos y 3 videos del recorrido.

El núcleo es el mismo del piso 9 (mismo edificio): ascensores, escalera principal, escalera
de emergencia, ducto, cuartos eléctrico y de cableado, baños y cuarto de seguridad caen en el
mismo sitio, y los muros estructurales (v 17.6-19.3, 49.5-50.3, 80.0-80.8) también. Cambian los
espacios: Virtualización, Laboratorio de Bioingeniería (con el de Tejidos adentro), Robótica
Autónoma, Robótica y Educación, Nanolab / Plasmónica y dos oficinas de Investigadores.

Lo que se ve en las fotos y no tiene el piso 9:
  - DOBLE ALTURA en el lobby, el pasillo y los laboratorios: el techo va a 8,4 m. El piso 13 es
    el último de la pila del juego, así que no choca con nada arriba.
  - Fachada con perfiles dorados y un travesaño ancho a media altura.
  - Muros de madera y frentes de vidrio hasta ~3,6 m; arriba, pared gris oscura.
  - Lámparas colgantes de campana en el pasillo, bajo bandejas de cables a la vista.
  - Oficinas de Investigadores con frente de vidrio claro y perfiles dorados.
  - Baños con muros de pizarra.
  - La escalera de emergencia es la funcional (StairsManager), como en el 9 y los pisos genéricos.

Coordenadas: metros del diagrama del piso 9 (u a lo ancho, v a lo largo), igual que los pisos 3 y 9.

Uso:   python3 Tools/piso13_planta.py          (desde la raíz del repositorio)
"""
import json, os

ANCHO, LARGO = 39.3, 106.6
TECHO = 8.4               # doble altura
PANEL = 3.6               # hasta aquí madera y vidrio; arriba, pared gris
CIELO_OFICINA = 3.3

V_IZQ, U_TOP, U_BOT = 1.1, 1.3, 38.2
U_C1, U_C2 = 18.7, 23.6
U_MID = (U_C1 + U_C2) / 2


def fachada_v(u):
    """v de la cara interior de la fachada inclinada (la misma recta de los pisos 3 y 9)."""
    return 89.8 + (u - 1.6) * (104.0 - 89.8) / (37.2 - 1.6)


muros, bloques, muebles, rotulos = [], [], [], []


def muro(a, b, tipo, g=0.3, nombre=""):
    muros.append({"a": [round(a[0], 3), round(a[1], 3)], "b": [round(b[0], 3), round(b[1], 3)],
                  "tipo": tipo, "g": g, "nombre": nombre})


def bloque(v0, v1, u0, u1, tipo="concreto", nombre="", alto=TECHO):
    bloques.append({"v0": v0, "v1": v1, "u0": u0, "u1": u1, "tipo": tipo, "nombre": nombre, "alto": alto})


def frente(u, tramos, nombre=""):
    """Muro a lo largo de v en u. tramos = [(v0, v1, tipo), ...]; lo que no se lista son vanos."""
    for v0, v1, tipo in tramos:
        if v1 - v0 > 0.05:
            muro((v0, u), (v1, u), tipo, 0.3, nombre)


def mueble(tipo, v0, v1, u0, u1, alto, y0=0.0, solido=True):
    muebles.append({"tipo": tipo, "v0": round(v0, 3), "v1": round(v1, 3), "u0": round(u0, 3),
                    "u1": round(u1, 3), "alto": alto, "y0": y0, "solido": solido})


def pts(lista):
    return [{"v": round(a, 3), "u": round(b, 3)} for a, b in lista]


# =====================================================================
# Fachada: vidrio de doble altura con perfiles dorados
# =====================================================================
muro((V_IZQ, U_TOP), (fachada_v(U_TOP), U_TOP), "fachada_dorada", 0.35, "Fachada norte")
muro((V_IZQ, U_TOP), (V_IZQ, U_BOT), "fachada_dorada", 0.35, "Fachada del extremo")
muro((fachada_v(U_TOP), U_TOP), (fachada_v(U_BOT), U_BOT), "fachada_dorada", 0.35, "Fachada inclinada")
muro((V_IZQ, U_BOT), (fachada_v(U_BOT), U_BOT), "fachada_dorada", 0.35, "Fachada sur")

# =====================================================================
# Lado de los laboratorios (u 1.3 .. 18.7)
# =====================================================================
# Los muros estructurales entre laboratorios van blancos (en las fotos los laboratorios son blancos).
# Virtualización (v 1.1..17.6), al fondo; comparte muro con Investigadores hasta v 8.9.
muro((V_IZQ, 17.7), (8.9, 17.7), "blanco", 0.3, "Muro Virtualización | Investigadores")
frente(17.7, [(8.9, 10.4, "madera"), (10.4, 13.6, "vitrina_negra"), (16.0, 17.6, "madera")],
       "Frente Virtualización")
bloque(17.6, 19.3, U_TOP, U_C1, "blanco", "Muro Virtualización | Bioingeniería")

# Laboratorio de Bioingeniería (v 19.3..49.5) con el Laboratorio de Tejidos adentro,
# contra la fachada (u 1.3..9.4, v 19.3..37.8). Se entra a Tejidos desde Bioingeniería.
frente(U_C1, [(19.3, 45.2, "vitrina_negra")], "Frente Laboratorio de Bioingeniería")
bloque(45.2, 46.8, 16.5, U_C1, "madera", "Portal Bioingeniería")
frente(16.5, [(49.2, 49.5, "madera")], "Nicho Bioingeniería")
muro((19.3, 9.4), (32.0, 9.4), "blanco", 0.25, "Muro Tejidos | Bioingeniería")
muro((32.0, 9.4), (34.0, 9.4), "vidrio_dorado", 0.12, "Vidrio de Tejidos")
muro((35.8, 9.4), (37.8, 9.4), "vidrio_dorado", 0.12, "Vidrio de Tejidos")
muro((37.8, U_TOP), (37.8, 9.4), "blanco", 0.25, "Muro Tejidos | Bioingeniería")
bloque(49.5, 50.3, U_TOP, U_C1, "blanco", "Muro Bioingeniería | Robótica Autónoma")

# Robótica Autónoma (v 50.3..70.0) y Robótica y Educación (v 70.0..80.0): puertas en un nicho común.
frente(U_C1, [(50.3, 65.0, "vitrina_negra")], "Frente Robótica Autónoma")
bloque(65.0, 66.6, 16.6, U_C1, "madera", "Portal Robótica Autónoma")
frente(16.6, [(66.6, 66.8, "madera"), (69.2, 70.8, "madera"), (73.2, 73.4, "madera")],
       "Nicho Robótica Autónoma | Robótica y Educación")
muro((70.0, U_TOP), (70.0, 16.6), "blanco", 0.3, "Muro Robótica Autónoma | Robótica y Educación")
bloque(73.4, 74.8, 16.6, U_C1, "madera", "Portal Robótica y Educación")
frente(U_C1, [(74.8, 80.0, "vitrina_negra")], "Frente Robótica y Educación")
bloque(80.0, 80.8, U_TOP, U_C1, "blanco", "Muro Robótica y Educación | Seguridad")

# Cuarto de seguridad y ducto, como en el 9.
bloque(80.8, 83.0, U_TOP, 14.6, "concreto", "Ducto junto al cuarto de seguridad")
muro((80.8, 14.6), (83.0, 14.6), "concreto", 0.25, "Fondo del cuarto de seguridad")
frente(U_C1, [(80.8, 81.2, "madera"), (82.6, 83.0, "madera")], "Frente Cuarto de seguridad")

# Caja de la escalera principal.
muro((83.0, U_TOP), (83.0, 19.0), "mostaza", 0.3, "Caja de la escalera principal (norte, muro mostaza)")
# La escalera principal es funcional (StairsManager, "escaleraPrincipal" del JSON de contenido):
# tramo de v 83.4 a 86.2 que sube hacia la fachada (de u 15.5 a 8.0) y rellano junto a las
# ventanas. Aquí va lo que la rodea: la columna del tramo oculto (con sus dos puertas en las
# caras), el macizo bajo el rellano y el cierre del rincón entre la columna y la fachada.
bloque(86.55, 89.0, 8.0, 15.5, "concreto", "Columna del tramo oculto (escalera principal)")
bloque(83.15, 89.0, 1.45, 8.0, "concreto", "Macizo bajo el rellano (escalera principal)", alto=3.0)
muro((89.0, 15.5), (fachada_v(15.5), 15.5), "concreto", 0.25, "Cierre junto a la escalera principal")
frente(19.0, [(83.0, 84.0, "madera"), (86.0, 92.6, "madera")], "Caja de la escalera principal")
muro((92.6, 19.0), (fachada_v(19.0), 19.0), "vidrio", 0.2, "Vidrio de la escalera hacia el lobby")

# =====================================================================
# Lado de las oficinas (u 23.6 .. 38.2)
# =====================================================================
# Investigadores (P13-3), al fondo: frente de vidrio claro con perfiles dorados.
muro((8.9, 17.7), (8.9, 19.55), "madera", 0.3, "Frente Investigadores P13-3")
muro((8.9, 22.0), (8.9, 28.3), "vidrio_dorado", 0.2, "Frente Investigadores P13-3")

# Ducto de servicio (la caja con la X): forrado en madera, como en las fotos.
bloque(8.9, 19.7, 28.3, U_BOT, "madera", "Ducto (caja con X)")

# Paso a la escalera de emergencia, detrás del muro de lockers.
frente(U_C2, [(17.2, 22.5, "madera")], "Muro de lockers")

# Cuarto eléctrico, Cuarto de cableado y Baño accesible.
frente(U_C2, [(22.5, 24.4, "madera"), (25.9, 28.5, "madera"), (29.9, 31.65, "madera"),
              (33.65, 36.3, "madera")], "Frente de cuartos técnicos")
for v in (22.5, 27.8, 30.6):
    muro((v, U_C2), (v, 30.2), "concreto", 0.25, "Muro de cuartos técnicos")
muro((21.91, 30.2), (36.3, 30.2), "concreto", 0.3, "Fondo de cuartos | escalera")
# Caja de la escalera de emergencia: la escalera funcional la arma StairsManager (la misma del
# 9 y de los pisos genéricos). Aquí, como en el 9, el hueco bajo el tramo oculto queda macizo.
muro((23.9, 30.2), (23.9, 32.3), "concreto", 0.2, "Cierre sobre el tramo oculto")
bloque(23.95, 35.8, 30.35, 35.0, "concreto", "Macizo bajo el tramo oculto", alto=3.3)
bloque(31.6, 35.8, 35.0, 38.05, "concreto", "Macizo bajo el rellano", alto=3.3)
muro((36.3, U_C2), (36.3, U_BOT), "concreto", 0.3, "Muro escalera | baños")

# Baños (mujeres hacia el pasillo, hombres hacia la fachada), muros de pizarra.
frente(U_C2, [(36.3, 36.8, "madera"), (38.6, 48.5, "madera")], "Frente Baños")
muro((38.8, U_C2), (38.8, 25.5), "pizarra", 0.2, "Vestíbulo de baños")
muro((38.8, 27.3), (38.8, 32.0), "pizarra", 0.2, "Vestíbulo de baños")
muro((38.8, 33.8), (38.8, U_BOT), "pizarra", 0.2, "Vestíbulo de baños")
muro((38.8, 30.8), (48.5, 30.8), "pizarra", 0.2, "Baño mujeres | Baño hombres")
muro((48.5, U_C2), (48.5, U_BOT), "concreto", 0.3, "Muro Baños | Investigadores")

# Investigadores (junto a los baños) y Nanolab / Plasmónica: puertas en un nicho común.
frente(U_C2, [(48.5, 55.0, "vitrina_negra")], "Frente Investigadores")
bloque(55.0, 56.7, U_C2, 25.0, "madera", "Portal Investigadores")
frente(25.0, [(56.7, 57.2, "madera"), (59.6, 60.0, "madera")], "Nicho Investigadores")
muro((60.0, 25.0), (60.0, U_BOT), "blanco", 0.3, "Muro Investigadores | Nanolab")
frente(25.45, [(60.0, 60.7, "madera"), (63.1, 63.3, "madera")], "Nicho Nanolab")
bloque(63.3, 64.8, U_C2, 25.45, "madera", "Portal Nanolab")
frente(U_C2, [(64.8, 80.0, "vitrina_negra"), (80.0, 86.2, "madera")], "Frente Nanolab / Plasmónica")
# La antesala del Nanolab (v 80..86.2), contra los ascensores: se entra por el laboratorio.
muro((80.0, U_C2), (80.0, 25.6), "blanco", 0.25, "Nanolab | antesala")
muro((80.0, 27.8), (80.0, U_BOT), "blanco", 0.25, "Nanolab | antesala")

# Núcleo de ascensores: concreto hacia el pasillo, granito hacia el lobby, con el "13".
bloque(86.2, 93.0, U_C2, U_BOT, "concreto", "Núcleo de ascensores")
muro((93.0, U_C2), (93.0, U_BOT), "granito", 0.12, "Granito del lobby")
ascensores = [{"v": 93.07, "u": u, "ancho": 2.4} for u in (26.6, 31.0, 35.4)]
rotulos.append({"texto": "13", "v": 93.08, "u": 33.2, "h": 2.3, "mira": "+v", "tam": 9.0, "color": "8C7444"})

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
huella = [(q["v"], q["u"]) for q in suelo]
LOBBY = [(93.0, 19.0), (fachada_v(19.0), 19.0), (fachada_v(U_BOT), U_BOT), (93.0, U_BOT)]
pisos = [{"tipo": "Terrazo", "h": 0.008, "pts": pts(LOBBY)}]

# Losa de arriba, oscura (pasillo y techo a la vista), y cielos rasos blancos.
techos = [{"tipo": "TechoOscuro", "h": TECHO, "pts": pts(huella)}]
LABS = {
    "Virtualización": [(V_IZQ, 17.6, U_TOP, 17.7)],
    "Laboratorio de Bioingeniería": [(19.3, 37.8, 9.4, U_C1), (37.8, 45.2, U_TOP, U_C1), (45.2, 49.5, U_TOP, 16.5)],
    "Laboratorio de Tejidos": [(19.3, 37.8, U_TOP, 9.4)],
    "Robótica Autónoma": [(50.3, 65.0, U_TOP, U_C1), (65.0, 70.0, U_TOP, 16.6)],
    "Robótica y Educación": [(70.0, 74.8, U_TOP, 16.6), (74.8, 80.0, U_TOP, U_C1)],
    "Nanolab / Plasmónica": [(60.0, 64.8, 25.45, U_BOT), (64.8, 86.2, U_C2, U_BOT)],
}
OFICINAS = {
    "Investigadores P13-3": [(V_IZQ, 8.9, 17.7, U_BOT)],
    "Investigadores": [(48.5, 55.0, U_C2, U_BOT), (55.0, 60.0, 25.0, U_BOT)],
    "Cuarto de seguridad": [(80.8, 83.0, 14.6, U_C1)],
    "Cuarto eléctrico": [(22.5, 27.8, U_C2, 30.2)],
    "Cuarto de cableado": [(27.8, 30.6, U_C2, 30.2)],
    "Baño accesible": [(30.6, 36.3, U_C2, 30.2)],
    "Baños": [(36.3, 48.5, U_C2, U_BOT)],
}
for nombre, rects in LABS.items():               # doble altura: cielo blanco justo bajo la losa
    for v0, v1, u0, u1 in rects:
        techos.append({"tipo": "TechoClaro", "h": TECHO - 0.05, "nombre": nombre,
                       "pts": pts([(v0, u0), (v1, u0), (v1, u1), (v0, u1)])})
for nombre, rects in OFICINAS.items():
    for v0, v1, u0, u1 in rects:
        techos.append({"tipo": "TechoClaro", "h": CIELO_OFICINA, "nombre": nombre,
                       "pts": pts([(v0, u0), (v1, u0), (v1, u1), (v0, u1)])})
techos.append({"tipo": "TechoClaro", "h": TECHO - 0.05, "nombre": "Lobby de ascensores", "pts": pts(LOBBY)})


# =====================================================================
# Mobiliario
# =====================================================================
def fila_de_puestos(v0, v1, u, sillas_hacia=+1, hueco=1.6, mesa="Mesa"):
    medio = (v0 + v1) / 2
    for a, b in ((v0, medio - hueco / 2), (medio + hueco / 2, v1)):
        if b - a < 1.5:
            continue
        mueble(mesa, a, b, u - 0.4, u + 0.4, 0.75 if mesa == "Mesa" else 0.9)
        n = max(1, int((b - a) / 1.5))
        for k in range(n):
            c = a + (b - a) * (k + 0.5) / n
            if mesa == "Mesa":
                mueble("Monitor", c - 0.28, c + 0.28, u - 0.1, u - 0.04, 0.38, y0=0.78, solido=False)
            su = u + sillas_hacia * 0.75
            mueble("Silla", c - 0.24, c + 0.24, su - 0.24, su + 0.24, 0.08, y0=0.42, solido=False)
            sb = su + sillas_hacia * 0.22
            mueble("Silla", c - 0.24, c + 0.24, sb - 0.03, sb + 0.03, 0.5, y0=0.5, solido=False)
            mueble("Silla", c - 0.03, c + 0.03, su - 0.03, su + 0.03, 0.42, solido=False)


# Virtualización y Robótica: puestos con computador (las fotos de los laboratorios).
for u in (3.8, 8.6, 13.4):
    fila_de_puestos(2.5, 16.2, u)
for v0, v1 in [(50.3, 70.0), (70.0, 80.0)]:
    for u in (3.8, 8.6, 13.4):
        fila_de_puestos(v0 + 1.4, v1 - 1.4, u)
# Bioingeniería, Tejidos y Nanolab: mesones de laboratorio.
for u in (12.0, 15.2):
    fila_de_puestos(20.8, 36.4, u, mesa="Meson")
for u in (3.8, 8.6, 13.4):
    fila_de_puestos(39.2, 48.0, u, mesa="Meson")
fila_de_puestos(20.8, 36.4, 5.2, mesa="Meson")
for u in (28.0, 32.4, 36.2):
    fila_de_puestos(66.0, 78.6, u, mesa="Meson")
# Vitrinas blancas con vidrio (foto del laboratorio) contra los muros.
for v0, v1, u0, u1 in [(46.0, 49.2, 1.6, 2.2), (78.0, 79.7, 1.6, 2.2), (51.0, 54.0, 1.6, 2.2), (15.5, 17.3, 1.6, 2.2)]:
    mueble("Vitrina", v0, v1, u0, u1, 2.0)
# Oficinas de Investigadores: puestos contra la ventana.
for v0, v1 in [(2.0, 7.6), (49.6, 54.4), (55.2, 59.2)]:
    mueble("Mesa", v0, v1, 36.5, 37.6, 0.75)
    n = max(1, int((v1 - v0) / 1.6))
    for k in range(n):
        c = v0 + (v1 - v0) * (k + 0.5) / n
        mueble("Monitor", c - 0.28, c + 0.28, 37.3, 37.36, 0.38, y0=0.78, solido=False)
mueble("Vitrina", 2.0, 3.4, 18.1, 18.8, 2.0)          # el mueble blanco de la foto de la P13-3

# Baños.
for u0 in (24.0, 31.2):
    mueble("Meson", 39.6, 43.6, u0, u0 + 0.6, 0.86)
for v in (41.0, 43.2, 45.4):
    mueble("Divisor", v - 0.03, v + 0.03, 27.8, 30.65, 2.0, y0=0.15, solido=False)
    mueble("Divisor", v - 0.03, v + 0.03, 35.0, 38.05, 2.0, y0=0.15, solido=False)
for v in (40.0, 42.1, 44.3, 46.6):
    mueble("Sanitario", v - 0.25, v + 0.25, 29.8, 30.6, 0.42)
    mueble("Sanitario", v - 0.25, v + 0.25, 37.2, 38.0, 0.42)

# =====================================================================
# Techo a la vista del pasillo, lámparas de campana y luces
# =====================================================================
bandejas = [{"a": [9.4, U_MID - 0.9], "b": [92.5, U_MID - 0.9], "y": 7.0, "ancho": 0.6},
            {"a": [9.4, U_MID + 1.0], "b": [92.5, U_MID + 1.0], "y": 7.2, "ancho": 0.35}]
luces, lamparas = [], []
for v in [x * 4.0 + 10.0 for x in range(21)]:          # campanas cada 4 m, como en las fotos
    mueble("TuboOscuro", v - 0.02, v + 0.02, U_MID - 0.02, U_MID + 0.02, TECHO - 5.6, y0=5.6, solido=False)
    mueble("Campana", v - 0.28, v + 0.28, U_MID - 0.28, U_MID + 0.28, 0.32, y0=5.3, solido=False)
    luces.append({"a": [v - 0.2, U_MID], "b": [v + 0.2, U_MID], "y": 5.29})
for v in range(11, 92, 8):
    lamparas.append({"v": v, "u": U_MID, "y": 5.1, "rango": 14.0, "intensidad": 1.8})
lamparas += [{"v": 13.0, "u": 25.5, "y": 5.1, "rango": 12.0, "intensidad": 1.4},
             {"v": 21.0, "u": 27.0, "y": 5.1, "rango": 10.0, "intensidad": 1.3},
             {"v": 21.8, "u": 34.0, "y": 5.1, "rango": 10.0, "intensidad": 1.3}]
for v, u in [(96.0, 27.0), (99.5, 33.5), (95.5, 21.0)]:
    lamparas.append({"v": v, "u": u, "y": 6.5, "rango": 15.0, "intensidad": 1.8})
for nombre, rects in LABS.items():
    for v0, v1, u0, u1 in rects:
        if (v1 - v0) * (u1 - u0) < 25:
            continue
        n = max(1, round((v1 - v0) / 8))
        for k in range(n):
            lamparas.append({"v": round(v0 + (v1 - v0) * (k + 0.5) / n, 2), "u": round((u0 + u1) / 2, 2),
                             "y": 6.4, "rango": 14.0, "intensidad": 1.6})
        for uu in ((u0 + u1) / 2 - 3.0, (u0 + u1) / 2 + 3.0):
            if u0 + 0.8 < uu < u1 - 0.8 and v1 - v0 > 4:
                luces.append({"a": [v0 + 1.2, round(uu, 2)], "b": [v1 - 1.2, round(uu, 2)], "y": 6.8})
for nombre, rects in OFICINAS.items():
    for v0, v1, u0, u1 in rects:
        if (v1 - v0) * (u1 - u0) < 25:
            continue
        n = max(1, round((v1 - v0) / 8))
        for k in range(n):
            lamparas.append({"v": round(v0 + (v1 - v0) * (k + 0.5) / n, 2), "u": round((u0 + u1) / 2, 2),
                             "y": CIELO_OFICINA - 0.35, "rango": 11.0, "intensidad": 1.25})

planta = {
    "version": 2,
    "fuente": "Plano de evacuación del piso 13, fotos y videos del recorrido (14 oct 2026); "
              "núcleo y muros estructurales del piso 9",
    "ancho": ANCHO, "largo": LARGO, "alturaTecho": TECHO, "alturaPanel": PANEL,
    "suelo": suelo, "pisos": pisos, "techos": techos,
    "muros": muros, "bloques": bloques, "escaleras": escaleras,
    "ascensores": ascensores, "torniquetes": [], "muebles": muebles, "rotulos": rotulos,
    "bandejas": bandejas, "luces": luces, "lamparas": lamparas,
}

if __name__ == "__main__":
    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    destino = os.path.join(raiz, "Assets", "MovU", "Models", "Piso13")
    os.makedirs(destino, exist_ok=True)
    with open(os.path.join(destino, "planta_piso13.json"), "w", encoding="utf-8") as f:
        json.dump(planta, f, ensure_ascii=False, indent=1)
    print(f"planta_piso13.json: {len(muros)} muros, {len(bloques)} bloques, {len(muebles)} muebles, "
          f"{len(techos)} techos, {len(lamparas)} lámparas.")
