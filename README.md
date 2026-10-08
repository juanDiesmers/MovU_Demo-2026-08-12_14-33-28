# MovU — Videojuego serio de orientación 3D

Proyecto de grado · Ingeniería de Sistemas · Pontificia Universidad Javeriana
Director: Ing. Leonardo Flórez Valencia
Equipo: Damián Rey (diseño de juego y misiones) · Juan Diego Palacios (QA y métricas) · Sergio Parra (desarrollo)

MovU es un juego de orientación en primera persona ambientado en el edificio de Ingeniería.
El jugador recorre el edificio buscando un objetivo, con una flecha de guía que se puede
apagar o encender, mientras el juego registra en CSV qué tan eficiente fue su recorrido.
Ese registro es la evidencia experimental de la tesis.

---

## 1. Estado actual (8 de octubre de 2026)

| Parte | Estado |
|---|---|
| Mecánicas base (misiones, flecha de guía, métricas CSV, récord) | ✅ Funcionando en `DemoMaze` |
| Personaje en 1ª / 3ª persona (`CameraRig`) | ✅ |
| Planta caminable a partir del modelo de Meshy | ✅ |
| Shader estilizado low-poly sin UVs | ✅ |
| Edificio de 3 pisos + ascensor + carga por piso | ⚠️ Compila; **falta abrirlo en Unity y correr `MovU > Preparar todo`** |
| POIs del piso 9 como datos (`contenido_piso9.json`) | ⚠️ Escrito y compilado; sin probar en Unity |
| Dos misiones encadenadas sobre los POIs de DTI | ⚠️ Escrito y compilado; sin probar en Unity |
| NPC (personal de DTI, profesores, vigilante, aseo, estudiantes, visitantes) | ⚠️ Escrito y compilado; sin probar en Unity |
| NavMesh de los tres pisos, horneado desde un menú | ⚠️ Escrito y compilado; sin probar en Unity |
| Flecha de guía entre pisos (lleva al ascensor) | ⚠️ Escrito y compilado; sin probar en Unity |
| Métricas por misión: participante, cambios de piso, pisos equivocados, consultas a NPC | ⚠️ Escrito y compilado; sin probar en Unity |
| Pruebas automáticas (Test Runner, modo edición) | ⚠️ Escritas y compiladas; sin correr en Unity |
| Occlusion Culling horneado | ❌ Pendiente (ya hay menú: `MovU > Rendimiento`) |
| Menú principal, opciones, pantalla de carga, guardado de progreso | ❌ Pendiente |
| Escala definitiva del edificio | ⚠️ Por decidir (hoy 56x horizontal / 45x vertical) |

**"Compila" quiere decir esto y nada más:** el 8 de octubre todo `Assets/MovU` se compiló
fuera de Unity con el compilador de C# contra las DLL de Unity 6000.3.21f1 de este equipo
(`UnityEngine`, `UnityEditor`, Input System, AI Navigation, TextMeshPro, URP), en las tres
configuraciones (ejecutable, editor y scripts de `Editor/`), sin errores. Eso descarta los
errores de sintaxis y de API, que era el riesgo R10. **No** dice que el juego se comporte
bien: nadie ha dado Play todavía. Lo primero al abrir el proyecto sigue siendo probar.

En este equipo la última vez que se abrió Unity fue el 11 de septiembre; el edificio de ese
día quedó en `Assets/meshi.unity` y `Assets/MovU/Scenes/Edificio.unity` todavía no existe.
La crea `MovU > Preparar todo`.

Último commit: `Readme` (23 de septiembre). Lo del 8 de octubre está sin commitear.

### Decisiones ya tomadas (no volver a discutirlas sin motivo)

- **El mapa jugable es el modelo ORIGINAL de Meshy**, no el reconstruido. Se conserva su
  geometría orgánica a la altura de los ojos; los muros cortos se tapan por encima con una
  malla aparte (`RellenoCorona.obj`).
- **Escala no uniforme a propósito:** 56x en horizontal, 45x en vertical. Escalar todo a 56x
  dejaría pasillos de 7 m de alto — catedral, no edificio universitario.
