#!/usr/bin/env python3
"""
pisos_planta.py — Plantas de los pisos 4 a 8 y 10 a 12, trazadas desde sus planos de evacuación.

Reemplaza a los pisos genéricos (núcleo del 9 con los salones cerrados) por plantas reales:
cada piso lleva sus espacios con el nombre del plano, puertas abiertas y un POI por espacio,
para que después se le puedan escribir misiones y se pueda llegar a todos lados.

Fuentes (21 de octubre de 2026): fotos de los planos de evacuación de los pisos 4, 5, 6, 7, 8,
10, 11 y 12 (vienen orientados como el del 13: ascensores abajo a la izquierda).

Qué es igual en todos (mismo edificio que el 9 y el 13):
  - Fachada, pasillo central, lobby, núcleo de ascensores, escalera principal, escalera de
    emergencia, ducto, cuartos eléctrico y de cableado, baños y cuarto de seguridad.
  - El salón del extremo del lado de las oficinas (v 1.1-8.9) y el ducto que lo sigue.
  - El tercer cuarto técnico es Baño accesible (con ducha) en los pisos 5, 7 y 11, y Aseo en el
    resto, según cada plano.
Qué cambia: los salones de los dos lados del pasillo (posiciones medidas en cada foto, ajustadas a
los muros estructurales cuando caían cerca). Casos especiales:
  - Piso 4: un paso abierto del pasillo a la fachada entre los baños y el almacén, y el Consultorio
    en un rincón de Pruebas Funcionales.
  - Piso 5: todo el lado de los laboratorios es un vacío (la doble altura de los laboratorios del
    piso 4): se ve oscuro a través del vidrio y no se entra.
  - Piso 7: una franja de cuartos chicos (ICP, Balanzas, Cromatografía...) contra el pasillo y los
    tres laboratorios de Calidad de Agua detrás, comunicados entre sí.
  - Piso 11: la Free Plaza, abierta al pasillo, con el ducto exento en medio.

Coordenadas: metros del diagrama del piso 9 (u a lo ancho, v a lo largo), como los pisos 3, 9 y 13.
Genera Assets/MovU/Models/Pisos/planta_pisoN.json y escribe en contenido_piso9.json las puertas y
los POIs de estos pisos (reemplaza los que hubiera; el resto del contenido no se toca).

Uso:   python3 Tools/pisos_planta.py          (desde la raíz del repositorio)
"""
import json, os, re, unicodedata

ANCHO, LARGO = 39.3, 106.6
TECHO = 5.6
CIELO_SALON = 3.3
CIELO_LOBBY = 4.2
V_IZQ, U_TOP, U_BOT = 1.1, 1.3, 38.2
U_C1, U_C2 = 18.7, 23.6
U_MID = (U_C1 + U_C2) / 2
U_TOPE = 17.7               # frente del salón del extremo (v < 17.6): el pasillo ahí es más ancho
V_DUCTO0, V_DUCTO1 = 17.6, 19.3
V_FIN_DER = 80.0
V_BANOS = 46.3
V_NUCLEO = 86.2

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
RUTA_CONTENIDO = os.path.join(RAIZ, "Assets", "MovU", "Resources", "MovU", "contenido_piso9.json")
DESTINO = os.path.join(RAIZ, "Assets", "MovU", "Models", "Pisos")

CATEGORIA = {"aula": "Salon", "lab": "Laboratorio", "oficina": "Oficina", "reunion": "Oficina",
             "consultorio": "Oficina", "coworking": "ZonaDeDescanso", "workspace": "ZonaDeDescanso",
             "plaza": "ZonaDeDescanso", "almacen": "Otro"}


def fachada_v(u):
    return 89.8 + (u - 1.6) * (104.0 - 89.8) / (37.2 - 1.6)


def pts(lista):
    return [{"v": round(a, 3), "u": round(b, 3)} for a, b in lista]


def slug(s):
    s = unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode().lower()
    s = re.sub(r"\(.*?\)", lambda m: m.group(0)[1:-1], s)
    return re.sub(r"[^a-z0-9]+", "_", s).strip("_")[:40]


class Piso:
    def __init__(self, n, accesible, extremo, tipo_extremo="oficina"):
        self.n, self.idx = n, n - 2               # 'piso' del contenido: el 3 es el 1, el 9 el 7
        self.accesible = accesible
        self.muros, self.bloques, self.muebles, self.rotulos = [], [], [], []
        self.pisos, self.techos, self.luces, self.lamparas, self.bandejas = [], [], [], [], []
        self.espacios = []                        # (nombre, tipo, rects, lado)
        self.puertas, self.pois = [], []
        self.extremo, self.tipo_extremo = extremo, tipo_extremo

    # --- piezas -------------------------------------------------------
    def muro(self, a, b, tipo, g=0.3, nombre="", lamas=False):
        m = {"a": [round(a[0], 3), round(a[1], 3)], "b": [round(b[0], 3), round(b[1], 3)],
             "tipo": tipo, "g": g, "nombre": nombre}
        if lamas:
            m["lamas"] = True
        self.muros.append(m)

    def bloque(self, v0, v1, u0, u1, tipo="concreto", nombre="", alto=TECHO):
        self.bloques.append({"v0": round(v0, 3), "v1": round(v1, 3), "u0": round(u0, 3), "u1": round(u1, 3),
                             "tipo": tipo, "nombre": nombre, "alto": alto})

    def mueble(self, tipo, v0, v1, u0, u1, alto, y0=0.0, solido=True):
        self.muebles.append({"tipo": tipo, "v0": round(v0, 3), "v1": round(v1, 3), "u0": round(u0, 3),
                             "u1": round(u1, 3), "alto": alto, "y0": y0, "solido": solido})

    def puerta(self, ident, poi, u, v, ancho, yaw=90, estilo="salon"):
        self.puertas.append({"id": ident, "poi": poi, "piso": self.idx, "u": round(u / ANCHO, 4),
                             "v": round(v / LARGO, 4), "yaw": yaw, "ancho": ancho, "estilo": estilo})

    def poi(self, ident, nombre, cat, u, v, ru, rv, importante=False):
        self.pois.append({"id": ident, "nombre": nombre, "categoria": cat, "piso": self.idx,
                          "u": round(u / ANCHO, 4), "v": round(v / LARGO, 4),
                          "rotuloU": round(ru / ANCHO, 4), "rotuloV": round(rv / LARGO, 4),
                          "radio": 1.5, "importante": importante, "rotulo": True})

    def frente(self, u, v0, v1, puertas, tipo, nombre):
        """Muro a lo largo de v en u, con vanos en 'puertas' [(v, ancho)] y madera a los lados de cada vano."""
        cur = v0
        for d, w in sorted(puertas):
            a, b = d - w / 2, d + w / 2
            ma = max(cur, a - 0.6)
            if ma - cur > 0.05:
                self.muro((cur, u), (ma, u), tipo, 0.3, nombre)
            if a - ma > 0.05:
                self.muro((ma, u), (a, u), "madera", 0.3, nombre)
            mb = min(v1, b + 0.6)
            if mb - b > 0.05:
                self.muro((b, u), (mb, u), "madera", 0.3, nombre)
            cur = mb
        if v1 - cur > 0.05:
            self.muro((cur, u), (v1, u), tipo, 0.3, nombre)

    def espacio(self, nombre, tipo, rects, lado):
        self.espacios.append((nombre, tipo, rects, lado))

    def sala_con_puertas(self, s, u_linea, puertas_aqui, hacia):
        """Puertas y POI de un salón. 'hacia' = +1 si el salón queda hacia +u desde el muro."""
        if s.get("tipo") in ("vacio", "pasillo"):
            return
        ident = f"p{self.n}_{s.get('id') or slug(s['nombre'])}"
        for k, (d, w) in enumerate(puertas_aqui):
            self.puerta(ident if k == 0 else f"{ident}_{k + 1}", ident if k == 0 else "", u_linea, d, w)
        if puertas_aqui and not s.get("sinPoi"):
            d = puertas_aqui[0][0]
            self.poi(ident, s["nombre"], CATEGORIA.get(s["tipo"], "Otro"), u_linea + hacia * 1.6, d, u_linea, d)


# =====================================================================
# Núcleo común (el del piso 9)
# =====================================================================
def nucleo(P):
    m = P.muro
    # Fachada
    m((V_IZQ, U_TOP), (fachada_v(U_TOP), U_TOP), "fachada", 0.35, "Fachada norte")
    m((V_IZQ, U_TOP), (V_IZQ, U_BOT), "fachada", 0.35, "Fachada del extremo")
    m((fachada_v(U_TOP), U_TOP), (fachada_v(U_BOT), U_BOT), "fachada", 0.35, "Fachada inclinada", lamas=True)
    m((V_IZQ, U_BOT), (fachada_v(U_BOT), U_BOT), "fachada", 0.35, "Fachada sur")

    # Salón del extremo (lado de las oficinas): frente en v 8.9, puerta al fondo del pasillo.
    nombre = P.extremo
    m((V_IZQ, U_TOPE), (8.9, U_TOPE), "blanco", 0.3, f"Muro {nombre}")
    m((8.9, U_TOPE), (8.9, 19.55), "madera", 0.3, f"Frente {nombre}")
    m((8.9, 22.0), (8.9, 28.3), "vitrina_negra", 0.3, f"Frente {nombre}")
    ident = f"p{P.n}_{slug(nombre)}"
    P.puerta(ident, ident, 20.77, 8.9, 2.45, yaw=0)
    P.poi(ident, nombre, CATEGORIA.get(P.tipo_extremo, "Oficina"), 21.15, 6.75, 20.78, 8.9)
    P.espacio(nombre, "extremo", [(V_IZQ, 8.9, U_TOPE, U_BOT)], "extremo")

    P.bloque(8.9, 19.7, 28.3, U_BOT, "negro_brillante", "Ducto (caja negra)")
    for a, b in [(17.2, 22.5)]:
        m((a, U_C2), (b, U_C2), "madera", 0.3, "Muro de lockers")

    # Cuartos técnicos y caja de la escalera de emergencia.
    for a, b in [(22.5, 24.4), (25.9, 28.5), (29.9, 31.65), (33.65, 36.3)]:
        m((a, U_C2), (b, U_C2), "madera", 0.3, "Frente de cuartos técnicos")
    for v in (22.5, 27.8, 30.6):
        m((v, U_C2), (v, 30.2), "concreto", 0.25, "Muro de cuartos técnicos")
    m((21.91, 30.2), (36.3, 30.2), "concreto", 0.3, "Fondo de cuartos | escalera")
    m((23.9, 30.2), (23.9, 32.3), "concreto", 0.2, "Cierre sobre el tramo oculto")
    P.bloque(23.95, 35.8, 30.35, 35.0, "concreto", "Macizo bajo el tramo oculto", alto=3.3)
    P.bloque(31.6, 35.8, 35.0, 38.05, "concreto", "Macizo bajo el rellano", alto=3.3)
    m((36.3, U_C2), (36.3, U_BOT), "concreto", 0.3, "Muro escalera | baños")

    # Baños.
    m((36.3, U_C2), (36.8, U_C2), "madera", 0.3, "Frente Baños")
    m((38.6, U_C2), (V_BANOS, U_C2), "madera", 0.3, "Frente Baños")
    m((38.8, U_C2), (38.8, 25.5), "blanco", 0.2, "Vestíbulo de baños")
    m((38.8, 27.3), (38.8, 32.0), "blanco", 0.2, "Vestíbulo de baños")
    m((38.8, 33.8), (38.8, U_BOT), "blanco", 0.2, "Vestíbulo de baños")
    m((38.8, 30.8), (V_BANOS, 30.8), "blanco", 0.2, "Baño mujeres | Baño hombres")
    m((V_BANOS, U_C2), (V_BANOS, U_BOT), "concreto", 0.3, "Muro Baños")
    for u0 in (24.0, 31.2):
        P.mueble("Meson", 39.6, 43.6, u0, u0 + 0.6, 0.86)
    for v in (40.4, 42.2, 44.0):
        P.mueble("Divisor", v - 0.03, v + 0.03, 27.8, 30.65, 2.0, y0=0.15, solido=False)
        P.mueble("Divisor", v - 0.03, v + 0.03, 35.0, 38.05, 2.0, y0=0.15, solido=False)
    for v in (39.6, 41.3, 43.1, 44.9):
        P.mueble("Sanitario", v - 0.25, v + 0.25, 29.8, 30.6, 0.42)
        P.mueble("Sanitario", v - 0.25, v + 0.25, 37.2, 38.0, 0.42)

    # Lado de los laboratorios, abajo: muro estructural, cuarto de seguridad y escalera principal.
    P.bloque(V_FIN_DER, 80.8, U_TOP, U_C1, "concreto", "Muro | Seguridad")
    P.bloque(80.8, 83.0, U_TOP, 14.6, "concreto", "Ducto junto al cuarto de seguridad")
    m((80.8, 14.6), (83.0, 14.6), "concreto", 0.25, "Fondo del cuarto de seguridad")
    m((80.8, U_C1), (81.2, U_C1), "madera", 0.3, "Frente Cuarto de seguridad")
    m((82.6, U_C1), (83.0, U_C1), "madera", 0.3, "Frente Cuarto de seguridad")
    m((83.0, U_TOP), (83.0, 19.0), "mostaza", 0.3, "Caja de la escalera principal (norte, muro mostaza)")
    P.bloque(86.55, 89.0, 8.0, 15.5, "concreto", "Columna del tramo oculto (escalera principal)")
    P.bloque(83.15, 89.0, 1.45, 8.0, "concreto", "Macizo bajo el rellano (escalera principal)", alto=3.0)
    m((89.0, 15.5), (fachada_v(15.5), 15.5), "concreto", 0.25, "Cierre junto a la escalera principal")
    m((83.0, 19.0), (84.0, 19.0), "madera", 0.3, "Caja de la escalera principal")
    m((86.0, 19.0), (92.6, 19.0), "madera", 0.3, "Caja de la escalera principal")
    m((92.6, 19.0), (fachada_v(19.0), 19.0), "vidrio", 0.2, "Vidrio de la escalera hacia el lobby", lamas=True)

    # Núcleo de ascensores y lobby.
    P.bloque(V_NUCLEO, 93.0, U_C2, U_BOT, "concreto", "Núcleo de ascensores")
    m((93.0, U_C2), (93.0, U_BOT), "granito", 0.12, "Granito del lobby")
    P.rotulos.append({"texto": "{n}", "v": 93.08, "u": 33.2, "h": 2.3, "mira": "+v", "tam": 9.0, "color": "8C7444"})
    LOBBY = [(93.0, 19.0), (fachada_v(19.0), 19.0), (fachada_v(U_BOT), U_BOT), (93.0, U_BOT)]
    P.pisos += [{"tipo": "Terrazo", "h": 0.008, "pts": pts(LOBBY)},
                {"tipo": "BandaGris", "h": 0.014, "pts": pts([(93.12, U_C2), (96.2, U_C2), (96.2, U_BOT), (93.12, U_BOT)])}]
    P.techos.append({"tipo": "TechoClaro", "h": CIELO_LOBBY, "nombre": "Lobby de ascensores", "pts": pts(LOBBY)})
    for nombre, rect in [("Cuarto de seguridad", (80.8, 83.0, 14.6, U_C1)),
                         ("Cuarto eléctrico", (22.5, 27.8, U_C2, 30.2)),
                         ("Cuarto de cableado", (27.8, 30.6, U_C2, 30.2)),
                         ("Baño accesible" if P.accesible else "Aseo", (30.6, 36.3, U_C2, 30.2)),
                         ("Baños", (36.3, V_BANOS, U_C2, U_BOT))]:
        P.espacio(nombre, "nucleo", [rect], "nucleo")

    # Puertas y POIs del núcleo (las mismas del 9 y del 13).
    n = P.n
    P.puerta(f"p{n}_escalera", f"p{n}_escalera_emergencia", 30.2, 20.81, 2.2, estilo="emergencia")
    P.puerta(f"p{n}_escalera_principal", f"p{n}_escalera_principal", 19.0, 85.0, 2.0, estilo="emergencia")
    P.puerta(f"p{n}_seguridad", "", U_C1, 81.9, 1.4)
    P.puerta(f"p{n}_electrico", "", U_C2, 25.15, 1.5)
    P.puerta(f"p{n}_cableado", "", U_C2, 29.2, 1.4)
    P.puerta(f"p{n}_bano_accesible" if P.accesible else f"p{n}_aseo",
             f"p{n}_bano_accesible" if P.accesible else "", U_C2, 32.65, 2.0)
    P.puerta(f"p{n}_banos", f"p{n}_banos", U_C2, 37.7, 1.8)
    P.poi(f"p{n}_banos", "Baños", "Bano", 25.0, 37.7, U_C2, 37.7)
    if P.accesible:
        P.poi(f"p{n}_bano_accesible", "Baño accesible", "Bano", 25.3, 32.65, U_C2, 32.65)
    P.poi(f"p{n}_escalera_emergencia", "Escalera de emergencia", "Escalera", 32.65, 20.84, 30.2, 20.81)
    P.poi(f"p{n}_escalera_principal", "Escalera principal", "Escalera", 17.4, 85.0, 19.0, 85.0)
    P.poi(f"p{n}_ascensores", "Ascensores", "Ascensor", 34.25, 97.6, 0.0, 0.0)

    # Pasillo: bandejas, luminarias y lámparas; lobby.
    P.bandejas += [{"a": [9.4, U_MID - 0.9], "b": [92.5, U_MID - 0.9], "y": 4.9, "ancho": 0.6},
                   {"a": [9.4, U_MID + 1.0], "b": [92.5, U_MID + 1.0], "y": 5.05, "ancho": 0.35}]
    P.luces += [{"a": [9.4, U_C1 + 0.55], "b": [92.0, U_C1 + 0.55], "y": 5.5},
                {"a": [9.4, U_C2 - 0.55], "b": [86.0, U_C2 - 0.55], "y": 5.5},
                {"a": [9.4, 25.5], "b": [17.0, 25.5], "y": 5.5}]
    for v in range(11, 92, 8):
        P.lamparas.append({"v": v, "u": U_MID, "y": 4.8, "rango": 13.0, "intensidad": 1.6})
    P.lamparas += [{"v": 13.0, "u": 25.5, "y": 4.8, "rango": 11.0, "intensidad": 1.3},
                   {"v": 21.0, "u": 27.0, "y": 4.8, "rango": 9.0, "intensidad": 1.2},
                   {"v": 21.8, "u": 34.0, "y": 4.8, "rango": 9.0, "intensidad": 1.2}]
    for v, u in [(96.0, 27.0), (99.5, 33.5), (95.5, 21.0)]:
        P.lamparas.append({"v": v, "u": u, "y": 3.9, "rango": 13.0, "intensidad": 1.6})
    for v in (95.0, 97.5, 100.0):
        for u in (25.0, 29.0, 33.0, 37.0):
            if v < fachada_v(u) - 0.8:
                P.luces.append({"a": [v - 0.15, u], "b": [v + 0.15, u], "y": CIELO_LOBBY - 0.01})