- **Tres pisos = el mismo piso apilado tres veces**, porque todavía no hay planos de los otros
  niveles. Se cambia de piso con un cubo interactuable que abre un panel.
- **Cada piso es autónomo.** `FloorManager` apaga los pisos que nadie está viendo, así que un
  piso no puede depender de que el vecino esté cargado (por eso cada piso trae su propia losa
  de techo). Cualquier cosa que se añada después tiene que cumplir esta regla.
- **La escena construida es la fuente de verdad**, no los menús. `Assets/MovU/Scenes/Edificio.unity`
  se versiona y se edita como cualquier escena; los menús quedan para *regenerar* la geometría
  cuando cambie el modelo o la escala. Ver la sección 4.

---

## 2. Requisitos

| Herramienta | Versión | Para qué |
|---|---|---|
| Unity | **6000.3.21f1** (Unity 6.3 LTS) | El proyecto. No abrirlo con otra versión sin avisar al equipo |
| URP | 17.3.0 (ya en `manifest.json`) | Render pipeline |
| Input System | 1.20.0 — **el nuevo**, no el viejo | Todo el input está escrito con `Keyboard.current` |
| AI Navigation | 2.0.14 | `NavMeshSurface` (el sistema nuevo, no "Navigation Static") |
| Git LFS | cualquiera reciente | `*.obj`, `*.fbx`, `*.blend`… están en LFS (ver `.gitattributes`) |
| Python 3 + `numpy` | 3.10+ | Scripts de `Tools/` que generan las mallas |
| Blender (opcional) | 4.x | Solo para `blender_pulir_plano.py` |

**Dónde clonar:** en una carpeta que **no** esté sincronizada con OneDrive / Drive / iCloud.
La carpeta `Library/` de Unity se reescribe constantemente y la sincronización la corrompe.
La ruta que se usa hoy es `C:\TesisProyecto\MovU_Demo`.

---

## 3. Cómo correr el proyecto

### 3.1 Primera vez

```bash
git lfs install
git clone https://github.com/juanDiesmers/MovU_Demo.git C:\TesisProyecto\MovU_Demo
cd C:\TesisProyecto\MovU_Demo
git lfs pull            # sin esto los .obj llegan como punteros de texto de 130 bytes
pip install numpy
```

Abrir el proyecto con Unity **6000.3.21f1**. La primera importación tarda varios minutos
(reimporta todos los modelos). Si Unity pide importar **TextMeshPro Essential Resources**,
aceptar — sin eso el HUD no dibuja texto.

### 3.2 Generar las mallas auxiliares

`RellenoCorona.obj` está versionado, pero si se cambia la altura del techo o el modelo base
hay que regenerarlo:

```bash
python3 Tools/tapar_corona.py                       # valores por defecto: muro a 3,49 m
python3 Tools/tapar_corona.py --altura-muro 3.49 --banda 0.25
```

Otros scripts, solo si hacen falta:

| Script | Qué produce | Cuándo |
|---|---|---|
| `Tools/tapar_corona.py` | `RellenoCorona.obj` — sube los muros cortos + losa de techo | Siempre que cambie la altura del edificio |
| `Tools/reconstruir_planta.py` | `PlantaMovU_Reconstruida.obj` — planta reconstruida a 8k triángulos | **Descartado** como mapa jugable; se conserva como evidencia |
| `Tools/preparar_malla.py` | `PlanoMovU_Estilizado.obj` — el mismo modelo con UVs | Solo si se quieren texturas PBR reales |
| `Tools/blender_pulir_plano.py` | `PlanoMovU_Pulido.obj` — normales hacia afuera + bisel | Solo si se quiere volver a back-face culling |

### 3.3 Armar la escena — un solo menú

```
MovU > Preparar todo (recomendado)
```

Encadena los cuatro pasos en el orden correcto (planta, estilo, edificio y NavMesh), guarda el resultado en
`Assets/MovU/Scenes/Edificio.unity` (sin tocar la escena del demo), la registra en Build
Settings y al terminar dice qué falta hacer a mano. Si un paso falla, se detiene ahí en vez
de dejar la escena a medias.