# =====================================================================
# Los dos lados del pasillo
# =====================================================================
def lado_derecho(P, salas):
    """Salones del lado de los laboratorios (u 1.3 .. 18.7), en orden de v.

    Cada sala: nombre, v0, v1, tipo, puertas [(v, ancho)], frente ('vitrina_negra', 'madera',
    'abierto'). Si entre dos salas hay hueco (el ducto de v 17.6-19.3), se rellena con concreto.
    """
    previa = None
    for s in salas:
        v0, v1 = s["v0"], s["v1"]
        if previa is not None:
            if previa["v1"] < v0 - 0.05:
                P.bloque(previa["v1"], v0, U_TOP, U_C1, "concreto", f"Ducto {previa['nombre']} | {s['nombre']}")
            else:
                u_fin = U_TOPE if v0 < V_DUCTO0 else U_C1
                P.muro((v0, U_TOP), (v0, u_fin), "blanco", 0.3, f"Muro {previa['nombre']} | {s['nombre']}")
        previa = s

        tipo_frente = s.get("frente", "vitrina_negra")
        puertas = s.get("puertas", [])
        arriba = [p for p in puertas if p[0] < V_DUCTO0]
        abajo = [p for p in puertas if p[0] >= V_DUCTO0]
        nombre = f"Frente {s['nombre']}"
        if tipo_frente == "abierto":
            for v in s.get("columnas", []):
                u = U_TOPE if v < V_DUCTO0 else U_C1
                P.bloque(v - 0.25, v + 0.25, u - 0.5, u, "concreto", f"Columna {s['nombre']}")
        else:
            if v0 < V_DUCTO0:
                P.frente(U_TOPE, max(v0, 8.9), min(v1, V_DUCTO0), arriba, tipo_frente, nombre)
            if v1 > V_DUCTO0:
                P.frente(U_C1, max(v0, V_DUCTO0), v1, abajo, tipo_frente, nombre)
            if v0 < V_DUCTO0 < v1:
                P.muro((V_DUCTO0, U_TOPE), (V_DUCTO0, U_C1), tipo_frente, 0.3, nombre)
        P.sala_con_puertas(s, U_TOPE, arriba, -1)
        P.sala_con_puertas(s, U_C1, abajo, -1)
        rects = []
        if v0 < V_DUCTO0:
            rects.append((v0, min(v1, V_DUCTO0), U_TOP, U_TOPE))
        if v1 > V_DUCTO0:
            rects.append((max(v0, V_DUCTO0), v1, U_TOP, U_C1))
        P.espacio(s["nombre"], s["tipo"], s.get("rects", rects), "der")


def lado_izquierdo(P, salas):
    """Salones del lado de las oficinas entre los baños (v 46.3) y los ascensores (v 86.2)."""
    for i, s in enumerate(salas):
        v0, v1 = s["v0"], s["v1"]
        if i > 0:
            P.muro((v0, U_C2), (v0, U_BOT), "blanco", 0.3, f"Muro {salas[i - 1]['nombre']} | {s['nombre']}")
        if s["tipo"] != "pasillo":
            P.frente(U_C2, v0, s.get("frenteHasta", v1), s.get("puertas", []), s.get("frente", "vitrina_negra"),
                     f"Frente {s['nombre']}")
        P.sala_con_puertas(s, U_C2, s.get("puertas", []), +1)
        if s["tipo"] != "pasillo":
            P.espacio(s["nombre"], s["tipo"], s.get("rects", [(v0, v1, U_C2, U_BOT)]), "izq")
    assert abs(salas[0]["v0"] - V_BANOS) < 0.01 and abs(salas[-1]["v1"] - V_NUCLEO) < 0.01


# =====================================================================
# Mobiliario, cielos rasos y luces de cada espacio
# =====================================================================
def fila(P, v0, v1, u, mesa="Mesa", hueco=1.6):
    medio = (v0 + v1) / 2
    for a, b in ((v0, medio - hueco / 2), (medio + hueco / 2, v1)):
        if b - a < 1.5:
            continue
        P.mueble(mesa, a, b, u - 0.4, u + 0.4, 0.75 if mesa == "Mesa" else 0.9)
        n = max(1, int((b - a) / 1.5))
        for k in range(n):
            c = a + (b - a) * (k + 0.5) / n
            if mesa == "Mesa":
                P.mueble("Monitor", c - 0.28, c + 0.28, u - 0.1, u - 0.04, 0.38, y0=0.78, solido=False)
            su = u + 0.75
            P.mueble("Silla", c - 0.24, c + 0.24, su - 0.24, su + 0.24, 0.08, y0=0.42, solido=False)
            P.mueble("Silla", c - 0.24, c + 0.24, su + 0.19, su + 0.25, 0.5, y0=0.5, solido=False)
            P.mueble("Silla", c - 0.03, c + 0.03, su - 0.03, su + 0.03, 0.42, solido=False)


def silla_suelta(P, v, u, eje, sentido):
    """Silla junto a una mesa suelta: asiento, espaldar (del lado de afuera) y pata."""
    P.mueble("Silla", v - 0.24, v + 0.24, u - 0.24, u + 0.24, 0.08, y0=0.42, solido=False)
    if eje == "u":
        e = u + sentido * 0.22
        P.mueble("Silla", v - 0.24, v + 0.24, e - 0.03, e + 0.03, 0.5, y0=0.5, solido=False)
    else:
        e = v + sentido * 0.22
        P.mueble("Silla", e - 0.03, e + 0.03, u - 0.24, u + 0.24, 0.5, y0=0.5, solido=False)
    P.mueble("Silla", v - 0.03, v + 0.03, u - 0.03, u + 0.03, 0.42, solido=False)