Después de eso, **Play**. Los POIs, las misiones y los NPC no están en la escena: salen de
`Assets/MovU/Resources/MovU/contenido_piso9.json` y se montan solos al empezar
(`MovUBootstrap`). Ver la sección 5.

Opcional, para rendimiento:

```
MovU > Rendimiento > Quitar sombras del sol (interior)
MovU > Rendimiento > Hornear Occlusion Culling
MovU > Rendimiento > Informe de presupuesto (RD-4)
```

Y en cualquier momento:

```
MovU > Proyecto > Revisar estado de la escena
```

dice qué hay y qué falta (personaje, edificio, `Contenido`, vértices dibujándose, occlusion,
NavMesh, serialización) sin tener que abrir seis ventanas.

**Los tres pasos por separado** — sólo hacen falta para regenerar una parte:

```
1. MovU > Preparar plano Meshy jugable          → planta suelta: colliders, escala, Player, aparición
2. MovU > Estilizar entorno > Limpio (low-poly) → material, luces y niebla
3. MovU > Edificio > Construir edificio de 3 pisos → apila los pisos, ascensor y FloorManager
```

El orden sigue importando, pero ya no es un campo de minas: el paso 2 detecta si el edificio
está construido y reaplica los valores de varios pisos después del preset, y el paso 1 detecta
el edificio y pregunta antes de tocar nada. Todos son idempotentes.

Menús adicionales:

- `MovU > Juego > Hornear NavMesh de todos los pisos` — hay que repetirlo si se mueve el
  ascensor o se reconstruye el edificio. Lo guarda en `Scenes/Edificio_NavMesh.asset`.
- `MovU > Juego > Abrir carpeta de métricas (CSV)`.
- `MovU > Contenido > Ver contenido del JSON en la escena` — dibuja POIs, NPC, aparición y
  ascensor sobre la planta sin crear ningún objeto.
- `MovU > Contenido > Copiar posición de la selección como u,v` — para pasar un punto de la
  escena al JSON.
- `MovU > Contenido > Crear POI aquí` — un POI a mano, en `Contenido/Piso_N`.
- `MovU > Contenido > Llevar ascensor y aparición al sitio del JSON`.
- `MovU > Edificio > Cargar tambien los pisos vecinos` — para depurar: mantiene 3 pisos activos.
- `MovU > Estilizar entorno > Anadir suelo exterior` — plano de horizonte sin collider.
- `MovU > Estilizar entorno > Alternar culling de caras` — `Off` / `Back`.
- `MovU > Construir escena demo` — reconstruye el laberinto del demo (`DemoMaze`).
- `MovU > Generar Build de Windows`.
- `MovU > Proyecto > Guardar escena como Edificio.unity`.
- `MovU > Proyecto > Reserializar escenas y prefabs a texto` — deja la serialización en texto
  y reescribe las escenas ya guardadas en binario, para que git pueda mergearlas.

Después de construir, la consola imprime las medidas reales:
`[Edificio] Planta de 39,4 x 106,8 m. Alto de un piso: ... -> separacion ...`.
Si esos números se ven raros, la escala está mal antes de que el juego siquiera corra.

### 3.4 Controles

| Tecla | Acción |
|---|---|
| `W A S D` | Caminar (3 m/s) |
| Ratón | Mirar |
| `V` | Alternar primera / tercera persona |
| `G` | Ciclar el modo de la flecha de guía (Off → Direct → NavMesh). No hace nada si el evaluador fijó el modo |
| `E` | Junto al ascensor: abrir el panel. Frente a un NPC: pedirle indicaciones |
| `Enter` / `Espacio` | Pasar a la siguiente misión (con el panel de resultados en pantalla) |
| `1`…`9` | Elegir piso con el panel abierto |
| `Esc` | Cerrar el panel · soltar/capturar el cursor |
| `R` | En el edificio: volver al inicio de la misión en curso (la abandonada queda registrada como abortada). En el demo: recargar la escena |

Al dar Play en el edificio aparece primero el **panel de sesión**: identificador del
participante y condición de guía (libre o un modo fijo). Enter para empezar. Se quita con
`"pedirParticipante": false` en el JSON.