def filas_u(u0, u1, lado):
    """Dónde van las filas de puestos: lejos del pasillo (3,5 m libres junto a la puerta)."""
    if lado == "der":
        return [u for u in (u0 + 2.5 + 4.8 * k for k in range(4)) if u <= u1 - 4.6]
    return [u for u in (u0 + 4.0 + 4.2 * k for k in range(4)) if u <= u1 - 2.0]


def amueblar(P, nombre, tipo, rects, lado):
    if tipo in ("nucleo", "vacio", "pasillo"):
        return
    if lado == "extremo":                      # contra la ventana del extremo, más una mesa al centro
        P.mueble("Mesa", 1.5, 2.5, 19.5, 36.8, 0.75)
        for k in range(10):
            c = 20.3 + k * 1.65
            P.mueble("Monitor", 1.95, 2.01, c - 0.28, c + 0.28, 0.38, y0=0.78, solido=False)
        P.mueble("Vitrina" if tipo == "lab" else "Mesa", 4.3, 5.5, 24.5, 34.5, 0.9 if tipo == "lab" else 0.75)
        return
    for v0, v1, u0, u1 in rects:
        if v1 - v0 < 3.0 or u1 - u0 < 3.0:
            continue
        if tipo in ("aula", "lab"):
            if u1 - u0 < 7.5:                     # cuarto chico: un mesón contra el muro del fondo
                P.mueble("Meson", v0 + 0.8, v1 - 0.8, u0 + 0.2, u0 + 0.9, 0.9)
                continue
            for u in filas_u(u0, u1, lado):
                fila(P, v0 + 1.4, v1 - 1.4, u, "Mesa" if tipo == "aula" else "Meson")
        elif tipo == "oficina" or tipo == "consultorio":
            uv = (u0 + 0.3, u0 + 1.4) if lado == "der" else (u1 - 1.7, u1 - 0.6)
            P.mueble("Mesa", v0 + 0.9, v1 - 0.9, uv[0], uv[1], 0.75)
            n = max(1, int((v1 - v0 - 1.8) / 1.6))
            for k in range(n):
                c = v0 + 0.9 + (v1 - v0 - 1.8) * (k + 0.5) / n
                um = uv[0] + 0.25 if lado == "der" else uv[1] - 0.31
                P.mueble("Monitor", c - 0.28, c + 0.28, um, um + 0.06, 0.38, y0=0.78, solido=False)
            if v1 - v0 > 9 and u1 - u0 > 9:
                cu = (u0 + u1) / 2 + (-1.0 if lado == "der" else 1.0)
                P.mueble("Mesa", (v0 + v1) / 2 - 2.0, (v0 + v1) / 2 + 2.0, cu - 0.7, cu + 0.7, 0.75)
        elif tipo == "reunion":
            cu = (u0 + u1) / 2
            P.mueble("Mesa", v0 + 2.0, v1 - 2.0, cu - 0.8, cu + 0.8, 0.75)
        elif tipo in ("coworking", "workspace", "plaza"):
            lejos = 3.5
            ua, ub = (u0 + 1.8, u1 - lejos) if lado == "der" else (u0 + lejos, u1 - 1.8)
            paso = 4.2 if tipo != "plaza" else 5.5
            v = v0 + 2.2
            while v <= v1 - 2.2:
                u = ua + 0.8
                while u <= ub - 0.8:
                    P.mueble("Mesa", v - 0.8, v + 0.8, u - 0.8, u + 0.8, 0.75)
                    silla_suelta(P, v, u - 1.15, "u", -1)
                    silla_suelta(P, v, u + 1.15, "u", +1)
                    silla_suelta(P, v - 1.15, u, "v", -1)
                    silla_suelta(P, v + 1.15, u, "v", +1)
                    u += paso
                v += paso
        elif tipo == "almacen":
            ua, ub = (u0 + 1.0, u1 - 4.0) if lado == "der" else (u0 + 4.0, u1 - 1.0)
            u = ua
            while u + 0.6 <= ub:
                v = v0 + 1.6
                while v + 3.0 <= v1 - 1.6:
                    P.mueble("Vitrina", v, min(v + 6.0, v1 - 1.6), u, u + 0.6, 2.0)
                    v += 7.6
                u += 2.8


def cielos_y_luces(P):
    for nombre, tipo, rects, lado in P.espacios:
        if tipo in ("vacio", "pasillo"):
            continue
        for v0, v1, u0, u1 in rects:
            P.techos.append({"tipo": "TechoClaro", "h": CIELO_SALON, "nombre": nombre,
                             "pts": pts([(v0, u0), (v1, u0), (v1, u1), (v0, u1)])})
            if (v1 - v0) * (u1 - u0) < 25:
                continue
            n = max(1, round((v1 - v0) / 8))
            for k in range(n):
                P.lamparas.append({"v": round(v0 + (v1 - v0) * (k + 0.5) / n, 2), "u": round((u0 + u1) / 2, 2),
                                   "y": CIELO_SALON - 0.35, "rango": 11.0, "intensidad": 1.25})
            for uu in ((u0 + u1) / 2 - 3.0, (u0 + u1) / 2 + 3.0):
                if u0 + 0.8 < uu < u1 - 0.8 and v1 - v0 > 4:
                    P.luces.append({"a": [v0 + 1.2, round(uu, 2)], "b": [v1 - 1.2, round(uu, 2)],
                                    "y": CIELO_SALON - 0.01})
        amueblar(P, nombre, tipo, rects, lado)


# =====================================================================
# Techo a la vista (la losa), con el hueco de la escalera de emergencia
# =====================================================================
def recortar(poligono, v0, v1, u0, u1):
    def corte(ps, dentro, cruce):
        salida = []
        for i in range(len(ps)):
            a, b = ps[i], ps[(i + 1) % len(ps)]
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