### 3.5 Dónde quedan las métricas

`%USERPROFILE%\AppData\LocalLow\<Company>\<Product>\movu_metrics_v2.csv`
(`MovU > Juego > Abrir carpeta de métricas (CSV)` la abre).

Una fila por **misión** en el edificio y una por corrida en el demo. Se escribe con
`InvariantCulture` (punto decimal). El archivo es `_v2` porque se añadieron columnas; el
`movu_metrics.csv` anterior no se toca.

Columnas de siempre: tiempo, distancia total, distancia y tiempo por modo de guía, ruta
óptima, índice de desvío, SPL, FPS promedio.

Columnas nuevas, al final:

| Columna | Qué es |
|---|---|
| `Scene` | Escena en la que se jugó |
| `ParticipantId` | Lo que el evaluador escribió en el panel de sesión. Sirve para cruzar con la encuesta SUS |
| `MissionId`, `MissionIndex`, `PoiId` | Qué misión y a qué POI |
| `StartFloor`, `TargetFloor` | Piso de partida y de destino, contando desde 1 **en el modelo** (no es el número que ve el jugador) |
| `FloorChanges` | Veces que cambió de piso |
| `WrongFloorVisits` | Llegadas a un piso que no era el del destino |
| `CaptureRadius` | Radio de la zona de llegada (1,5 m) |
| `NpcConsults` | Veces que pidió indicaciones a un NPC |
| `GuidanceLocked` | `true` si el evaluador fijó el modo de guía |
| `RouteUsesElevator` | `true` si la ruta óptima cambia de piso |
| `MinFPS` | El peor segundo de la misión (RD-1 pide que nunca baje de 30) |

**Ruta óptima entre pisos** (era una decisión abierta): tramo hasta el ascensor del piso de
partida + tramo desde el ascensor del piso de destino, eligiendo el ascensor que haga más
corta la suma. El viaje en ascensor no suma distancia. Es una propuesta: si el equipo define
otra cosa, se cambia en `NavUtil.RutaEntrePisos`.

---

## 4. Cómo añadir contenido sin que se lo lleve la próxima reconstrucción

Ésta es la regla más importante del proyecto ahora mismo.

**Todo lo que ustedes pongan a mano va en la raíz `Contenido`**, en el hijo del piso que le
toque: `Contenido/Piso_1`, `Contenido/Piso_2`, `Contenido/Piso_3`. Puertas, POIs, objetivos
de misión, mobiliario, luces propias — todo ahí.

Por qué:

- `MovU > Edificio > Construir edificio` **borra y rehace** la jerarquía `Edificio` entera. Lo
  que esté colgando de ahí desaparece en la siguiente reconstrucción.
- `Contenido` no se toca nunca. El menú la crea si no existe y respeta lo que tenga dentro.
- `FloorManager` enciende y apaga `Contenido/Piso_N` **junto con su piso**, así que el contenido
  respeta la carga por piso sin que haya que programar nada. Una puerta del piso 3 no se
  dibuja mientras el jugador está en el 1.

Lo que los menús generan lleva el componente **`GeneradoPorMovU`**, y la limpieza borra
exactamente eso y nada más. Si en la escena aparece geometría grande sin ese marcador (escenas
de antes de este cambio), el menú **pregunta** antes de borrarla, con la opción de conservarla.
Nunca destruye en silencio.

Si algo que pusieron a mano tiene que ser hijo del edificio por alguna razón, la alternativa es
ponerle el componente `GeneradoPorMovU` a lo que sea **regenerable** y dejar fuera lo que no.
En la duda: `Contenido`.

---

## 5. El contenido del piso es un archivo de texto

`Assets/MovU/Resources/MovU/contenido_piso9.json` define los POIs, las misiones, los NPC, el
punto de aparición y el sitio del ascensor. **Agregar una misión o un POI es añadir un bloque
ahí; no hay que tocar código ni la escena** (SRS: misiones como datos).

`Docs/piso9_contenido.png` es el mapa de lo que hay hoy.

### Posiciones: `u` y `v`

Van normalizadas sobre la planta: `u` de 0 a 1 a lo ancho (eje X) y `v` de 0 a 1 a lo largo
(eje Z). Así siguen valiendo si cambia la escala del edificio. Para sacar el `u, v` de un
punto: poner un objeto vacío ahí, seleccionarlo y
`MovU > Contenido > Copiar posición de la selección como u,v`.

### Qué hay hoy

| POI | Qué es | De dónde sale |
|---|---|---|
| `dti_prestamo` | DTI, préstamo de equipos de cómputo (el espacio F del mapa) | Identificado por el equipo |
| `dti_soporte` | DTI, ingenieros de soporte (el espacio J) | Identificado por el equipo |
| `ascensores` | Hall del extremo | Identificado por el equipo |
| `escalera_emergencia` | Espacio L | Identificado por el equipo |
| `salon_a` … `salon_h` | Los otros siete salones | **Nombre provisional**: falta el recorrido de campo |

Los dos de DTI son los espacios clave del piso y son el destino de las dos misiones:

| Misión | Enunciado | Destino |
|---|---|---|
| `M01` | Tienes clase en un rato y necesitas un portátil. Ve a DTI y pide uno prestado. | `dti_prestamo`, saliendo del ascensor |
| `M02` | Tu usuario institucional no te deja entrar. Busca a los ingenieros de soporte de DTI. | `dti_soporte`, desde donde quedó |

El **punto exacto** de cada POI (2 m adentro de la puerta) y la posición del ascensor dentro
del hall se sacaron de la malla, no de una medición en el edificio: hay que
revisarlos en Unity con `MovU > Contenido > Ver contenido del JSON en la escena`.

### NPC

18 personajes en el piso: 8 con puesto fijo (4 de DTI, 2 profesores, un vigilante y una
persona de aseo que deambula) y una multitud de 8 estudiantes y 2 visitantes que caminan
entre los puntos de `recorridos`.

- La multitud usa una **semilla** (`multitud.semilla`): con la misma, todos los participantes
  ven a la misma gente saliendo de los mismos sitios.
- Con `E` el NPC dice hacia dónde queda el destino de la misión, más o menos a cuántos metros
  y de qué lado, y se gira para señalarlo. Cada consulta suma en `NpcConsults`. Se apaga con
  `"npcDanIndicaciones": false` para las pruebas en las que no se quiera esa ayuda.
- No tienen collider: el jugador los atraviesa. Es a propósito, para que un NPC parado en una
  puerta no cambie la distancia recorrida que se mide.
- Todos comparten una malla de 91 triángulos generada por código (`NpcMeshFactory`). Los 18
  suman 1.638 triángulos; con cápsulas de Unity serían unos 29.000 y el piso se pasaría del
  presupuesto del SRS (RD-4: 100.000, de los que la planta ya gasta 94.440).

### Otros ajustes del JSON

| Campo | Qué hace |
|---|---|
| `ajustes.rotulos` | Rótulos con el nombre de cada espacio sobre su puerta. `false` para medir sin señalización |
| `ajustes.numeroDelPrimerPiso` | `9`: el piso de abajo del modelo se le muestra al jugador como "Piso 9" |
| `aparicion` | Dónde aparece el jugador (hoy, frente al ascensor del hall) |
| `ascensor` | Dónde va el ascensor. Lo lee `MovU > Preparar todo`; en una escena ya armada, `MovU > Contenido > Llevar ascensor y aparición al sitio del JSON` |
| `misiones[].partida` | `"aparicion"`, el id de un POI, o vacío para empezar donde esté el jugador |
| `misiones[].guia` | `Off`, `Direct` o `NavMesh` para arrancar la misión en ese modo; vacío para no tocarlo |

Un POI puesto a mano en la escena con el mismo `id` que uno del JSON gana sobre el del JSON.

---

## 6. Estructura del repositorio