SUELO = [(V_IZQ, U_TOP), (fachada_v(U_TOP), U_TOP), (fachada_v(U_BOT), U_BOT), (V_IZQ, U_BOT)]


def losa(P, esc):
    huecos = [
        (esc["tramo"]["v0"] * LARGO, esc["rellano"]["v0"] * LARGO, esc["ocultoU0"] * ANCHO, esc["tramo"]["u1"] * ANCHO),
        (esc["rellano"]["v0"] * LARGO, esc["rellano"]["v1"] * LARGO, esc["rellano"]["u0"] * ANCHO, esc["rellano"]["u1"] * ANCHO),
    ]
    cortes_v = sorted({-1.0, 200.0} | {h[0] for h in huecos} | {h[1] for h in huecos})
    cortes_u = sorted({-1.0, 100.0} | {h[2] for h in huecos} | {h[3] for h in huecos})
    for i in range(len(cortes_v) - 1):
        for j in range(len(cortes_u) - 1):
            v0, v1, u0, u1 = cortes_v[i], cortes_v[i + 1], cortes_u[j], cortes_u[j + 1]
            cv, cu = (v0 + v1) / 2, (u0 + u1) / 2
            if any(h[0] <= cv <= h[1] and h[2] <= cu <= h[3] for h in huecos):
                continue
            pieza = recortar(SUELO, v0, v1, u0, u1)
            if len(pieza) >= 3 and area(pieza) > 0.5:
                P.techos.insert(0, {"tipo": "TechoOscuro", "h": TECHO, "pts": pts(pieza)})


# =====================================================================
# Cada piso
# =====================================================================
def sala(nombre, v0, v1, tipo, puertas=(), **extra):
    d = {"nombre": nombre, "v0": v0, "v1": v1, "tipo": tipo, "puertas": [(p, 2.4) if isinstance(p, (int, float)) else p for p in puertas]}
    d.update(extra)
    return d


def piso4():
    P = Piso(4, False, "Pruebas de Seguridad", "lab")
    nucleo(P)
    lado_derecho(P, [
        sala("Laboratorio de Radio Frecuencia", V_IZQ, 17.6, "lab", [15.4]),
        sala("Simulación Trabajo Oficinas", 19.3, 27.8, "workspace", [21.8]),
        sala("Simulación Tareas Industriales (Módulo A)", 27.8, 54.5, "lab", [30.4, 51.8]),
        sala("Simulación Tareas Industriales (Módulo B)", 54.5, V_FIN_DER, "lab", [57.2, 77.4]),
    ])
    lado_izquierdo(P, [
        sala("Paso a la fachada", V_BANOS, 51.2, "pasillo"),
        sala("Almacén de Equipos Tareas Funcionales", 51.2, 58.5, "almacen", [54.8]),
        sala("Pruebas Funcionales", 58.5, V_NUCLEO, "lab", [61.2, 78.6], frenteHasta=82.0,
             rects=[(58.5, 82.0, U_C2, U_BOT), (82.0, V_NUCLEO, 29.2, U_BOT)]),
    ])
    # El Consultorio, en el rincón de Pruebas Funcionales junto a los ascensores.
    P.muro((82.0, U_C2), (82.0, 29.2), "blanco", 0.25, "Muro Pruebas Funcionales | Consultorio")
    P.muro((82.0, 29.2), (V_NUCLEO, 29.2), "blanco", 0.25, "Muro Pruebas Funcionales | Consultorio")
    P.frente(U_C2, 82.0, V_NUCLEO, [(84.0, 1.6)], "madera", "Frente Consultorio")
    c = {"nombre": "Consultorio", "tipo": "consultorio", "id": "consultorio"}
    P.sala_con_puertas(c, U_C2, [(84.0, 1.6)], +1)
    P.espacio("Consultorio", "consultorio", [(82.0, V_NUCLEO, U_C2, 29.2)], "izq")
    return P


def piso5():
    P = Piso(5, True, "Dirección Laboratorio Ingeniería Civil")
    nucleo(P)
    # Todo el lado de los laboratorios es el vacío de la doble altura del piso 4.
    lado_derecho(P, [
        sala("Vacío (doble altura del piso 4)", V_IZQ, 17.6, "vacio"),
        sala("Vacío (doble altura del piso 4) ", 19.3, V_FIN_DER, "vacio"),
    ])
    P.pisos.append({"tipo": "Negro", "h": 0.01, "pts": pts([(V_IZQ + 0.2, U_TOP + 0.2), (17.4, U_TOP + 0.2), (17.4, 17.5), (V_IZQ + 0.2, 17.5)])})
    P.pisos.append({"tipo": "Negro", "h": 0.01, "pts": pts([(19.5, U_TOP + 0.2), (79.8, U_TOP + 0.2), (79.8, 18.5), (19.5, 18.5)])})
    lado_izquierdo(P, [
        sala("Simulación Corporativa", V_BANOS, 54.5, "aula", [52.6]),
        sala("Observación", 54.5, 62.5, "oficina", [56.4]),
        sala("Trabajo Colaborativo", 62.5, V_NUCLEO, "workspace", [64.6, 80.6]),
    ])
    return P


def piso6():
    P = Piso(6, False, "Laboratorio de Rocas", "lab")
    nucleo(P)
    lado_derecho(P, [
        sala("Ensayos Dinámicos Mezclas Asfálticas", V_IZQ, 17.6, "lab", [15.4]),
        sala("Reología de Asfaltos", 19.3, 28.0, "lab", [25.0]),
        sala("Fatiga en Mezclas", 28.0, 41.0, "lab", [30.2]),
        sala("Envejecimiento de Asfaltos", 41.0, 50.3, "lab", [47.6]),
        sala("Química de Asfaltos", 50.3, 62.5, "lab", [53.0]),
        sala("Caracterización de Asfaltos", 62.5, V_FIN_DER, "lab", [77.4]),
    ])
    lado_izquierdo(P, [
        sala("Docencia Geotecnia", V_BANOS, 62.5, "aula", [60.3]),
        sala("Investigación Geotecnia", 62.5, V_NUCLEO, "lab", [64.7]),
    ])
    return P