```
Assets/MovU/
  Scenes/DemoMaze.unity         Laberinto del demo: misiones, flecha, métricas
  Scenes/Edificio.unity         El edificio de 3 pisos — la escena que se juega hoy
  Scripts/
    Core/      GameManager, DemoConfig, MovUBootstrap (monta el juego en el edificio),
               TestSession (panel del evaluador)
    Maze/      MazeGenerator, MazeGrid, MazeData      (laberinto del demo, con semilla)
    Player/    PlayerController, MouseLook, CameraRig
    Mission/   MissionManager, Objective, ObjectiveTrigger
    Guidance/  GuidanceArrow, GuidanceMode
    Data/      MetricsLogger, RunMetrics, SaveSystem,
               ContenidoPiso + ContenidoLoader (el JSON del piso)
    UI/        HUDController, HudBuilder (arma el HUD del edificio por código)
    Building/  FloorManager, ElevatorTrigger, ElevatorPanel, SolSinSombras,
               GeneradoPorMovU (marcador de lo regenerable)
    POI/       PointOfInterest (y su registro), PoiSignage (rótulos)
    Npc/       NpcManager, NpcCharacter, NpcMeshFactory
    Navigation/ NavUtil (rutas sin basura, rutas entre pisos)
  Editor/
    DemoSceneBuilder.cs     construye el laberinto del demo
    MeshiWalkableSetup.cs   deja el plano de Meshy caminable
    EnvironmentStyler.cs    presets de estilo, luces y niebla
    BuildingSetup.cs        edificio de 3 pisos, ascensor, FloorManager
    MovUPipeline.cs         'Preparar todo', guardado de escena, diagnóstico
    MovUJuegoMenu.cs        NavMesh, vista previa del JSON, POIs, rendimiento
    Tests/MovUPruebas.cs    pruebas automáticas (Test Runner > EditMode)
  Resources/MovU/contenido_piso9.json   POIs, misiones y NPC del piso
  Shaders/StylizedEnvironment.shader    shader URP que no necesita UVs
  Models/    OBJ del plano y mallas generadas
  Materials/
Docs/        mapas del piso 9 (espacios detectados y contenido)
Tools/       scripts de Python que generan las mallas
_ModelosCrudos/   fuera de Assets, ignorado por git
```

**Arquitectura:** Manager Pattern + Observer (eventos de C#). 1 unidad de Unity = 1 metro.
Objetivo de rendimiento: 60+ FPS.

---

## 7. Errores conocidos y cómo se arreglan

### Al importar / abrir

**El modelo aparece minúsculo (o 1.000× más grande).**
Es `Convert Units` del importador de OBJ. No se arregla con la escala del Transform: hay que
poner el **Scale Factor en el importador del asset**. Con archivos DWG pasa lo mismo si vienen
en milímetros: verificar unidades **antes** de cualquier otra cosa.

**Los `.obj` pesan 130 bytes y Unity no los abre.**
Falta `git lfs pull`. Lo que se descargó son punteros de LFS, no las mallas.

**Errores de compilación raros después de un `pull`.**
Cerrar Unity, borrar `Library/`, volver a abrir. Tarda, pero resuelve el 90% de los casos.

**El HUD no muestra texto.**
Falta importar TextMeshPro Essential Resources (`Window > TextMeshPro > Import TMP Essential Resources`).

### Al armar la escena

**"No hay un Player en la escena" al construir el edificio.**
`MovU > Edificio` necesita que ya exista el jugador. Correr antes
`MovU > Preparar plano Meshy jugable`.

**"Falta Assets/MovU/Models/RellenoCorona.obj".**
Correr `python3 Tools/tapar_corona.py`. Sin esa malla, al apilar los pisos se ve el piso de
arriba por encima de cada muro que se quedó corto.

**Se me borró lo que había puesto en la escena.**
Ya no debería pasar: la limpieza sólo borra objetos con el componente `GeneradoPorMovU`. Si
igual desaparece algo, es que estaba colgando de `Edificio` — muévanlo a `Contenido/Piso_N`
(sección 4). Ctrl+Z deshace la reconstrucción completa.

**El zócalo y la sombra de contacto quedan bien en el piso 1 y a media pared en el 2 y el 3.**
Es el preset de estilo pisando `_FloorLevel` y `_WallGradientHeight` con los valores de un
solo piso. Ya está resuelto: el styler detecta el `FloorManager` y reaplica los valores de
varios pisos después del preset. Si aparece igual, volver a correr
`MovU > Estilizar entorno > Limpio (low-poly)`, que ahora también repara la luz de interior.

**El entorno se ve blanco y liso, sin baldosas ni zócalo.**
Unity importó los materiales planos del `.mtl` del OBJ y quedaron pegados a las ranuras.
`EnvironmentStyler` ya pone `materialImportMode = None`; volver a correr
`MovU > Estilizar entorno > Limpio (low-poly)`.

**Huecos negros al mirar ciertos muros.**
El OBJ de Meshy trae caras con el giro invertido. El material va con culling apagado a
propósito. Si alguien lo pone en `Back`, vuelven los huecos:
`MovU > Estilizar entorno > Alternar culling de caras`. Se arregla de verdad pasando el modelo
por `Tools/blender_pulir_plano.py` (recalcula las normales hacia afuera).

**Los muros se ven "derretidos".**
Ángulo de suavizado del importador. `EnvironmentStyler` lo baja a 35°; con el 60° por defecto
una malla decimada pierde todas las esquinas.

### En ejecución

**Los botones del panel del ascensor no responden (pero las teclas 1..9 sí).**
El proyecto usa el Input System nuevo: el `EventSystem` tiene que llevar
`InputSystemUIInputModule`, no `StandaloneInputModule`. `ElevatorPanel` lo crea si falta; si
alguien dejó un EventSystem viejo en la escena, hay que borrarlo.

**El jugador aparece dentro de un muro o se cae al vacío al cambiar de piso.**
`FloorManager.LlevarAlPiso()` activa el piso destino **antes** de mover al jugador y desactiva
el `CharacterController` durante el movimiento. Si se toca ese orden, el jugador cae sobre
colisiones que todavía no existen, o el `CharacterController` reescribe la posición en su
propio `Update`.

**El interior está oscuro.**
Con el techo cerrado el sol ya no entra. La iluminación viene de `_CeilingEmission` del shader
(el techo hace de luminaria) más luz ambiental alta y niebla corta (18–70 m). Lo configura
`BuildingSetup`; si se reemplaza el material a mano, se pierde.

**No se ve el techo / se ve el piso de arriba.**
Cada piso tiene que traer su propia losa de techo. Si alguien "optimiza" usando la losa del
piso superior como techo del inferior, al apagarse ese piso quedan todos a cielo abierto.

**`¡CRÍTICO! NavMesh NO disponible` en consola.**
`DemoSceneBuilder` llama a `BuildNavMesh()` sin crear un asset de `NavMeshData`, así que el
NavMesh puede no persistir al guardar la escena. Rehornear el `NavMeshSurface`, y si el
problema se repite, persistir el `NavMeshData` como asset. En Unity 6.3 es
**`NavMeshSurface`**, no el viejo "Navigation Static".

**FPS bajos / el horneado de colisión tarda una eternidad.**
Números de referencia por piso: 94.440 triángulos de render, 78.878 de colisión. Con los tres
pisos cargados a la vez son 283.320. Si va lento: confirmar que `FloorManager` está dejando un
solo piso activo (`pisosVecinosCargados = 0`) y que el Occlusion Culling está horneado.

**Doy Play en el edificio y no hay misiones ni HUD.**
Mirar la consola: `MovUBootstrap` dice por qué no montó el juego. Los tres motivos posibles:
falta o está mal escrito `contenido_piso9.json` (una coma de más); no hay personaje en la
escena; o la escena ya trae un `MissionManager` (pasa si `Edificio.unity` se guardó a partir
de `DemoMaze`: hay que borrar de la escena `GameManager`, `MissionManager`, el Canvas del
HUD, `GuidanceArrow` y `Goal`).

**"No hay ruta por el NavMesh hasta…" al empezar una misión.**
El NavMesh no está horneado o el POI cayó dentro de un muro. Hornear con
`MovU > Juego > Hornear NavMesh de todos los pisos` y revisar el punto con
`MovU > Contenido > Ver contenido del JSON en la escena`. Mientras salga ese error, el índice
de desvío y el SPL de esa misión **no son válidos** (la columna `IsNavMeshValid` queda en
`false`).

**En el editor funciona y en el ejecutable no hay flecha por ruta.**
Dentro de Unity, si la escena no trae NavMesh, el juego lo hornea al vuelo. En el ejecutable
eso no funciona (las mallas no tienen Read/Write). Hay que hornearlo con el menú y guardar la
escena antes de hacer el build.

**El ascensor sigue dentro del salón A.**
La escena se armó antes de que el JSON dijera dónde va. Correr
`MovU > Contenido > Llevar ascensor y aparición al sitio del JSON` y volver a hornear el
NavMesh.

**Los rótulos o el HUD no muestran texto.**
Lo mismo que el HUD del demo: falta importar TextMeshPro Essential Resources.

### Git

**Conflictos imposibles en `DemoMaze.unity`.**
La escena está guardada en **binario**, aunque el proyecto ya tiene `Asset Serialization:
Force Text`: se guardó así antes y Unity no la reescribe sola. Correr una vez
`MovU > Proyecto > Reserializar escenas y prefabs a texto` y commitear el resultado (el diff
va a ser enorme esa única vez). Hasta entonces: **una sola persona toca la escena a la vez**.

**Se gasta la cuota de Git LFS.**
`*.obj` está en LFS. Antes de hacer commit, borrar lo que ya no se usa (ver la sección de
limpieza). Los modelos crudos van en `_ModelosCrudos/`, que está en `.gitignore`.

---

## 8. Qué falta (en orden)

1. **Abrir Unity y probar.** `MovU > Preparar todo` desde una escena vacía (no desde
   `DemoMaze`), Play, y jugar las dos misiones. Todo lo del 8 de octubre compila pero no se ha
   ejecutado nunca.
2. **Correr las pruebas automáticas:** `Window > General > Test Runner > EditMode > Run All`.
3. **Revisar en la vista de escena** que cada POI, el ascensor y la aparición caen donde deben
   (`MovU > Contenido > Ver contenido del JSON en la escena`) y corregir el JSON.
4. **Correr una vez** `MovU > Proyecto > Reserializar escenas y prefabs a texto` y commitear
   el resultado, antes de que alguien más toque una escena.
5. **Recorrido de campo del piso 9:** nombres reales de los salones A–E, G y H, y qué son los
   espacios que quedaron sin identificar (el K del mapa, la zona de baños).
6. **Decidir la escala definitiva** del edificio, ya con el techo puesto. Los POIs van en
   `u, v`, así que no hay que recolocarlos.
7. **Rendimiento:** `MovU > Rendimiento > Quitar sombras del sol` y medir con el Profiler
   antes y después; hornear el Occlusion Culling.
8. **Lo que falta del núcleo (H4):** menú principal, opciones, pantalla de carga y guardado
   de las misiones completadas.
9. **Puertas y marcos** (kits CC0 de Kenney) una vez validado el material base.
10. **Limpieza antes del próximo commit:**
   - `Assets/MovU/Models/PlanoMovU_Estilizado.obj` (9,5 MB) y `PlantaMovU_Reconstruida.obj`
     (1,3 MB): `Assets/meshi.unity` todavía referencia uno de los dos. Cuando exista
     `Edificio.unity` y `meshi.unity` ya no haga falta, se pueden sacar los tres.
     `MovU > Rendimiento > Informe de presupuesto` lista los modelos que la escena abierta no usa.
   - `_Previews/` y `planta_reconstruida.png` en la raíz — son diagnósticos.

Nota: el SSAO **ya está activo** en `Assets/Settings/PC_Renderer.asset`; no hay que activarlo.

---

## 9. Recursos externos

Todos CC0 (dejar constancia de la fuente en el documento igual):

- [Kenney](https://kenney.nl/assets/category:3D) — `Furniture Kit`, `Building Kit`, `Modular Buildings`
- [Quaternius](https://quaternius.com/) — `Ultimate House Interior Pack`
- [Poly Pizza](https://poly.pizza/) — piezas sueltas de los dos anteriores
- [ambientCG](https://ambientcg.com/) — texturas PBR (`Tiles012`, `Concrete010`, `Concrete012`)
- [3D Textures](https://3dtextures.me/) — concreto y baldosa