def piso7():
    P = Piso(7, True, "Calidad de Aguas Almacén", "almacen")
    nucleo(P)
    lado_derecho(P, [sala("Docencia Calidad de Aguas", V_IZQ, 17.6, "lab", [15.4])])
    P.bloque(V_DUCTO0, V_DUCTO1, U_TOP, U_C1, "concreto", "Ducto Docencia | laboratorios")

    # Franja de cuartos chicos contra el pasillo (u 12.2 .. 18.7), con puerta al pasillo.
    U_IN = 12.2
    chicos = [("ICP", 21.3, 27.8), ("Balanzas", 27.8, 31.7), ("Cuarto Análisis de Carbono Total", 31.7, 38.2),
              ("Cromatografía de Gases", 38.2, 45.3), ("Absorción Atómica", 45.3, 53.0),
              ("Microbiología", 53.0, 59.0), ("Hornos", 59.0, 69.8), ("Bioreactor", 69.8, 76.3)]
    P.muro((21.3, U_IN), (76.3, U_IN), "blanco", 0.25, "Fondo de los cuartos chicos")
    P.muro((21.3, U_IN), (21.3, U_C1), "blanco", 0.25, "Muro paso | ICP")
    P.muro((76.3, U_IN), (76.3, U_C1), "blanco", 0.25, "Muro Bioreactor | paso")
    # Pasos al pasillo de los laboratorios de los extremos (v 19.3-21.3 y 76.3-80.0).
    for nombre, v0, v1 in chicos:
        if v0 > 21.3:
            P.muro((v0, U_IN), (v0, U_C1), "blanco", 0.25, f"Muro | {nombre}")
        d, w = (v0 + v1) / 2, 1.8          # 1,4 m no lo cruza el NavMesh
        P.frente(U_C1, v0, v1, [(d, w)], "vitrina_negra", f"Frente {nombre}")
        s = {"nombre": nombre, "tipo": "lab"}
        P.sala_con_puertas(s, U_C1, [(d, w)], -1)
        P.espacio(nombre, "lab", [(v0, v1, U_IN, U_C1)], "der")
    # Los tres laboratorios de Calidad de Agua (u 1.3 .. 12.2), comunicados por dentro.
    labs = [("Laboratorio Calidad de Agua (Aguas Naturales)", V_DUCTO1, 35.3, "aguas_naturales"),
            ("Laboratorio Calidad de Agua", 35.3, 61.0, "calidad_de_agua"),
            ("Laboratorio Calidad de Agua (Aguas Residuales)", 61.0, V_FIN_DER, "aguas_residuales")]
    for v in (35.3, 61.0):
        P.muro((v, U_TOP), (v, 9.9), "blanco", 0.3, "Muro entre laboratorios de Calidad de Agua")
        P.muro((v, 11.5), (v, U_IN), "blanco", 0.3, "Muro entre laboratorios de Calidad de Agua")
    n = P.n
    # Entradas: por el paso de arriba (Aguas Naturales) y por el de abajo (Aguas Residuales).
    P.puerta(f"p{n}_aguas_naturales", f"p{n}_aguas_naturales", U_C1, 20.3, 1.8)
    P.poi(f"p{n}_aguas_naturales", labs[0][0], "Laboratorio", 11.0, 20.3, U_C1, 20.3)
    P.puerta(f"p{n}_aguas_residuales", f"p{n}_aguas_residuales", U_C1, 78.15, 3.0)
    P.poi(f"p{n}_aguas_residuales", labs[2][0], "Laboratorio", 11.0, 78.15, U_C1, 78.15)
    P.poi(f"p{n}_calidad_de_agua", labs[1][0], "Laboratorio", 10.7, 36.5, 10.7, 35.3)
    for nombre, v0, v1, _ in labs:
        P.espacio(nombre, "lab", [(v0, v1, U_TOP, U_IN)], "der")
    P.espacio("Paso a Aguas Naturales", "pasillo", [], "der")
    lado_izquierdo(P, [
        sala("Workspace (Módulo B)", V_BANOS, 65.0, "workspace", [63.0]),
        sala("Workspace (Módulo A)", 65.0, V_NUCLEO, "workspace", [67.0]),
    ])
    return P


def piso8():
    P = Piso(8, False, "Gestión Laboratorio Ingeniería Civil")
    nucleo(P)
    lado_derecho(P, [
        sala("Programación", V_IZQ, 17.6, "aula", [15.4]),
        sala("Arquitectura Empresarial", 19.3, 35.0, "aula", [33.0]),
        sala("Sistemas Heterogéneos", 35.0, 51.0, "aula", [37.0]),
        sala("Laboratorio Sistemas Distributivos", 51.0, 65.5, "aula", [63.5]),
        sala("Laboratorio Alan Turing", 65.5, V_FIN_DER, "aula", [67.5]),
    ])
    lado_izquierdo(P, [
        sala("Manufactura y Mediciones Especializadas Electrónicas", V_BANOS, 55.0, "lab", [53.0],
             id="manufactura_y_mediciones"),
        sala("Investigación en Redes", 55.0, 72.0, "lab", [57.0]),
        sala("Investigación en Redes (I+D+I)", 72.0, V_NUCLEO, "lab", [81.6], id="investigacion_en_redes_idi"),
    ])
    return P


def piso10():
    P = Piso(10, False, "Dirección General de Laboratorios")
    nucleo(P)
    lado_derecho(P, [
        sala("Coworking 2", V_IZQ, 17.6, "coworking", [15.4]),
        sala("Aula de Cómputo 7", 19.3, 34.5, "aula", [32.5]),
        sala("Aula de Cómputo 6", 34.5, 49.5, "aula", [36.5]),
        sala("Aula de Cómputo 5", 49.5, 65.5, "aula", [63.5]),
        sala("Aula de Cómputo 4", 65.5, V_FIN_DER, "aula", [67.5]),
    ])
    lado_izquierdo(P, [
        sala("Workspace 2A", V_BANOS, 65.0, "workspace", [63.0]),
        sala("Workspace 2B", 65.0, V_NUCLEO, "workspace", [67.0]),
    ])
    return P


def piso11():
    P = Piso(11, True, "Sala de Reuniones", "reunion")
    nucleo(P)
    lado_derecho(P, [
        sala("Free Plaza", V_IZQ, 65.5, "plaza", [], frente="abierto",
             columnas=[26.0, 34.0, 42.0, 50.0, 58.0],
             rects=[(V_IZQ, V_DUCTO0, U_TOP, U_TOPE), (V_DUCTO0, 65.5, U_TOP, U_C1)]),
        sala("Laboratorio de Geoinformática", 65.5, 77.5, "aula", [67.5]),
    ])
    P.bloque(77.5, V_FIN_DER, U_TOP, U_C1, "concreto", "Ducto junto a Geoinformática")
    # La plaza sigue de largo: el ducto queda exento y un tabique (la línea del medio del
    # plano) parte el espacio, con paso por los dos extremos. Va de piso a techo: como
    # banca baja, el NavMesh la tomaba por escalón y el recorrido automático se estrellaba.
    P.bloque(16.3, 18.6, 5.2, 17.2, "negro_brillante", "Ducto exento de la Free Plaza")
    P.muro((43.9, 4.6), (43.9, 16.4), "madera", 0.2, "Tabique de la Free Plaza")
    # La columna del quiebre del pasillo (v 17.6), donde no hay muro.
    P.bloque(17.35, 17.85, U_TOPE, U_C1, "concreto", "Columna Free Plaza")
    lado_izquierdo(P, [
        sala("Laboratorio de Control Automático", V_BANOS, 65.0, "lab", [63.0]),
        sala("Conversión de Energía", 65.0, V_NUCLEO, "lab", [67.0]),
    ])
    n = P.n
    P.poi(f"p{n}_free_plaza", "Free Plaza", "ZonaDeDescanso", 15.0, 38.0, U_C1, 38.0)
    return P


def piso12():
    P = Piso(12, False, "Director General de Laboratorio")
    nucleo(P)
    lado_derecho(P, [
        sala("Oficina Gestión Técnica 4", V_IZQ, 17.6, "oficina", [15.4]),
        sala("Almacén de Instrumentación Electrónica", 19.3, V_FIN_DER, "almacen", [21.4, 77.6]),
    ])
    lado_izquierdo(P, [
        sala("Workspace 3B", V_BANOS, 66.0, "workspace", [64.0]),
        sala("Workspace 3A", 66.0, V_NUCLEO, "workspace", [68.0]),
    ])
    return P


PISOS = [piso4, piso5, piso6, piso7, piso8, piso10, piso11, piso12]


# =====================================================================
# Revisión (lo mismo que miran las pruebas de Unity)
# =====================================================================
def dist_seg(p, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = dx * dx + dy * dy
    t = 0 if L == 0 else max(0, min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / L))
    return ((p[0] - a[0] - t * dx) ** 2 + (p[1] - a[1] - t * dy) ** 2) ** 0.5


def choca(P, v, u, r=0.35):
    for m in P.muros:
        if dist_seg((v, u), m["a"], m["b"]) < m["g"] / 2 + r:
            return "muro " + m["nombre"]
    for c in P.bloques + [m for m in P.muebles if m["solido"]]:
        if c["v0"] - r < v < c["v1"] + r and c["u0"] - r < u < c["u1"] + r:
            return c.get("nombre") or c["tipo"]
    return None


def revisar(P):
    fallos = []
    for p in P.pois:
        if p["rotuloU"] == 0:
            continue
        c = choca(P, p["v"] * LARGO, p["u"] * ANCHO)
        if c:
            fallos.append(f"POI {p['id']} choca con {c}")
    for d in P.puertas:
        cv, cu = d["v"] * LARGO, d["u"] * ANCHO
        for m in P.muros:
            if dist_seg((cv, cu), m["a"], m["b"]) < 0.2:
                fallos.append(f"Puerta {d['id']} sobre el muro {m['nombre']}")
    return fallos


if __name__ == "__main__":
    with open(RUTA_CONTENIDO, encoding="utf-8") as f:
        contenido = json.load(f)
    os.makedirs(DESTINO, exist_ok=True)
    indices = set()
    nuevas_puertas, nuevos_pois = [], []
    for hacer in PISOS:
        P = hacer()
        losa(P, contenido["escalera"])
        cielos_y_luces(P)
        fallos = revisar(P)
        if fallos:
            raise SystemExit(f"Piso {P.n}:\n  " + "\n  ".join(fallos))
        planta = {
            "version": 2,
            "fuente": f"Plano de evacuación del piso {P.n} (foto del 21 oct 2026) sobre el núcleo del piso 9",
            "ancho": ANCHO, "largo": LARGO, "alturaTecho": TECHO,
            "suelo": pts(SUELO), "pisos": P.pisos, "techos": P.techos,
            "muros": P.muros, "bloques": P.bloques, "escaleras": [],
            "ascensores": [{"v": 93.07, "u": u, "ancho": 2.4} for u in (26.6, 31.0, 35.4)],
            "torniquetes": [], "muebles": P.muebles, "rotulos": P.rotulos,
            "bandejas": P.bandejas, "luces": P.luces, "lamparas": P.lamparas,
        }
        with open(os.path.join(DESTINO, f"planta_piso{P.n}.json"), "w", encoding="utf-8") as f:
            json.dump(planta, f, ensure_ascii=False, indent=1)
        indices.add(P.idx)
        nuevas_puertas += P.puertas
        nuevos_pois += P.pois
        salas = [p["nombre"] for p in P.pois if p["categoria"] not in ("Bano", "Escalera", "Ascensor")]
        print(f"piso {P.n:>2}: {len(P.muros)} muros, {len(P.bloques)} bloques, {len(P.muebles)} muebles, "
              f"{len(P.puertas)} puertas, {len(P.pois)} POIs ({len(salas)} espacios)")

    contenido["puertas"] = [d for d in contenido["puertas"] if d["piso"] not in indices] + nuevas_puertas
    contenido["pois"] = [p for p in contenido["pois"] if p.get("piso") not in indices] + nuevos_pois
    with open(RUTA_CONTENIDO, "w", encoding="utf-8") as f:
        json.dump(contenido, f, ensure_ascii=False, indent=2)
    print(f"contenido_piso9.json: {len(contenido['pois'])} POIs, {len(contenido['puertas'])} puertas en total.")
